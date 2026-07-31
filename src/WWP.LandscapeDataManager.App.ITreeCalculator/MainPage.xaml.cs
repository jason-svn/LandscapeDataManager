using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _preferredUnitSystem = "Metric";

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<InstanceReportRow> ReportRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Password = _iTreeCredentialStore.Load();
    }

    private async void Validate_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(RefreshReportAsync);

    private async Task RefreshReportAsync()
    {
        var selectedOnly = ValidationScopeBox.SelectedIndex == 1;
        var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
        var validationTask = GetClient().SendAsync<ValidatePlantingInstancesResult>(
            PipeCommands.ValidatePlantingInstances, new ValidatePlantingInstancesRequest(selectedOnly));
        await Task.WhenAll(catalogTask, validationTask);

        _preferredUnitSystem = (await catalogTask).PreferredUnitSystem;
        var validation = await validationTask;

        ReportRows.Clear();
        foreach (var item in validation.Items)
        {
            var status = PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion);
            ReportRows.Add(new InstanceReportRow(item, status));
        }

        UpdateCounts();
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
        var apiKey = ApiKeyBox.Password.Trim();
        if (rows.Count == 0)
        {
            CalculateStatusText.Text = "Nothing to calculate.";
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            CalculateStatusText.Text = "Enter your i-Tree API key first.";
            return;
        }

        if (RememberKeyCheckBox.IsChecked == true)
        {
            _iTreeCredentialStore.Save(apiKey);
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

        var writeItems = new List<InstanceParameterWriteItem>();
        foreach (var (row, (signature, _)) in rows.Zip(signedInputs))
        {
            var outcome = outcomes.GetValueOrDefault(signature);
            var status = outcome?.Error is null ? "Calculated" : "APIError";
            writeItems.AddRange(ITreeInstanceResultMapper.BuildWriteItems(
                row.UniqueId, status, outcome?.Error, signature, outcome, _preferredUnitSystem));
        }

        await GetClient().SendAsync<InstanceParameterWriteResult>(
            PipeCommands.ApplyInstanceParameterWrites,
            new InstanceParameterWriteBatch(writeItems));

        var succeeded = outcomes.Values.Count(o => o.Error is null);
        var failed = outcomes.Values.Count(o => o.Error is not null);
        CalculateStatusText.Text = $"Calculated {succeeded:N0} unique signature(s), {failed:N0} failed, applied to {rows.Count:N0} instance(s).";

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
