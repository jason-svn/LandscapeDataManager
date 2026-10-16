using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.GrowthProjections;

public sealed partial class MainPage : Page
{
    private static readonly IReadOnlyList<int> DefaultYears = [5, 10, 15, 20, 25];

    private readonly ITreeApiClient _iTreeApiClient = new();
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private string _pipeName = string.Empty;
    private GrowthProjectionStatus? _status;
    private List<(PlantingInstanceValidationItem Item, string Status)> _growthTrees = [];
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<GrowthYearRow> YearRows { get; } = [];
    public ObservableCollection<DesignOptionRow> OptionRows { get; } = [];
    public ObservableCollection<WorksetRow> WorksetRows { get; } = [];
    public ObservableCollection<YearCalculationRow> CalculationRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _ = RunBusyAsync(LoadAsync);
    }

    /// <summary>Years: the project's saved list, else the growth years already modelled, else 5–25.</summary>
    private async Task LoadAsync()
    {
        IReadOnlyList<int>? savedYears = null;
        try
        {
            savedYears = (await ProjectSettingsSync.PullAsync(GetClient()))?.GrowthProjectionYears;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            // Saved years are a convenience; the status below still loads.
        }

        ShowYears(savedYears is { Count: > 0 } ? savedYears : DefaultYears);
        await RefreshStatusAsync();
        if (savedYears is not { Count: > 0 } &&
            _status?.Worksets.Select(workset => workset.Year).Where(year => year > 0).ToList() is { Count: > 0 } modelled)
        {
            ShowYears(modelled);
        }
    }

    private void ShowYears(IEnumerable<int> years)
    {
        YearRows.Clear();
        foreach (var year in years.Where(year => year > 0).Distinct().Order())
        {
            YearRows.Add(new GrowthYearRow(year));
        }
    }

    private async Task RefreshStatusAsync()
    {
        _status = await GetClient().SendAsync<GrowthProjectionStatus>(PipeCommands.GetGrowthProjectionStatus);

        var ticked = OptionRows.Where(row => row.IsChecked).Select(row => row.Key).ToHashSet();
        OptionRows.Clear();
        foreach (var option in _status.Options.Where(option => option.OriginalTreeCount > 0 || option.IsActive))
        {
            OptionRows.Add(new DesignOptionRow(option) { IsChecked = ticked.Count > 0 ? ticked.Contains(option.Key) : option.IsActive });
        }

        WorksetRows.Clear();
        foreach (var workset in _status.Worksets)
        {
            WorksetRows.Add(new WorksetRow(workset));
        }

        ModelSummaryText.Text =
            $"{_status.DocumentTitle}: {_status.OriginalTreeCount:N0} original tree(s); active design option: {_status.ActiveOptionLabel}." +
            (_status.Worksets.Count == 0 ? " No growth-year worksets yet." : string.Empty);

        var warnings = new List<string>();
        if (!_status.IsWorkshared)
        {
            warnings.Add("This model isn't workshared. Growth years live on worksets — enable worksharing (Collaborate > Worksharing) first.");
        }

        if (!_status.YearsParameterBound)
        {
            warnings.Add("!_S_PLT_TreeGrowth_Years_Number isn't on the trees — run Import Shared Parameter, or the copies won't get their ages.");
        }

        if (!_status.AgeAtPlantingBound)
        {
            warnings.Add("!_S_PLT_TreeGrowth_AgeAtPlanting_Number isn't on the planting types yet — run Import Shared Parameter and fill in each type's nursery age. Until then, ages count from 0 at planting.");
        }

        if (_status.UnlinkedGrowthTrees > 0)
        {
            warnings.Add($"{_status.UnlinkedGrowthTrees:N0} tree(s) are already on growth-year worksets but weren't made by this tool (copied by hand?). They're left alone — delete them if these projections replace them.");
        }

        WarningsText.Text = string.Join(Environment.NewLine, warnings.Select(warning => $"• {warning}"));
        WarningsBanner.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunButton.IsEnabled = _status.IsWorkshared;
        UpdateScopeSummary();
    }

    private void AddYear_Click(object sender, RoutedEventArgs e)
    {
        var next = YearRows.Count == 0 ? 5 : Math.Min(GrowthYearNames.MaximumYear, YearRows.Max(row => row.Year) + 5);
        YearRows.Add(new GrowthYearRow(next));
    }

    private void RemoveYear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GrowthYearRow row })
        {
            YearRows.Remove(row);
        }
    }

    private void ScopeButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OptionsPanel.Visibility = ScopeButtons.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateScopeSummary();
    }

    private void UpdateScopeSummary()
    {
        if (_status is null || ScopeSummaryText is null)
        {
            return;
        }

        ScopeSummaryText.Text = ScopeButtons.SelectedIndex switch
        {
            0 => $"{_status.OriginalTreeCount:N0} original tree(s) in the model.",
            1 => $"{_status.SelectedOriginalCount:N0} original tree(s) selected in Revit (click Refresh after changing the selection).",
            _ => "Tick the main model and/or design options to project."
        };
    }

    private GrowthProjectionRequest BuildRequest(bool previewOnly)
    {
        var years = YearRows.Select(row => row.Year).Where(year => year > 0).Distinct().Order().ToList();
        if (years.Count == 0)
        {
            throw new InvalidOperationException("Add at least one growth year.");
        }

        var scope = ScopeButtons.SelectedIndex switch
        {
            1 => GrowthProjectionScopes.Selection,
            2 => GrowthProjectionScopes.DesignOptions,
            _ => GrowthProjectionScopes.AllTrees
        };
        var optionKeys = OptionRows.Where(row => row.IsChecked).Select(row => row.Key).ToList();
        return new GrowthProjectionRequest(years, scope, optionKeys, previewOnly);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(RefreshStatusAsync);

    private async void Preview_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var result = await GetClient().SendAsync<GrowthProjectionResult>(PipeCommands.RunGrowthProjection, BuildRequest(previewOnly: true));
        StatusText.Text = Describe(result);
    });

    /// <summary>
    /// One step: create/refresh the growth-year copies, then calculate i-Tree for every growth-year
    /// tree that's new or changed (refreshed ages change DBH, so those results go stale). One
    /// confirmation covers both; the model change is a single Revit undo step.
    /// </summary>
    private async void Run_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var request = BuildRequest(previewOnly: false);
        var preview = await GetClient().SendAsync<GrowthProjectionResult>(PipeCommands.RunGrowthProjection, request with { PreviewOnly = true });
        var changesModel = preview.Created + preview.Updated + preview.Deleted + preview.MovedToBaseline > 0;
        await ScanGrowthTreesAsync();
        var recalculateAll = RecalculateAllCheckBox.IsChecked == true;
        var waitingNow = _growthTrees.Count(tree => IsWaiting(tree.Status) || recalculateAll && tree.Status == "Calculated");
        if (!changesModel && waitingNow == 0)
        {
            StatusText.Text = "Nothing to do — the growth years are up to date and every growth-year tree is calculated (or still needs inputs).";
            return;
        }

        var steps = new List<string>();
        if (changesModel)
        {
            steps.Add("1. " + Describe(preview) + " (one Revit undo step)");
        }

        steps.Add($"{(changesModel ? "2" : "1")}. Calculate i-Tree for growth-year trees that are new or changed" +
                  (recalculateAll ? ", plus those already calculated" : string.Empty) +
                  $" — {waitingNow:N0} waiting now{(changesModel ? ", plus any the refresh changes" : string.Empty)}. Trees with identical inputs are sent once.");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create, refresh & calculate?",
            Content = string.Join(Environment.NewLine + Environment.NewLine, steps),
            PrimaryButtonText = "Go",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var status = new List<string>();
        if (changesModel)
        {
            var result = await GetClient().SendAsync<GrowthProjectionResult>(PipeCommands.RunGrowthProjection, request);
            status.Add(Describe(result));
            try
            {
                await ProjectSettingsSync.PushAsync(GetClient(), growthProjectionYears: request.Years);
            }
            catch (Exception exception)
            {
                status.Add($"(Years not saved to the project: {exception.Message})");
            }
        }

        // Re-scan after the refresh: it brings every copy's age up to date, so changed trees show as stale.
        await ScanGrowthTreesAsync();
        var targets = _growthTrees
            .Where(tree => IsWaiting(tree.Status) || recalculateAll && tree.Status == "Calculated")
            .Select(tree => tree.Item)
            .ToList();
        if (targets.Count > 0)
        {
            var summary = await ITreeBatchCalculator.CalculateAsync(
                GetClient(), targets, _iTreeCredentialStore.Load(), _preferredUnitSystem, _preferredCurrency, _iTreeApiClient, _exchangeRateService);
            status.Add(summary.Ran
                ? $"i-Tree: calculated {summary.Succeeded:N0} unique input set(s), {summary.Failed:N0} failed, written to {summary.Instances:N0} tree(s) in {summary.CurrencyCode}."
                : $"i-Tree not run: {summary.Message}");
            await ScanGrowthTreesAsync();
        }
        else
        {
            status.Add("i-Tree: nothing to calculate.");
        }

        StatusText.Text = string.Join(" ", status);
        await RefreshStatusAsync();
    });

    // ---- i-Tree by growth year ----

    private async void CheckCalculation_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(ScanGrowthTreesAsync);

    /// <summary>
    /// Scans every tree (which also brings growth-year ages up to date, capped at mature size), keeps
    /// the ones on a growth-year workset, and tallies each year's i-Tree status.
    /// </summary>
    private async Task ScanGrowthTreesAsync()
    {
        var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
        var validationTask = GetClient().SendAsync<ValidatePlantingInstancesResult>(
            PipeCommands.ValidatePlantingInstances, new ValidatePlantingInstancesRequest());
        await Task.WhenAll(catalogTask, validationTask);
        var catalog = await catalogTask;
        var validation = await validationTask;
        _preferredUnitSystem = catalog.PreferredUnitSystem;
        _preferredCurrency = catalog.PreferredCurrency;

        _growthTrees = validation.Items
            .Where(item => item.GrowthYear is not null)
            .Select(item => (item, PlantingInstanceStatusEvaluator.Evaluate(item, ITreeBatchCalculator.SignatureEngineVersion).Status))
            .ToList();

        CalculationRows.Clear();
        foreach (var year in _growthTrees.GroupBy(tree => tree.Item.GrowthYear!.Value).OrderBy(group => group.Key))
        {
            CalculationRows.Add(new YearCalculationRow(
                year.Key,
                year.Count(),
                year.Count(tree => tree.Status == "Calculated"),
                year.Count(tree => IsWaiting(tree.Status)),
                year.Count(tree => tree.Status != "Calculated" && !IsWaiting(tree.Status))));
        }

        var capped = validation.AgeSync?.CappedAtMaturity ?? 0;
        CalculationStatusText.Text = _growthTrees.Count == 0
            ? "No trees are on growth-year worksets yet — create the growth years above first."
            : $"{_growthTrees.Count:N0} tree(s) across {CalculationRows.Count:N0} growth year(s)." +
              (validation.AgeSync is { Updated: > 0 } sync ? $" Updated {sync.Updated:N0} tree age(s)." : string.Empty) +
              (capped > 0 ? $" {capped:N0} tree(s) have stopped growing (mature size, or 25 years after planting without one), so their age is capped." : string.Empty);
    }

    /// <summary>Has every input and just needs (re)calculating.</summary>
    private static bool IsWaiting(string status) => status is "Ready" or "Stale";

    private static string Describe(GrowthProjectionResult result)
    {
        var verb = result.PreviewOnly ? "Will" : "Done:";
        var parts = new List<string>
        {
            $"{result.OriginalsInScope:N0} original tree(s) in scope",
            $"move {result.MovedToBaseline:N0} to the Baseline workset",
            $"create {result.Created:N0} copies",
            $"refresh {result.Updated:N0}",
            $"remove {result.Deleted:N0} orphaned/duplicate copies"
        };
        var text = $"{verb} {string.Join(", ", parts)}.";
        if (result.WorksetsCreated.Count > 0)
        {
            text += $" {(result.PreviewOnly ? "New worksets" : "Created worksets")}: {string.Join(", ", result.WorksetsCreated)}.";
        }

        if (result.Notes.Count > 0)
        {
            text += " " + string.Join(" ", result.Notes);
        }

        return text;
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
