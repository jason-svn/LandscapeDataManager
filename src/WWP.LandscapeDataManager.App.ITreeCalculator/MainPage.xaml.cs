using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.ITreeCalculator;

public sealed partial class MainPage : Page
{
    /// <summary>
    /// Our own cache-busting version for the input signature — bump this if the calculation
    /// mapping changes in a way that should invalidate every cached result. Independent of
    /// whatever engine/database version string the i-Tree API itself reports (that's stored
    /// verbatim in the EngineVersion tracking parameter instead).
    /// </summary>
    private const string SignatureEngineVersion = "LIM-iTreeCalculator-v1";

    private readonly ITreeApiClient _iTreeApiClient = new();
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";
    private bool _suppressCurrencyChange;
    private string? _activeFilter;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<InstanceReportRow> ReportRows { get; } = [];

    /// <summary>The subset of <see cref="ReportRows"/> currently shown in the list — everything when <see cref="_activeFilter"/> is null, or just one status when a summary card is active.</summary>
    public ObservableCollection<InstanceReportRow> FilteredRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SiblingToolLauncher.ShowOrStart(Path.Combine("Settings", "WWP.LandscapeDataManager.Settings.exe"), _pipeName);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed to open Settings: {exception.Message}";
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_iTreeCredentialStore.Load()))
        {
            StatusText.Text = "No i-Tree API key saved — open Settings from the LIM ribbon first.";
        }
    }

    private async void Validate_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(RefreshReportAsync);

    /// <summary>Persists the currency choice to Project Information immediately, the same way it's read back on every Refresh — so it travels with the project file for the next person who opens it.</summary>
    private async void CurrencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCurrencyChange || CurrencyBox.SelectedItem is not ComboBoxItem { Content: string code })
        {
            return;
        }

        _preferredCurrency = code;
        await RunBusyAsync(async () =>
        {
            await GetClient().SendAsync<PublishPreferredCurrencyResult>(
                PipeCommands.PublishPreferredCurrency, new PublishPreferredCurrencyRequest(code));
            await ProjectSettingsSync.PushAsync(GetClient(), preferredCurrency: code, preferredUnitSystem: _preferredUnitSystem);
            StatusText.Text = $"Preferred currency set to {code}.";
        });
    }

    private async Task RefreshReportAsync()
    {
        var selectedOnly = ValidationScopeBox.SelectedIndex == 1;
        var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
        var validationTask = GetClient().SendAsync<ValidatePlantingInstancesResult>(
            PipeCommands.ValidatePlantingInstances, new ValidatePlantingInstancesRequest(selectedOnly));
        await Task.WhenAll(catalogTask, validationTask);

        var catalog = await catalogTask;
        _preferredUnitSystem = catalog.PreferredUnitSystem;
        _preferredCurrency = catalog.PreferredCurrency;
        var validation = await validationTask;

        _suppressCurrencyChange = true;
        try
        {
            CurrencyBox.SelectedItem = CurrencyBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals((string)item.Content, _preferredCurrency, StringComparison.OrdinalIgnoreCase))
                ?? CurrencyBox.Items.OfType<ComboBoxItem>().First();
        }
        finally
        {
            _suppressCurrencyChange = false;
        }

        ReportRows.Clear();
        foreach (var item in validation.Items)
        {
            var status = PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion);
            ReportRows.Add(new InstanceReportRow(item, status));
        }

        _activeFilter = null;
        UpdateCounts();
        ApplyFilter();
        StatusText.Text = $"{validation.Items.Count:N0} planting instances validated.";
    }

    private void UpdateCounts()
    {
        int Count(string status) => ReportRows.Count(row => row.Status == status);
        ReadyCountText.Text = Count("Ready").ToString("N0");
        StaleCountText.Text = Count("Stale").ToString("N0");
        CalculatedCountText.Text = Count("Calculated").ToString("N0");
        MissingCountText.Text = Count("MissingInput").ToString("N0");
        InvalidCountText.Text = Count("InvalidInput").ToString("N0");
        WarningCountText.Text = Count("APIWarning").ToString("N0");
        ErrorCountText.Text = Count("APIError").ToString("N0");
    }

    /// <summary>Clicking the active card again clears the filter; clicking a different one switches to it.</summary>
    private void StatusCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var status = (string)((FrameworkElement)sender).Tag;
        _activeFilter = _activeFilter == status ? null : status;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredRows.Clear();
        foreach (var row in ReportRows.Where(row => _activeFilter is null || row.Status == _activeFilter))
        {
            FilteredRows.Add(row);
        }

        foreach (var card in new[] { ReadyCard, StaleCard, CalculatedCard, MissingCard, InvalidCard, WarningCard, ErrorCard })
        {
            var isActive = _activeFilter is not null && (string)card.Tag == _activeFilter;
            if (isActive)
            {
                card.BorderBrush = (Brush)Application.Current.Resources["LimAccentBrush"];
                card.BorderThickness = new Thickness(2);
            }
            else
            {
                card.ClearValue(Border.BorderBrushProperty);
                card.ClearValue(Border.BorderThicknessProperty);
            }
        }

        FilterStatusText.Text = _activeFilter is null
            ? $"Showing all {ReportRows.Count:N0} row(s) — click a card above to filter the list to just that status."
            : $"Showing {FilteredRows.Count:N0} {_activeFilter} row(s) — click the card again to show all.";
    }

    private IReadOnlyList<string> GetSelectedUniqueIds() =>
        ReportList.SelectedItems.Cast<InstanceReportRow>().Select(row => row.UniqueId).ToList();

    private async void SelectRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.SelectElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void ZoomRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.ZoomToElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void IsolateRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.IsolateElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void ResetIsolate_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.ResetIsolation, null));

    private async void ColourOverrides_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var statusByUniqueId = ReportRows.ToDictionary(row => row.UniqueId, row => row.Status);
        var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ApplyStatusColourOverrides, new StatusColourOverrideRequest(statusByUniqueId));
        StatusText.Text = result.Message ?? "Colour overrides applied.";
    });

    private async void ResetColours_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var allIds = ReportRows.Select(row => row.UniqueId).ToList();
        var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ResetColourOverrides, new ElementSelectionRequest(allIds));
        StatusText.Text = result.Message ?? "Colour overrides reset.";
    });

    private async void CalculateValid_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => CalculateAsync(ReportRows.Where(row => row.CanCalculate).ToList()));

    private async void RecalculateSelected_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => CalculateAsync(ReportList.SelectedItems.Cast<InstanceReportRow>().ToList()));

    private async Task CalculateAsync(IReadOnlyList<InstanceReportRow> rows)
    {
        var apiKey = _iTreeCredentialStore.Load().Trim();
        if (rows.Count == 0)
        {
            CalculateStatusText.Text = "Nothing to calculate.";
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            CalculateStatusText.Text = "No i-Tree API key saved — open Settings from the LIM ribbon first.";
            return;
        }

        var signedInputs = rows.Select(row => (
            Signature: PlantingInstanceStatusEvaluator.Evaluate(row.Item, SignatureEngineVersion).InputSignature,
            Input: new ITreeRevitInput(
                row.Item.SpeciesCode ?? string.Empty, string.Empty, string.Empty,
                row.Item.FamilyName, row.Item.TypeName, 0,
                row.Item.Condition ?? string.Empty,
                row.Item.DbhInches ?? 0,
                row.Item.Latitude ?? 0,
                row.Item.Longitude ?? 0,
                row.Item.Years ?? 1,
                row.Item.CrownExposure ?? 0))).ToList();

        var outcomes = await _iTreeApiClient.CalculateForInstancesAsync(signedInputs, apiKey);

        // One rate lookup per batch, not per tree — the rate doesn't vary by tree, only by currency.
        var exchangeRate = await _exchangeRateService.GetUsdRateAsync(_preferredCurrency);

        var writeItems = new List<InstanceParameterWriteItem>();
        foreach (var (row, (signature, _)) in rows.Zip(signedInputs))
        {
            var outcome = outcomes.GetValueOrDefault(signature);
            var status = outcome?.Error is null ? "Calculated" : "APIError";
            writeItems.AddRange(ITreeInstanceResultMapper.BuildWriteItems(
                row.UniqueId, status, outcome?.Error, signature, outcome,
                _preferredUnitSystem, exchangeRate.CurrencyCode, exchangeRate.UsdRate));
        }

        await GetClient().SendAsync<InstanceParameterWriteResult>(
            PipeCommands.ApplyInstanceParameterWrites,
            new InstanceParameterWriteBatch(writeItems));

        var succeeded = outcomes.Values.Count(o => o.Error is null);
        var failed = outcomes.Values.Count(o => o.Error is not null);
        var rateNote = exchangeRate.Success ? string.Empty : $" ({exchangeRate.Error})";
        CalculateStatusText.Text = $"Calculated {succeeded:N0} unique signature(s), {failed:N0} failed, applied to {rows.Count:N0} instance(s) in {exchangeRate.CurrencyCode}.{rateNote}";

        await RefreshReportAsync();
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"iTree QC Report {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("ElementId,UniqueId,FamilyType,SpeciesCode,Years,Status,Details,LastUpdated");
        foreach (var row in ReportRows)
        {
            builder.AppendLine(string.Join(',',
                row.ElementId, row.UniqueId, Csv(row.FamilyType), row.SpeciesCode, row.Years,
                row.Status, Csv(row.Details), row.LastUpdated));
        }

        await Windows.Storage.FileIO.WriteTextAsync(file, builder.ToString());
        StatusText.Text = $"Exported {ReportRows.Count:N0} rows to {file.Name}.";
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed: {exception.Message}";
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
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
