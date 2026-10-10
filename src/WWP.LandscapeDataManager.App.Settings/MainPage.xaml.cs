using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Settings;

public sealed partial class MainPage : Page
{
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly ITreeMyTreeKeyFetcher _myTreeKeyFetcher = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();
    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;

    public MainPage()
    {
        InitializeComponent();
    }

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

    /// <summary>Fills every card from Credential Manager and the project; also re-run after an import.</summary>
    private async Task LoadAsync()
    {
        ITreeApiKeyBox.Password = _iTreeCredentialStore.Load();
        AirtableTokenBox.Password = _airtableCredentialStore.Load();

        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        var ldsSettings = snapshot?.WwpLdsSource ?? WwpLdsAirtableSettings.CompanyDefault;
        LdsBaseIdBox.Text = ldsSettings.BaseId;
        LdsTableIdBox.Text = ldsSettings.TableIdOrName;
        LdsViewIdBox.Text = ldsSettings.ViewName ?? string.Empty;

        var importSource = snapshot?.AirtableApi ?? new AirtableApiSettings(string.Empty, string.Empty, null);
        ImportBaseIdBox.Text = importSource.BaseId;
        ImportTableIdBox.Text = importSource.TableIdOrName;
        ImportViewIdBox.Text = importSource.ViewName ?? string.Empty;

        SharedParameterFilePathBox.Text = snapshot?.SharedParameterFilePath ?? string.Empty;
        _loadedFixedRate = snapshot?.ExchangeRateOverride;

        try
        {
            var catalog = await GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
            UnitSystemBox.SelectedIndex = string.Equals(catalog.PreferredUnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            SelectComboBoxItem(CurrencyBox, catalog.PreferredCurrency);
            ShowFixedRate(_loadedFixedRate, catalog.PreferredCurrency);
        }
        catch (Exception exception)
        {
            UnitsAndCurrencyStatusText.Text = $"Could not read the current unit/currency preference: {exception.Message}";
        }
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string content) =>
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals((string)item.Content, content, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items.OfType<ComboBoxItem>().First();

    private async void SaveUnitsAndCurrency_Click(object sender, RoutedEventArgs e)
    {
        if (UnitSystemBox.SelectedItem is not RadioButton { Content: string unitSystem } ||
            CurrencyBox.SelectedItem is not ComboBoxItem { Content: string currencyCode })
        {
            return;
        }

        ExchangeRateOverride fixedRate;
        if (FixedRateCheckBox.IsChecked == true)
        {
            if (double.IsNaN(FixedRateBox.Value) || FixedRateBox.Value <= 0)
            {
                UnitsAndCurrencyStatusText.Text = $"Enter how many {currencyCode} one US dollar is worth, or untick the fixed rate.";
                return;
            }

            fixedRate = new ExchangeRateOverride(true, currencyCode, FixedRateBox.Value, NullIfBlank(FixedRateNoteBox.Text));
        }
        else
        {
            fixedRate = new ExchangeRateOverride(false, currencyCode, 0);
        }

        UnitsAndCurrencyStatusText.Text = "Saving...";
        try
        {
            var result = await PublishUnitsAndCurrencyAsync(unitSystem, currencyCode, fixedRate);
            UnitsAndCurrencyStatusText.Text = result.Message;
            if (result.Success)
            {
                _loadedFixedRate = fixedRate;
            }
        }
        catch (Exception exception)
        {
            UnitsAndCurrencyStatusText.Text = $"Failed to save: {exception.Message}";
        }
    }

    /// <summary>
    /// Writes both preferences to their dedicated project parameters and mirrors them into the
    /// settings snapshot. Publishing the currency rescales every stored cost-saved value from its
    /// own recorded rate to <paramref name="fixedRate"/> (when on) or today's rate — so changing
    /// only the fixed rate re-costs the project too.
    /// </summary>
    private async Task<UnitsAndCurrencyPublishResult> PublishUnitsAndCurrencyAsync(
        string unitSystem, string currencyCode, ExchangeRateOverride fixedRate)
    {
        var unitResult = await GetClient().SendAsync<PublishPreferredUnitSystemResult>(
            PipeCommands.PublishPreferredUnitSystem, new PublishPreferredUnitSystemRequest(unitSystem));

        var rate = ProjectExchangeRate.ResolveWithoutLookup(fixedRate, currencyCode) ?? await _exchangeRateService.GetUsdRateAsync(currencyCode);
        if (!rate.Success)
        {
            return new UnitsAndCurrencyPublishResult(false,
                $"Couldn't save currency: {rate.Error} The project currency, fixed rate, and stored cost results were left unchanged.");
        }

        await GetClient().SendAsync<PublishPreferredCurrencyResult>(
            PipeCommands.PublishPreferredCurrency, new PublishPreferredCurrencyRequest(currencyCode, rate.UsdRate));
        // Persist the settings only after Revit has successfully rescaled the stored values. This
        // prevents a failed publish from advertising a new rate for values that still use the old one.
        await ProjectSettingsSync.PushAsync(
            GetClient(), preferredUnitSystem: unitSystem, preferredCurrency: currencyCode, exchangeRateOverride: fixedRate);

        var currencyNote = $"{currencyCode} at {ProjectExchangeRate.Describe(rate, fixedRate.Note)}. Already-calculated cost-saved values were rescaled to it.";
        var message = unitResult.Warning is null
            ? $"Saved: {unitSystem} units, {currencyNote}"
            : $"Saved: {unitSystem} units, {currencyNote} {unitResult.Warning}";
        return new UnitsAndCurrencyPublishResult(true, message);
    }

    private sealed record UnitsAndCurrencyPublishResult(bool Success, string Message);

    private ExchangeRateOverride? _loadedFixedRate;

    /// <summary>Shows the fixed rate only while it is for the selected currency — a rate saved for GBP means nothing for EUR.</summary>
    private void ShowFixedRate(ExchangeRateOverride? fixedRate, string currencyCode)
    {
        var applies = fixedRate?.RateFor(currencyCode) is not null;
        FixedRateCheckBox.IsChecked = applies;
        FixedRateBox.Value = applies ? fixedRate!.UsdRate : double.NaN;
        FixedRateNoteBox.Text = applies ? fixedRate!.Note ?? string.Empty : string.Empty;
        FixedRateCurrencyText.Text = currencyCode;
        FixedRatePanel.Visibility = applies ? Visibility.Visible : Visibility.Collapsed;
    }

    private string SelectedCurrency => (CurrencyBox.SelectedItem as ComboBoxItem)?.Content as string ?? "USD";

    private void CurrencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Fires during InitializeComponent, before the fixed-rate controls exist.
        if (FixedRatePanel is not null)
        {
            ShowFixedRate(_loadedFixedRate, SelectedCurrency);
        }
    }

    private void FixedRateCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        FixedRatePanel.Visibility = FixedRateCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FixedRateCurrencyText.Text = SelectedCurrency;
    }

