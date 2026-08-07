using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.HealthCheck;

public sealed partial class MainPage : Page
{
    // Must match App.ITreeCalculator (and App.SyncAudit) exactly — it's part of the input
    // signature that decides whether a stored i-Tree result is still current.
    private const string SignatureEngineVersion = "LIM-iTreeCalculator-v1";

    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly SpeciesCatalogueDatabase _speciesCatalogueDatabase = new();
    private readonly WwpLdsCoefficientDatabase _coefficientDatabase = new();

    private RevitPipeClient? _revitClient;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<HealthCheckRow> ReportRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
    }

    private async void RunHealthCheck_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var floorTask = GetClient().SendAsync<HealthCheckResult>(PipeCommands.RunHealthCheck, null);
        var plantingTask = GetClient().SendAsync<ValidatePlantingInstancesResult>(PipeCommands.ValidatePlantingInstances, null);
        await Task.WhenAll(floorTask, plantingTask);
        var floorResult = await floorTask;
        var plantingResult = await plantingTask;

        var warnings = new List<string>(floorResult.ProjectWarnings);

        if (string.IsNullOrWhiteSpace(_iTreeCredentialStore.Load()))
        {
            warnings.Add("No i-Tree API key is saved — open Settings from the LIM ribbon.");
        }

        if ((await _speciesCatalogueDatabase.GetAllAsync()).Count == 0)
        {
            warnings.Add("The i-Tree species catalogue hasn't been downloaded yet — run i-Tree Downloader.");
        }

        if ((await _coefficientDatabase.GetAllAsync()).Count == 0)
        {
            warnings.Add("The WWP landscape data sheet hasn't been synced yet — run Floor Calculator's \"Sync coefficient data.\"");
        }

        ReportRows.Clear();
        foreach (var item in plantingResult.Items)
        {
            ReportRows.Add(new HealthCheckRow(ToHealthCheckItem(item)));
        }

        foreach (var item in floorResult.FloorItems)
        {
            ReportRows.Add(new HealthCheckRow(item));
        }

        WarningsText.Text = string.Join(Environment.NewLine, warnings.Select(warning => $"• {warning}"));
        WarningsBanner.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateCounts();
        StatusText.Text = $"{floorResult.DocumentTitle}: scanned {ReportRows.Count:N0} Planting and Floor element(s).";
    });

    /// <summary>Evaluates a raw Planting scan item exactly the way i-Tree Calculator and Refresh &amp; Audit do, then reduces it to Health Check's Success/NeedsAttention verdict.</summary>
    private static HealthCheckItem ToHealthCheckItem(PlantingInstanceValidationItem item)
    {
        var status = PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion);
        if (status.Status == "Calculated")
        {
            return new HealthCheckItem(item.UniqueId, "Planting", item.FamilyName, item.TypeName, "Success", "Calculated.");
        }

        var reason = status.Details ?? (status.Status == "Ready"
            ? "All inputs are set — run i-Tree Calculator to calculate this instance."
            : status.Status);
        return new HealthCheckItem(item.UniqueId, "Planting", item.FamilyName, item.TypeName, "NeedsAttention", reason);
    }

    private void UpdateCounts()
    {
        int Count(string status) => ReportRows.Count(row => row.Status == status);
        TotalCountText.Text = ReportRows.Count.ToString("N0");
        SuccessCountText.Text = Count("Success").ToString("N0");
        NeedsAttentionCountText.Text = Count("NeedsAttention").ToString("N0");
    }

    private IReadOnlyList<string> GetSelectedUniqueIds() =>
        ReportList.SelectedItems.Cast<HealthCheckRow>().Select(row => row.UniqueId).ToList();

    private async void SelectRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.SelectElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void ZoomRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.ZoomToElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void IsolateRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.IsolateElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void ResetIsolate_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.ResetIsolation, null));

    private async void ApplyColours_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
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
