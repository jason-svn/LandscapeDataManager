using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Settings;

public sealed partial class MainPage : Page
{
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();

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

    private async void Page_Loaded(object sender, RoutedEventArgs e)
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