    private async void UseTodaysRate_Click(object sender, RoutedEventArgs e)
    {
        var rate = await _exchangeRateService.GetUsdRateAsync(SelectedCurrency);
        if (rate.Success)
        {
            FixedRateBox.Value = Math.Round(rate.UsdRate, 6);
            UnitsAndCurrencyStatusText.Text = $"Filled in today's rate, 1 USD = {rate.UsdRate:G6} {SelectedCurrency}. Adjust it if needed, then Save.";
        }
        else
        {
            UnitsAndCurrencyStatusText.Text = rate.Error ?? "Couldn't fetch today's rate.";
        }
    }

    private void SaveITreeKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ITreeApiKeyBox.Password.Trim();
        if (RememberITreeKeyCheckBox.IsChecked == true)
        {
            _iTreeCredentialStore.Save(apiKey);
            ITreeStatusText.Text = string.IsNullOrWhiteSpace(apiKey)
                ? "No i-Tree API key saved."
                : "i-Tree API key saved — every LIM tool that needs it will now read it from here.";
        }
        else
        {
            _iTreeCredentialStore.Delete();
            ITreeStatusText.Text = "i-Tree API key cleared (not saved for this Windows user).";
        }
    }

    private void ClearITreeKey_Click(object sender, RoutedEventArgs e)
    {
        _iTreeCredentialStore.Delete();
        ITreeApiKeyBox.Password = string.Empty;
        ITreeStatusText.Text = "i-Tree API key cleared.";
    }

    private async void FetchITreeKey_Click(object sender, RoutedEventArgs e)
    {
        FetchITreeKeyButton.IsEnabled = false;
        ITreeStatusText.Text = "Fetching the public key from MyTree...";
        try
        {
            var result = await _myTreeKeyFetcher.FetchAsync();
            if (result is { Success: true, Key: { Length: > 0 } key })
            {
                ITreeApiKeyBox.Password = key;
                ITreeStatusText.Text = "Fetched MyTree's public key. Review it, then Save to store it for every LIM tool.";
            }
            else
            {
                ITreeStatusText.Text = result.Error ?? "Could not fetch the MyTree key.";
            }
        }
        finally
        {
            FetchITreeKeyButton.IsEnabled = true;
        }
    }

    private void SaveAirtableToken_Click(object sender, RoutedEventArgs e)
    {
        var token = AirtableTokenBox.Password.Trim();
        if (RememberAirtableTokenCheckBox.IsChecked == true)
        {
            _airtableCredentialStore.Save(token);
            AirtableStatusText.Text = string.IsNullOrWhiteSpace(token)
                ? "No Airtable personal access token saved."
                : "Airtable personal access token saved — Importer, Refresh & Audit, and Floor Calculator will now read it from here.";
        }
        else
        {
            _airtableCredentialStore.Delete();
            AirtableStatusText.Text = "Airtable personal access token cleared (not saved for this Windows user).";
        }
    }

    private void ClearAirtableToken_Click(object sender, RoutedEventArgs e)
    {
        _airtableCredentialStore.Delete();
        AirtableTokenBox.Password = string.Empty;
        AirtableStatusText.Text = "Airtable personal access token cleared.";
    }

    private async void SaveLdsSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new WwpLdsAirtableSettings(
            LdsBaseIdBox.Text.Trim(),
            LdsTableIdBox.Text.Trim(),
            string.IsNullOrWhiteSpace(LdsViewIdBox.Text) ? null : LdsViewIdBox.Text.Trim());
        await ProjectSettingsSync.PushAsync(GetClient(), wwpLdsSource: settings);
        LdsStatusText.Text = "WWP landscape data sheet source saved — Floor Calculator will use it on its next sync.";
    }

    private async void SaveImportSourceSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AirtableApiSettings(
            ImportBaseIdBox.Text.Trim(),
            ImportTableIdBox.Text.Trim(),
            string.IsNullOrWhiteSpace(ImportViewIdBox.Text) ? null : ImportViewIdBox.Text.Trim());
        await ProjectSettingsSync.PushAsync(GetClient(), airtableApi: settings);
        ImportSourceStatusText.Text = "Planting data source saved — Excel Importer and Refresh & Audit will use it next time Airtable is selected.";
    }

    private async void BrowseLdsSource_Click(object sender, RoutedEventArgs e)
    {
        var result = await PickAirtableSourceAsync();
        if (result is { } picked)
        {
            LdsBaseIdBox.Text = picked.BaseId;
            LdsTableIdBox.Text = picked.TableIdOrName;
            LdsViewIdBox.Text = picked.ViewName ?? string.Empty;
        }
    }

    private async void BrowseImportSource_Click(object sender, RoutedEventArgs e)
    {
        var result = await PickAirtableSourceAsync();
        if (result is { } picked)
        {
            ImportBaseIdBox.Text = picked.BaseId;
            ImportTableIdBox.Text = picked.TableIdOrName;
            ImportViewIdBox.Text = picked.ViewName ?? string.Empty;
        }
    }

    private async Task<(string BaseId, string TableIdOrName, string? ViewName)?> PickAirtableSourceAsync()
    {
        var token = AirtableTokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            var noTokenDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Airtable token needed",
                Content = "Save an Airtable personal access token above first, then Browse can list your bases.",
                CloseButtonText = "OK"
            };
            await noTokenDialog.ShowAsync();
            return null;
        }

        var picker = new AirtableSourcePickerDialog { XamlRoot = XamlRoot };
        return await picker.PickAsync(token);
    }

    private async void BrowseSharedParameterFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".txt");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            SharedParameterFilePathBox.Text = file.Path;
        }
    }

    private async void SaveSharedParameterFile_Click(object sender, RoutedEventArgs e)
    {
        await ProjectSettingsSync.PushAsync(GetClient(), sharedParameterFilePath: SharedParameterFilePathBox.Text.Trim());
        SharedParameterFileStatusText.Text = "Shared parameter file path saved — Shared Parameter Setup will default to it next time.";
    }

    private RevitPipeClient GetClient() =>
        _revitClient ?? throw new InvalidOperationException("The Revit connection has not been initialized.");

    public async ValueTask DisposeAsync()
    {
        if (_revitClient is not null)
        {
            await _revitClient.DisposeAsync();
        }
    }
}
