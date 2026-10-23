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
    private readonly AirtableApiClient _airtableApiClient = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();

    private RevitPipeClient? _revitClient;
    private string _pipeName = string.Empty;
    private GrowthProjectionStatus? _status;
    private List<(PlantingInstanceValidationItem Item, string Status)> _growthTrees = [];

    // What the last tree scan noticed (ages updated or capped, trees on the wrong workset, DBH that
    // doesn't grow), shown under the by-design-option table.
    private string _scanNotes = string.Empty;
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";
    private IReadOnlyList<TypeGrowthRates> _growthRates = [];
    private IReadOnlyDictionary<long, RateStatus> _typeRateStatus = new Dictionary<long, RateStatus>();
    private IReadOnlyDictionary<long, RateStatus> _sheetRateStatus = new Dictionary<long, RateStatus>();
    private OrganizerTreesResult? _organizerTrees;
    private TreeSortPlan? _sortPlan;
    private IReadOnlyList<TreeOverlapGroup> _overlaps = [];
    private IReadOnlyList<string> _redTreeIds = [];

    public MainPage()
    {
        InitializeComponent();
        StepHintText.Text = StepHints[0];
    }

    public ObservableCollection<GrowthYearRow> YearRows { get; } = [];
    public ObservableCollection<DesignOptionRow> OptionRows { get; } = [];
    public ObservableCollection<WorksetRow> WorksetRows { get; } = [];
    public ObservableCollection<GrowthRateRow> GrowthRateRows { get; } = [];
    public ObservableCollection<OptionProgressRow> OptionProgressRows { get; } = [];
    public ObservableCollection<SortMoveRow> SortMoveRows { get; } = [];
    public ObservableCollection<OverlapRow> OverlapRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _ = RunBusyAsync(LoadAsync);
    }

    /// <summary>Years: the project's saved list, else the growth years already modelled, else 5–25; the design year: the project's, else 25.</summary>
    private async Task LoadAsync()
    {
        // The defaults first, so a failure below never leaves the list empty.
        ShowYears(DefaultYears);
        IReadOnlyList<int>? savedYears = null;
        try
        {
            var settings = await ProjectSettingsSync.PullAsync(GetClient());
            savedYears = settings?.GrowthProjectionYears;
            DesignYearBox.Value = settings?.GrowthDesignYear ?? GrowthYearNames.DefaultDesignYear;
            if (savedYears is { Count: > 0 } && settings?.GrowthDesignYear is not null)
            {
                SetStep(0, StepState.Done);
            }
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
            WorksetRows.Add(new WorksetRow(workset, DesignYear()));
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
            warnings.Add("!_S_PLT_TreeGrowth_ProjectedYears_Number isn't on the trees — run Import Shared Parameter, or the copies won't get their growth year.");
        }

        if (!_status.AgeAtPlantingBound)
        {
            warnings.Add("!_S_PLT_TreeGrowth_AgeAtPlanting_Number isn't on the trees yet — run Import Shared Parameter (it moves the old type value onto each tree) and fill in each tree's age at planting. Until then, ages count from 0 at planting.");
        }

        if (_status.GroupedTrees > 0)
        {
            warnings.Add($"{_status.GroupedTrees:N0} tree(s) are in groups. Trees in a group share one age and growth year, so they're skipped here — ungroup them (Health Check lists them).");
        }

        if (_status.UnlinkedGrowthTrees > 0)
        {
            warnings.Add($"{_status.UnlinkedGrowthTrees:N0} tree(s) are on a growth year (on its workset, or given a projected year) but weren't made by this tool, e.g. placed by hand. Create & refresh leaves them alone; Sort & check trees moves them onto their year's workset and finds any that overlap.");
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
        var ticked = OptionRows.Where(row => row.IsChecked).Select(row => row.Label.Split(" — ")[0]).ToList();
        RunScopeText.Text = ScopeButtons.SelectedIndex switch
        {
            0 => $"Trees: all {_status.OriginalTreeCount:N0} original trees in the project, every design option.",
            1 => $"Trees: the {_status.SelectedOriginalCount:N0} original tree(s) selected in Revit.",
            _ => $"Trees: the ticked design options ({(ticked.Count > 0 ? string.Join(", ", ticked) : "none")})."
        };
    }

    /// <summary>Copies made and trees calculated per batch, so a large project never holds Revit in one huge change.</summary>
    private const int BatchSize = 500;

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
        return new GrowthProjectionRequest(years, scope, optionKeys, previewOnly, DesignYear());
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(RefreshStatusAsync);

    private async void Preview_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(PreviewRunAsync);

    /// <summary>
    /// What Create & refresh would do. Done when every original is on its year and has its copies
    /// (refreshing them is routine), none of them waiting for another design option to be active.
    /// </summary>
    private async Task PreviewRunAsync()
    {
        var result = await GetClient().SendAsync<GrowthProjectionResult>(PipeCommands.RunGrowthProjection, BuildRequest(previewOnly: true));
        StatusText.Text = Describe(result, DesignYear());
        var options = await ShowOptionProgressAsync();
        // Done when every option has its copies and every tree i-Tree can calculate is calculated.
        var allDone = options.Count > 0
            ? options.All(option => option.ToDo == GrowthOptionProgressRow.Done)
            : result.Created == 0 && !result.HasCopiesWaiting;
        SetStep(3, allDone && result.Deleted + result.MovedToBaseline == 0 ? StepState.Done : StepState.Attention);
    }

    /// <summary>
    /// Each design option's originals, the copies made of them, and the trees still waiting for i-Tree,
    /// with what to do next: run, or make the option active first. Reads only.
    /// </summary>
    private async Task<IReadOnlyList<GrowthOptionProgressRow>> ShowOptionProgressAsync()
    {
        // The active option may have changed in Revit since the step opened.
        await RefreshStatusAsync();
        var organizer = await GetClient().SendAsync<OrganizerTreesResult>(PipeCommands.GetOrganizerTrees);
        await ScanGrowthTreesAsync();
        var waiting = _growthTrees.Where(tree => IsWaiting(tree.Status)).Select(tree => tree.Item.UniqueId).ToHashSet(StringComparer.Ordinal);
        var needingInputs = _growthTrees
            .Where(tree => tree.Status != "Calculated" && !IsWaiting(tree.Status))
            .Select(tree => tree.Item.UniqueId)
            .ToHashSet(StringComparer.Ordinal);
        var activeKey = _status?.Options.FirstOrDefault(option => option.IsActive)?.Key ?? GrowthOptionKeys.MainModel;
        var rows = GrowthOptionProgress.Build(organizer.Trees, BuildRequest(previewOnly: true).Years, DesignYear(), activeKey, waiting, needingInputs);

        OptionProgressRows.Clear();
        foreach (var row in rows)
        {
            OptionProgressRows.Add(new OptionProgressRow(row));
        }

        var makeActive = rows.Where(row => row.ToDo == GrowthOptionProgressRow.MakeActive).Select(row => row.OptionLabel).ToList();
        var run = rows.Where(row => row.ToDo == GrowthOptionProgressRow.Run).Select(row => row.OptionLabel).ToList();
        var needsInputs = rows.Sum(row => row.NeedsInputs);
        OptionProgressText.Text = (makeActive.Count + run.Count == 0
                                      ? "Every design option has all its growth-year copies, and every tree i-Tree can calculate is calculated."
                                      : (run.Count > 0 ? $"Run Create, refresh & calculate for: {string.Join(", ", run)}. " : string.Empty) +
                                        (makeActive.Count > 0 ? $"Then make each of these active in turn and run again: {string.Join(", ", makeActive)}." : string.Empty)) +
                                  (needsInputs > 0 ? $" {needsInputs:N0} tree(s) need inputs before i-Tree can calculate them (species, DBH, condition or location)." : string.Empty) +
                                  _scanNotes;
        return rows;
    }

    /// <summary>
    /// One step: create/refresh the growth-year copies, then calculate i-Tree for every growth-year
    /// tree that's new or changed (refreshed ages change DBH, so those results go stale). One
    /// confirmation covers both. Copies are made <see cref="BatchSize"/> at a time, each batch its own
    /// Revit undo step; i-Tree is calculated and written <see cref="BatchSize"/> trees at a time.
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
            StatusText.Text = preview.HasCopiesWaiting
                ? "Nothing to do in the active design option. " + string.Join(" ", preview.Notes)
                : "Nothing to do — the growth years are up to date and every growth-year tree is calculated (or still needs inputs).";
            SetStep(3, preview.HasCopiesWaiting ? StepState.Attention : StepState.Done);
            return;
        }

        var steps = new List<string>();
        if (changesModel)
        {
            steps.Add("1. " + Describe(preview, request.DesignYear) +
                      (preview.Created > BatchSize ? $" (in batches of {BatchSize:N0} copies, one Revit undo step each)" : " (one Revit undo step)"));
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
        var copiesWaiting = preview.HasCopiesWaiting;
        if (changesModel)
        {
            // The first batch also moves originals onto their year, refreshes the existing copies and
            // makes the worksets; the later ones only make copies. An older connector makes them all at once.
            var first = await GetClient().SendAsync<GrowthProjectionResult>(
                PipeCommands.RunGrowthProjection, request with { MaxCopies = BatchSize, RefreshCopies = true });
            var last = first;
            var made = first.Created;
            while (last.CopiesLeft > 0 && last.Created > 0)
            {
                StatusText.Text = $"Making copies… {made:N0} made, {last.CopiesLeft:N0} to go.";
                last = await GetClient().SendAsync<GrowthProjectionResult>(
                    PipeCommands.RunGrowthProjection, request with { MaxCopies = BatchSize, RefreshCopies = false });
                made += last.Created;
            }

            var result = first with { Created = made, Notes = last.Notes, WaitingForOption = last.WaitingForOption, CopiesLeft = last.CopiesLeft };
            status.Add(Describe(result, request.DesignYear));
            copiesWaiting = result.HasCopiesWaiting;
            try
            {
                await ProjectSettingsSync.PushAsync(GetClient(), growthProjectionYears: request.Years, growthDesignYear: request.DesignYear);
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
            int succeeded = 0, failed = 0, written = 0, batches = 0;
            string? currency = null, timeline = null, notRun = null;
            foreach (var chunk in targets.Chunk(BatchSize))
            {
                StatusText.Text = $"i-Tree: {written:N0} of {targets.Count:N0} tree(s) calculated…";
                var summary = await ITreeBatchCalculator.CalculateAsync(
                    GetClient(), chunk, _iTreeCredentialStore.Load(), _preferredUnitSystem, _preferredCurrency, _iTreeApiClient, _exchangeRateService);
                if (!summary.Ran)
                {
                    notRun = summary.Message;
                    break;
                }

                succeeded += summary.Succeeded;
                failed += summary.Failed;
                written += summary.Instances;
                currency = summary.CurrencyCode;
                timeline = summary.Timeline?.Describe();
                batches++;
            }

            status.Add(notRun is not null && batches == 0
                ? $"i-Tree not run: {notRun}"
                : $"i-Tree: calculated {succeeded:N0} unique input set(s), {failed:N0} failed, written to {written:N0} tree(s) in {currency}" +
                  (batches > 1 ? $", in {batches:N0} batches" : string.Empty) + $". {timeline}" +
                  (notRun is not null ? $" Stopped: {notRun}" : string.Empty));
            await ScanGrowthTreesAsync();
        }
        else
        {
            status.Add("i-Tree: nothing to calculate.");
        }

        StatusText.Text = string.Join(" ", status);
        var options = await ShowOptionProgressAsync();
        SetStep(3, copiesWaiting || options.Any(option => option.ToDo != GrowthOptionProgressRow.Done) ? StepState.Attention : StepState.Done);
    });

    // ---- i-Tree by growth year ----

    /// <summary>
    /// Scans every tree (which also brings growth-year ages up to date, capped at mature size), keeps
    /// the ones on a growth-year workset with their i-Tree status, and notes what needs attention.
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

        var capped = validation.AgeSync?.CappedAtMaturity ?? 0;
        var flatDbh = GrowthProjectionChecks.TypesWithUnchangingDbh(_growthTrees.Select(tree => tree.Item));
        if (flatDbh.Count > 0)
        {
            SetStep(2, StepState.Attention);
        }

        _scanNotes =
              (validation.AgeSync is { Updated: > 0 } sync ? $" Updated {sync.Updated:N0} tree(s) to their projected year." : string.Empty) +
              (capped > 0 ? $" {capped:N0} tree(s) without a family age formula have reached mature size, so LIM capped their age." : string.Empty) +
              (validation.AgeSync is { NotOnTheirWorkset: > 0 } misplaced
                  ? $" {misplaced.NotOnTheirWorkset:N0} tree(s) have a projected year that isn't their workset's: step 2 (Check trees) moves them."
                  : string.Empty) +
              (flatDbh.Count > 0
                  ? $" ⚠ DBH is the same in every growth year for {flatDbh.Count:N0} type(s) ({string.Join(", ", flatDbh.Take(5))}{(flatDbh.Count > 5 ? ", …" : string.Empty)}), so i-Tree will give every year the same results — fill in their trunk growth rate (!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number) in step 3 (Growth rates)."
                  : string.Empty);
    }

    // ---- Sort & check trees ----

    private async void CheckTrees_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(CheckTreesAsync);

    /// <summary>Reads every tree, lists the ones on another workset than their year's, and finds and reddens overlapping ones.</summary>
    private async Task CheckTreesAsync()
    {
        _organizerTrees = await GetClient().SendAsync<OrganizerTreesResult>(PipeCommands.GetOrganizerTrees);
        ShowSortPlan(_organizerTrees);
        await ShowOverlapsAsync();
        SetStep(1, _sortPlan is { Moves.Count: 0, Problems.Count: 0 } && _overlaps.Count == 0 ? StepState.Done : StepState.Attention);
    }

    private void ShowSortPlan(OrganizerTreesResult trees)
    {
        var designYear = DesignYear();
        var plan = _sortPlan = TreeSortPlanner.Plan(trees.Trees, designYear);
        SortMoveRows.Clear();
        foreach (var group in plan.Moves
                     .GroupBy(move => (From: move.Tree.WorksetName, move.Year))
                     .OrderBy(group => group.Key.Year)
                     .ThenBy(group => group.Key.From, StringComparer.OrdinalIgnoreCase))
        {
            var exists = trees.YearWorksets.TryGetValue(group.Key.Year, out var to);
            SortMoveRows.Add(new SortMoveRow(group.Key.From, exists ? to! : GrowthYearNames.WorksetName(group.Key.Year), group.Count(), !exists));
        }

        SortTable.Visibility = SortMoveRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SortTreesButton.IsEnabled = trees.IsWorkshared && plan.Moves.Count > 0;
        var problems = plan.Problems.Count > 0
            ? $" {plan.Problems.Count:N0} can't be sorted: {string.Join("; ", plan.Problems.Take(3).Select(problem => $"{problem.Tree.TypeName} ({problem.Tree.ElementId}): {problem.Reason}"))}{(plan.Problems.Count > 3 ? " …" : string.Empty)}"
            : string.Empty;
        var byPhase = plan.YearFromPhase > 0
            ? $" {plan.YearFromPhase:N0} tree(s) have no projected year of their own, so their phase decides: existing trees year 0, new trees year {designYear} (the design year)."
            : string.Empty;
        SortSummaryText.Text = !trees.IsWorkshared
            ? "This model isn't workshared, so there are no growth-year worksets to sort into."
            : (plan.Moves.Count == 0
                  ? "Every tree is on its year's workset."
                  : $"{plan.Moves.Count:N0} tree(s) aren't on their year's workset yet, or need their projected year written.") + problems + byPhase;
    }

    private async void OverlapToleranceBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => await RefreshOverlapsAsync();

    private async void SameTypeOnly_Changed(object sender, RoutedEventArgs e) => await RefreshOverlapsAsync();

    private async Task RefreshOverlapsAsync()
    {
        if (_organizerTrees is not null && OverlapInfoBar is not null)
        {
            await RunBusyAsync(ShowOverlapsAsync);
        }
    }

    /// <summary>Finds overlapping trees in the last Check and paints the ones to delete red in the active view (clearing the last ones painted).</summary>
    private async Task ShowOverlapsAsync()
    {
        if (_organizerTrees is null)
        {
            return;
        }

        var tolerance = double.IsNaN(OverlapToleranceBox.Value) ? 0.1 : OverlapToleranceBox.Value;
        var sameTypeOnly = SameTypeOnlyCheckBox.IsChecked == true;
        _overlaps = TreeOverlapFinder.Find(_organizerTrees.Trees, tolerance, sameTypeOnly);
        OverlapRows.Clear();
        foreach (var group in _overlaps)
        {
            OverlapRows.Add(new OverlapRow(group));
        }

        var extras = _overlaps.SelectMany(group => group.Extras).Select(tree => tree.UniqueId).ToList();
        if (_redTreeIds.Count > 0)
        {
            await GetClient().SendAsync<OperationResult>(PipeCommands.ResetColourOverrides, new ElementSelectionRequest(_redTreeIds));
        }

        if (extras.Count > 0)
        {
            await GetClient().SendAsync<OperationResult>(
                PipeCommands.ApplyStatusColourOverrides,
                new StatusColourOverrideRequest(extras.ToDictionary(id => id, _ => "Overlap", StringComparer.Ordinal)));
        }

        _redTreeIds = extras;
        OverlapTable.Visibility = _overlaps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectOverlapsButton.IsEnabled = extras.Count > 0;
        ResetOverlapColoursButton.IsEnabled = extras.Count > 0;
        OverlapInfoBar.IsOpen = extras.Count > 0;
        OverlapInfoBar.Title = $"{extras.Count:N0} overlapping tree(s), shown red in the active view";
        OverlapInfoBar.Message =
            $"In {_overlaps.Count:N0} place(s), trees stand within {tolerance:0.###} m of another {(sameTypeOnly ? "of the same type " : string.Empty)}on the same workset in the same design option. " +
            "One tree in each place is kept (the original its growth-year copies project, else the oldest). Delete the red ones?";
        OverlapSummaryText.Text = extras.Count > 0
            ? "Each row is one place where trees overlap. Select the red trees to look at them in Revit first."
            : $"No trees overlap within {tolerance:0.###} m on the same workset in the same design option.";
    }

    private async void SortTrees_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        if (_sortPlan is not { Moves.Count: > 0 } plan)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Sort trees into worksets?",
            Content = $"Moves {plan.Moves.Count:N0} tree(s) onto their projected year's workset: " +
                      string.Join(", ", SortMoveRows.GroupBy(row => row.ToText).Select(group => $"{group.Sum(row => row.Trees):N0} to {group.Key}{(group.First().IsNewWorkset ? " (new)" : string.Empty)}")) +
                      ". One Revit undo step. Afterwards, Create, refresh & calculate brings their i-Tree results up to date.",
            PrimaryButtonText = "Sort",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await GetClient().SendAsync<TreeSortResult>(
            PipeCommands.SortTreesIntoWorksets,
            new TreeSortRequest(plan.Moves.Select(move => new TreeSortItem(move.Tree.UniqueId, move.Year)).ToList()));
        await RefreshStatusAsync();
        await CheckTreesAsync();
        SortSummaryText.Text = $"Moved {result.Moved:N0} tree(s)." +
                               (result.WorksetsCreated.Count > 0 ? $" Created {string.Join(", ", result.WorksetsCreated)}." : string.Empty) +
                               (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : string.Empty) +
                               " " + SortSummaryText.Text;
    });

    private async void DeleteOverlaps_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var extras = _overlaps.SelectMany(group => group.Extras).ToList();
        if (extras.Count == 0 || _organizerTrees is null)
        {
            return;
        }

        var leaving = extras.Select(tree => tree.UniqueId).ToHashSet(StringComparer.Ordinal);
        var copies = _organizerTrees.Trees.Count(tree =>
            !leaving.Contains(tree.UniqueId) && tree.LinkedSourceUniqueId is { } source && leaving.Contains(source));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {extras.Count:N0} overlapping tree(s)?",
            Content = $"Keeps one tree in each of the {_overlaps.Count:N0} place(s) and deletes the {extras.Count:N0} red one(s)" +
                      (copies > 0 ? $", plus the {copies:N0} growth-year copies Growth Projections made of them" : string.Empty) +
                      ". One Revit undo step.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await GetClient().SendAsync<DeleteTreesResult>(PipeCommands.DeleteTrees, new DeleteTreesRequest(leaving.ToList()));
        await RefreshStatusAsync();
        await CheckTreesAsync();
        OverlapSummaryText.Text = $"Deleted {result.Deleted:N0} tree(s)" +
                                  (result.CopiesDeleted > 0 ? $" and {result.CopiesDeleted:N0} growth-year copies of them" : string.Empty) + "." +
                                  (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : string.Empty) +
                                  " " + OverlapSummaryText.Text;
    });

    private async void SelectOverlaps_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(() =>
        GetClient().SendAsync<OperationResult>(PipeCommands.SelectElements, new ElementSelectionRequest(_redTreeIds)));

    private async void ResetOverlapColours_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        if (_redTreeIds.Count > 0)
        {
            await GetClient().SendAsync<OperationResult>(PipeCommands.ResetColourOverrides, new ElementSelectionRequest(_redTreeIds));
        }

        _redTreeIds = [];
        ResetOverlapColoursButton.IsEnabled = false;
        OverlapSummaryText.Text = "Colours reset. Select Check to paint the overlapping trees again.";
    });

    // ---- Growth rates from i-Tree ----

    private async void GetGrowthRates_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(GetGrowthRatesAsync);

    /// <summary>
    /// Every planting type with a species code, with the condition and crown exposure most of its
    /// planted trees have, and the project's i-Tree location; null (after saying why) when there's
    /// nothing to rate. Reads the model only (the tree scan also brings growth-year ages up to date,
    /// as Check does).
    /// </summary>
    private async Task<(IReadOnlyList<TypeGrowthRates> Plan, double Latitude, double Longitude)?> PlanGrowthRatesAsync()
    {
        var scanTask = GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, new ModelScanOptions(PrimaryDesignOptionsOnly: false));
        var validationTask = GetClient().SendAsync<ValidatePlantingInstancesResult>(
            PipeCommands.ValidatePlantingInstances, new ValidatePlantingInstancesRequest());
        await Task.WhenAll(scanTask, validationTask);
        var trees = (await validationTask).Items;
        var plan = GrowthRatesFromITree.Plan((await scanTask).Items, trees);
        if (plan.Count == 0)
        {
            ShowGrowthRates([], "No planting types have a species code yet: assign them in Tree Searcher first.");
            return null;
        }

        var location = trees.FirstOrDefault(tree => tree.Latitude is not null && tree.Longitude is not null);
        if (location is null)
        {
            ShowGrowthRates([], "The project has no i-Tree location: set it with Location Finder first.");
            return null;
        }

        return (plan, location.Latitude!.Value, location.Longitude!.Value);
    }

    /// <summary>
    /// Opening the step: the rates i-Tree gave last time, saved on the project, and where each type
    /// stands. No i-Tree request; a type never rated, or whose inputs changed since, says to Get rates.
    /// </summary>
    private async Task LoadSavedGrowthRatesAsync()
    {
        if (await PlanGrowthRatesAsync() is not var (plan, latitude, longitude))
        {
            return;
        }

        var saved = (await ProjectSettingsSync.PullAsync(GetClient()))?.GrowthRates;
        var rated = GrowthRatesFromITree.FromSaved(plan, saved, DesignYear(), latitude, longitude);
        ShowGrowthRates(rated);
        var loaded = rated.Count(type => type.HasRates);
        if (loaded == 0)
        {
            GrowthRatesStatusText.Text = "No i-Tree rates saved on this project for these types yet: select Get rates to ask i-Tree.";
            SetStep(2, StepState.Attention);
            return;
        }

        var toAsk = rated.Count - loaded;
        GrowthRatesStatusText.Text =
            $"Loaded the i-Tree rates saved on the project for {loaded:N0} type(s)" +
            (toAsk > 0 ? $"; {toAsk:N0} need Get rates (see Note)" : string.Empty) + ". " +
            await CheckSavedRatesAsync();
        if (toAsk > 0)
        {
            SetStep(2, StepState.Attention);
        }
    }

    /// <summary>
    /// Asks i-Tree for every type's rates, to the design year, and saves them on the project so the
    /// step opens on them next time without asking again.
    /// </summary>
    private async Task GetGrowthRatesAsync()
    {
        var apiKey = _iTreeCredentialStore.Load().Trim();
        if (apiKey.Length == 0)
        {
            GrowthRatesStatusText.Text = "No i-Tree API key saved: add one in Settings first.";
            return;
        }

        if (await PlanGrowthRatesAsync() is not var (plan, latitude, longitude))
        {
            return;
        }

        var designYear = DesignYear();
        // The last report reaches the UI after the result does, so it mustn't replace the summary.
        var progress = new Progress<(int Done, int Total)>(step =>
        {
            if (step.Done < step.Total)
            {
                GrowthRatesStatusText.Text = $"Asking i-Tree… {step.Done:N0} of {step.Total:N0}";
            }
        });
        var rated = await GrowthRatesFromITree.RunAsync(
            plan,
            latitude,
            longitude,
            designYear,
            (input, years, token) => _iTreeApiClient.ForecastAsync(input, years, apiKey, token),
            progress);
        var ratedCount = rated.Count(type => type.HasRates);
        var failed = rated.Count - ratedCount;
        var saveNote = string.Empty;
        if (ratedCount > 0)
        {
            try
            {
                var saved = (await ProjectSettingsSync.PullAsync(GetClient()))?.GrowthRates;
                await ProjectSettingsSync.PushAsync(GetClient(),
                    growthRates: GrowthRatesFromITree.Save(saved, rated, designYear, latitude, longitude, DateTimeOffset.Now));
            }
            catch (Exception exception)
            {
                saveNote = $" (Not saved on the project: {exception.Message})";
            }
        }

        ShowGrowthRates(rated);
        GrowthRatesStatusText.Text =
            $"{ratedCount:N0} of {rated.Count:N0} type(s) rated by i-Tree (height and width at year {designYear}) and saved on the project{saveNote}." +
            (failed > 0 ? $" {failed:N0} failed (see Note)." : string.Empty) +
            (ratedCount > 0 ? " " + await CheckSavedRatesAsync() : string.Empty);
    }

    private void ShowGrowthRates(IReadOnlyList<TypeGrowthRates> rated)
    {
        _growthRates = rated;
        _typeRateStatus = new Dictionary<long, RateStatus>();
        _sheetRateStatus = new Dictionary<long, RateStatus>();
        RenderGrowthRates();
    }

    private void ShowGrowthRates(IReadOnlyList<TypeGrowthRates> rated, string status)
    {
        ShowGrowthRates(rated);
        GrowthRatesStatusText.Text = status;
    }

    /// <summary>The rows with where each type's rates already are; Write and Push only when something would change.</summary>
    private void RenderGrowthRates()
    {
        GrowthRateRows.Clear();
        foreach (var type in _growthRates)
        {
            GrowthRateRows.Add(new GrowthRateRow(type, _typeRateStatus.GetValueOrDefault(type.TypeId), _sheetRateStatus.GetValueOrDefault(type.TypeId)));
        }

        var rated = _growthRates.Where(type => type.HasRates).ToList();
        WriteGrowthRatesButton.IsEnabled = rated.Any(type => _typeRateStatus.GetValueOrDefault(type.TypeId) is not { IsUpToDate: true });
        PushGrowthRatesButton.IsEnabled = rated.Any(type => _sheetRateStatus.GetValueOrDefault(type.TypeId) is null or { NeedsWriting: true });
    }

    /// <summary>
    /// Reads where each type's i-Tree rates already are, on its Revit type and on its row of the
    /// Airtable sheet, and says what Write and Push would do. Changes nothing.
    /// </summary>
    private async Task<string> CheckSavedRatesAsync()
    {
        var rated = _growthRates.Where(type => type.HasRates).ToList();
        var writes = GrowthRatesFromITree.BuildTypeWrites(rated);
        var preview = await GetClient().SendAsync<ParameterWriteResult>(
            PipeCommands.PreviewParameterWrites, new ParameterWriteBatch(new ModelScanOptions(PrimaryDesignOptionsOnly: false), writes));
        _typeRateStatus = GrowthRatesFromITree.TypeStatuses(writes, preview.Rows);

        var sheet = await LoadSheetMatchesAsync();
        _sheetRateStatus = sheet is null
            ? new Dictionary<long, RateStatus>()
            : rated.ToDictionary(
                type => type.TypeId,
                type => GrowthRatesFromITree.SheetStatus(type, sheet.Value.Matches.FirstOrDefault(match => match.RevitType.TypeId == type.TypeId)));
        RenderGrowthRates();

        static string Count(IEnumerable<RateStatus> statuses, string status, string label) =>
            statuses.Count(item => item.Status == status) is var count and > 0 ? $"{count:N0} {label}" : string.Empty;
        string Summary(IReadOnlyCollection<RateStatus> statuses) => string.Join(", ", new[]
        {
            Count(statuses, RateStatus.UpToDate, "up to date"),
            Count(statuses, RateStatus.Stale, "stale"),
            Count(statuses, RateStatus.Missing, "missing"),
            Count(statuses, RateStatus.NoRow, "with no row")
        }.Where(part => part.Length > 0));

        var typesDone = _typeRateStatus.Values.All(status => status.IsUpToDate);
        var sheetDone = sheet is not null && _sheetRateStatus.Values.All(status => !status.NeedsWriting);
        SetStep(2, typesDone && sheetDone ? StepState.Done : StepState.Attention);
        return $"Revit types: {Summary(_typeRateStatus.Values.ToList())}." +
               (sheet is null
                   ? " Airtable sheet not checked: no Airtable token or planting data source in Settings."
                   : $" Airtable sheet: {Summary(_sheetRateStatus.Values.ToList())}.");
    }

    private async void WriteGrowthRates_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var writes = GrowthRatesFromITree.BuildTypeWrites(_growthRates);
        if (writes.Count == 0)
        {
            GrowthRatesStatusText.Text = "Nothing to write: select Get rates first.";
            return;
        }

        var batch = new ParameterWriteBatch(new ModelScanOptions(PrimaryDesignOptionsOnly: false), writes);
        var preview = await GetClient().SendAsync<ParameterWriteResult>(PipeCommands.PreviewParameterWrites, batch);
        var ready = preview.Rows.Where(row => row.Status == "Ready").ToList();
        var problems = preview.Rows.Where(row => row.Status is not ("Ready" or "No change")).ToList();
        var statuses = GrowthRatesFromITree.TypeStatuses(writes, preview.Rows);
        var upToDate = statuses.Values.Count(status => status.IsUpToDate);
        if (ready.Count == 0)
        {
            GrowthRatesStatusText.Text = (problems.Count == 0
                ? $"Every type already has its i-Tree rates: {upToDate:N0} type(s) up to date, nothing written. "
                : $"Nothing written. {string.Join("; ", problems.Take(5).Select(row => $"{row.TypeName}: {row.Message}"))} ") +
                await CheckSavedRatesAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Write growth rates?",
            Content = $"Sets {ready.Count:N0} growth rate(s) on {ready.Select(row => row.TypeName).Distinct().Count():N0} type(s), e.g. " +
                      string.Join(", ", ready.Take(3).Select(row => $"{row.TypeName} {GrowthRatesFromITree.RateName(row.RevitParameter)} {Blank(row.CurrentValue)} → {row.ProposedValue}")) +
                      (upToDate > 0 ? $". {upToDate:N0} type(s) already up to date are skipped" : string.Empty) +
                      ". The family then grows each tree from them, and the height rate sets each type's Mature Age. One Revit undo step." +
                      (problems.Count > 0 ? $" {problems.Count:N0} can't be written: {string.Join("; ", problems.Take(3).Select(row => $"{row.TypeName}: {row.Message}"))}." : string.Empty),
            PrimaryButtonText = "Write",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await GetClient().SendAsync<ParameterWriteResult>(PipeCommands.ApplyParameterWrites, batch);
        GrowthRatesStatusText.Text = $"Wrote {result.ChangedParameterCount:N0} growth rate(s) on {ready.Select(row => row.TypeName).Distinct().Count():N0} type(s)" +
                                     (upToDate > 0 ? $"; {upToDate:N0} type(s) were already up to date and skipped" : string.Empty) +
                                     ". Run Create, refresh & calculate so the growth-year copies get their new size and i-Tree results. " +
                                     await CheckSavedRatesAsync();
    });

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "(empty)" : value;

    private static readonly string[] SheetTypeKeyFields = ["Types", "Planting type", "Species names", "species", "type"];

    private static readonly (string Column, string Description)[] RateColumns =
    [
        (GrowthRatesFromITree.TrunkSheetColumn, "Trunk diameter (DBH) growth per year in cm, from i-Tree's forecast. Pushed by LIM Growth Projections."),
        (GrowthRatesFromITree.HeightSheetColumn, "Tree height growth per year in m: i-Tree's height at the design year divided by the design year. Pushed by LIM Growth Projections."),
        (GrowthRatesFromITree.WidthSheetColumn, "Crown width growth per year in m: i-Tree's crown width at the design year divided by the design year. Pushed by LIM Growth Projections.")
    ];

    /// <summary>The project's Airtable sheet with each rated type matched to its row; null without a token or source.</summary>
    private async Task<(AirtableApiSettings Settings, string Token, IReadOnlyList<TypeMatch> Matches)?> LoadSheetMatchesAsync()
    {
        var token = _airtableCredentialStore.Load().Trim();
        var settings = (await ProjectSettingsSync.PullAsync(GetClient()))?.AirtableApi;
        if (token.Length == 0 || settings is null || string.IsNullOrWhiteSpace(settings.BaseId))
        {
            return null;
        }

        var scan = await GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, new ModelScanOptions(PrimaryDesignOptionsOnly: false));
        var ratedIds = _growthRates.Where(type => type.HasRates).Select(type => type.TypeId).ToHashSet();
        var types = scan.Items.Where(item => ratedIds.Contains(item.TypeId)).DistinctBy(item => item.TypeId).ToList();
        var records = await _airtableApiClient.GetRecordsAsync(settings, token);
        return (settings, token, StableTypeMatcher.Build(types, records, SheetTypeKeyFields, []));
    }

    /// <summary>
    /// Replaces each rated type's rates on its row of the project's Airtable sheet (the planting data
    /// source in Settings), adding a number column first if the sheet doesn't have it. Rows that
    /// already have the rates are skipped.
    /// </summary>
    private async void PushGrowthRates_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        if (await LoadSheetMatchesAsync() is not var (settings, token, matches))
        {
            GrowthRatesStatusText.Text = "No Airtable token or planting data source saved: open Settings first.";
            return;
        }

        var updates = GrowthRatesFromITree.SheetUpdates(_growthRates, matches);
        var matchedRows = matches.Where(match => match.Status == "Matched" && match.Record is not null).Select(match => match.Record!.Id).Distinct().Count();
        var upToDate = matchedRows - updates.Count;
        var unmatched = matches.Where(match => match.Status != "Matched").Select(match => match.RevitType.TypeName).ToList();
        if (updates.Count == 0)
        {
            GrowthRatesStatusText.Text = (matchedRows > 0
                ? $"Nothing pushed: the sheet already has these rates ({upToDate:N0} row(s) up to date). "
                : $"No sheet rows match these types{(unmatched.Count > 0 ? $" ({string.Join(", ", unmatched.Take(5))})" : string.Empty)}. ") +
                await CheckSavedRatesAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Push growth rates to the sheet?",
            Content = $"Writes the i-Tree rates to {updates.Count:N0} row(s) of the Airtable sheet's growth rate columns " +
                      $"({string.Join(", ", RateColumns.Select(column => $"\"{column.Column}\""))}), adding any that are missing. " +
                      "The values already there are replaced." +
                      (upToDate > 0 ? $" {upToDate:N0} row(s) already up to date are skipped." : string.Empty) +
                      (unmatched.Count > 0 ? $" {unmatched.Count:N0} type(s) have no matching row and are skipped." : string.Empty),
            PrimaryButtonText = "Push",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var added = new List<string>();
        foreach (var (column, description) in RateColumns)
        {
            if (await _airtableApiClient.EnsureNumberFieldAsync(settings, token, column, 2, description))
            {
                added.Add(column);
            }
        }

        var pushed = 0;
        var problems = new List<string>();
        foreach (var (recordId, fields, typeName) in updates)
        {
            try
            {
                await _airtableApiClient.UpdateRecordFieldsAsync(settings, token, recordId, fields);
                pushed++;
            }
            catch (Exception exception)
            {
                problems.Add($"{typeName} ({exception.Message})");
            }

            // Airtable allows 5 requests a second per base.
            await Task.Delay(210);
        }

        GrowthRatesStatusText.Text = (added.Count > 0 ? $"Added the {string.Join(", ", added.Select(column => $"\"{column}\""))} column(s). " : string.Empty) +
                                     $"Pushed the rates of {pushed:N0} row(s) to the sheet" +
                                     (upToDate > 0 ? $"; {upToDate:N0} row(s) were already up to date and skipped" : string.Empty) + "." +
                                     (unmatched.Count > 0 ? $" No row for: {string.Join(", ", unmatched.Take(5))}{(unmatched.Count > 5 ? ", …" : string.Empty)}." : string.Empty) +
                                     (problems.Count > 0 ? $" Failed: {string.Join("; ", problems.Take(3))}." : string.Empty) + " " +
                                     await CheckSavedRatesAsync();
    });

    // ---- Steps ----

    private enum StepState { None, Attention, Done }

    private static readonly string[] StepNames = ["Years", "Check trees", "Growth rates", "Create & calculate"];

    private static readonly string[] StepHints =
    [
        "Set the year new trees are drawn at, the growth years to model and the trees in scope. Next saves them on the project, so the next run starts from them.",
        "Puts every tree on its year's workset and finds trees that overlap (shown red in the active view). Fix what it finds, then go on.",
        "Gives each tree type its trunk, height and crown growth rates from i-Tree, so trees grow between the years. Do it before creating the copies.",
        "Makes or refreshes the growth-year copies and calculates i-Tree for them, in one confirmation. Revit adds copies only to the active design option: with trees in several options, make each one active in turn (status bar > Design Options) and run again. Done when every option has its copies and every tree is calculated."
    ];

    private readonly StepState[] _stepStates = new StepState[4];
    private int _step;

    private SelectorBarItem[] StepItems => [Step1Item, Step2Item, Step3Item, Step4Item];

    private StackPanel[] StepPanels => [Step1Panel, Step2Panel, Step3Panel, Step4Panel];

    /// <summary>Shows one step and runs its read-only check on arrival, so the step opens on what's left to do.</summary>
    private async void StepBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = Array.IndexOf(StepItems, sender.SelectedItem);
        if (index < 0 || Step4Panel is null || NextButton is null)
        {
            return;
        }

        _step = index;
        for (var i = 0; i < StepPanels.Length; i++)
        {
            StepPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        BackButton.IsEnabled = index > 0;
        NextButton.Visibility = index < StepNames.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = index < StepNames.Length - 1 ? $"Next: {StepNames[index + 1]}" : string.Empty;
        StepHintText.Text = StepHints[index];
        if (_revitClient is null)
        {
            return;
        }

        switch (index)
        {
            case 1:
                await RunBusyAsync(CheckTreesAsync);
                break;
            case 2:
                await RunBusyAsync(LoadSavedGrowthRatesAsync);
                break;
            case 3:
                await RunBusyAsync(PreviewRunAsync);
                break;
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0 && !await SaveYearsAsync())
        {
            return;
        }

        if (_step < StepNames.Length - 1)
        {
            StepBar.SelectedItem = StepItems[_step + 1];
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
        {
            StepBar.SelectedItem = StepItems[_step - 1];
        }
    }

    /// <summary>Saves the growth years and design year on the project. False (and stays on the step) when that fails.</summary>
    private async Task<bool> SaveYearsAsync()
    {
        var years = YearRows.Select(row => row.Year).Where(year => year > 0).Distinct().Order().ToList();
        if (years.Count == 0)
        {
            SetStep(0, StepState.Attention);
            ErrorBar.Title = "Add at least one growth year";
            ErrorBar.Message = "The years below the design year (and any above it) get copies; you need at least one.";
            ErrorBar.IsOpen = true;
            return false;
        }

        var saved = false;
        await RunBusyAsync(async () =>
        {
            await ProjectSettingsSync.PushAsync(GetClient(), growthProjectionYears: years, growthDesignYear: DesignYear());
            saved = true;
        });
        SetStep(0, saved ? StepState.Done : StepState.Attention);
        return saved;
    }

    private void SetStep(int index, StepState state)
    {
        _stepStates[index] = state;
        StepItems[index].Text = $"{index + 1}  {StepNames[index]}" + state switch
        {
            StepState.Done => "  ✓",
            StepState.Attention => "  !",
            _ => string.Empty
        };
    }

    /// <summary>The year new-construction trees are drawn at (the design year).</summary>
    private int DesignYear() =>
        double.IsNaN(DesignYearBox.Value) ? GrowthYearNames.DefaultDesignYear : Math.Clamp((int)Math.Round(DesignYearBox.Value), 1, GrowthYearNames.MaximumYear);

    /// <summary>Has every input and just needs (re)calculating.</summary>
    private static bool IsWaiting(string status) => status is "Ready" or "Stale";

    private static string Describe(GrowthProjectionResult result, int designYear)
    {
        var verb = result.PreviewOnly ? "Will" : "Done:";
        var parts = new List<string>
        {
            $"{result.OriginalsInScope:N0} original tree(s) in scope",
            $"move {result.MovedToBaseline:N0} onto their own year's workset (existing trees year 0, new trees year {designYear})",
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
        ErrorBar.IsOpen = false;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed: {exception.Message}";
            ErrorBar.Title = "Failed";
            ErrorBar.Message = exception.Message + LogError(exception);
            ErrorBar.IsOpen = true;
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Appends the full error to %TEMP%\LIM-GrowthProjections-errors.log; returns where, for the message.</summary>
    private static string LogError(Exception exception)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "LIM-GrowthProjections-errors.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return $" (details in {path})";
        }
        catch (IOException)
        {
            return string.Empty;
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
