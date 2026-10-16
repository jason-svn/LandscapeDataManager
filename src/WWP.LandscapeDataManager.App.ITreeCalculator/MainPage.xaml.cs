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
    private const string SignatureEngineVersion = ITreeBatchCalculator.SignatureEngineVersion;

    private readonly ITreeApiClient _iTreeApiClient = new();
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";
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

        ReportRows.Clear();
        foreach (var item in validation.Items)
        {
            var status = PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion);
            ReportRows.Add(new InstanceReportRow(item, status));
        }

        _activeFilter = null;
        UpdateCounts();
        ApplyFilter();
        StatusText.Text = $"{validation.Items.Count:N0} planting instances validated." + DescribeAgeSync(validation.AgeSync);
    }

    /// <summary>What the automatic "age at planting + growth-year workset" update did before this scan.</summary>
    private static string DescribeAgeSync(TreeAgeSyncSummary? sync)
    {
        if (sync is not { OnGrowthWorksets: > 0 })
        {
            return string.Empty;
        }

        var notes = new List<string>
        {
            sync.Updated > 0
                ? $"Updated the age of {sync.Updated:N0} tree(s) to age at planting + their growth-year workset."
                : "Tree ages already match age at planting + their growth-year workset."
        };
        if (sync.SkippedNotEditable > 0)
        {
            notes.Add($"{sync.SkippedNotEditable:N0} tree(s) aren't editable, so their age wasn't updated.");
        }

        if (sync.SkippedGrouped > 0)
        {
            notes.Add($"{sync.SkippedGrouped:N0} grouped tree(s) don't allow different ages between group instances — run Shared Parameter Setup.");
        }

        if (sync.MissingYearsParameter > 0)
        {
            notes.Add($"{sync.MissingYearsParameter:N0} tree(s) have no !_S_PLT_TreeGrowth_Years_Number — run Import Shared Parameter.");
        }

        if (sync.CappedAtMaturity > 0)
        {
            notes.Add($"{sync.CappedAtMaturity:N0} tree(s) have stopped growing — at their type's mature size (LDS max height/width), or 25 years after planting when the type has none — so their age is capped there.");
        }

        return " " + string.Join(" ", notes);
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

    private void SelectAllRows_Click(object sender, RoutedEventArgs e) => ReportList.SelectAll();

    private async Task CalculateAsync(IReadOnlyList<InstanceReportRow> rows)
    {
        var summary = await ITreeBatchCalculator.CalculateAsync(
            GetClient(), rows.Select(row => row.Item).ToList(), _iTreeCredentialStore.Load(),
            _preferredUnitSystem, _preferredCurrency, _iTreeApiClient, _exchangeRateService);
        if (!summary.Ran)
        {
            CalculateStatusText.Text = summary.Message;
            return;
        }

        CalculateStatusText.Text = $"Calculated {summary.Succeeded:N0} unique signature(s), {summary.Failed:N0} failed, applied to {summary.Instances:N0} instance(s) in {summary.CurrencyCode}.";
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
