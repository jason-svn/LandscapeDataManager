using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Connectivity;

public sealed partial class MainPage : Page
{
    private RevitPipeClient? _revitClient;
    private string _pipeName = string.Empty;
    private ConnectivitySettings _lastSettings = ConnectivitySettings.Default;

    // The model as last read: every patch with its design option and phase, the option sets, and
    // which trees are Growth Projections' copies (left out: the trees as drawn are the network).
    private ConnectivityPatchesResult? _scan;
    private IReadOnlyList<DesignOptionSetItem> _sets = [];
    private IReadOnlySet<string> _growthCopies = new HashSet<string>();
    private IReadOnlyList<DesignScenario> _savedScenarios = [];
    private IReadOnlyList<DesignScenario> _offered = [];
    private IReadOnlyList<ConnectivityScenarioResult> _savedResults = [];
    private int _designYear = GrowthYearNames.DefaultDesignYear;
    private DesignScenario? _lastScenario;

    public MainPage()
    {
        InitializeComponent();
        ShowSettings(ConnectivitySettings.Default);
    }

    public ObservableCollection<ConnectivityRow> ResultRows { get; } = [];
    public ObservableCollection<ScenarioResultRow> ScenarioResultRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadSettingsAsync();
        await RunBusyAsync(() => LoadModelAsync(IncludeFloorsCheckBox.IsChecked == true));
        ShowSavedResult();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
            if (snapshot?.Connectivity is { } settings)
            {
                ShowSettings(settings);
            }

            _savedScenarios = snapshot?.DesignScenarios ?? [];
            _savedResults = snapshot?.ConnectivityResults ?? [];
            _designYear = snapshot?.GrowthDesignYear ?? _designYear;
            RenderComparison();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Couldn't read the project's connectivity settings, using defaults: {exception.Message}";
        }
    }

    /// <summary>
    /// Reads every tree and planting area with its design option and phase, and which trees are
    /// Growth Projections' copies, then offers the design scenarios (keeping the one picked).
    /// </summary>
    private async Task LoadModelAsync(bool includeFloors)
    {
        var scan = await GetClient().SendAsync<ConnectivityPatchesResult>(
            PipeCommands.GetConnectivityPatches,
            new ConnectivityPatchesRequest(includeFloors, AllDesignOptions: true));
        var organizer = await GetClient().SendAsync<OrganizerTreesResult>(PipeCommands.GetOrganizerTrees);
        _growthCopies = organizer.Trees
            .Where(tree => tree.LinkedYear is not null)
            .Select(tree => tree.UniqueId)
            .ToHashSet(StringComparer.Ordinal);
        _scan = scan;
        _sets = DesignScenarios.ResolveSets(scan.DesignOptionSets, scan.Items.Select(item => item.DesignOption).OfType<DesignOptionInfo>());

        var picked = SelectedScenario()?.Name;
        _offered = ConnectivityScenarios.Offered(scan.Items, _sets, _savedScenarios);
        ScenarioBox.ItemsSource = _offered.Select(ScenarioLabel).ToList();
        ScenarioBox.SelectedIndex = Math.Max(0, _offered.ToList().FindIndex(scenario => scenario.Name == picked));
        CompareButton.IsEnabled = _offered.Count > 1;
        if (!ConnectivityScenarios.HasOptions(scan.Items))
        {
            StatusText.Text = "This connector reports only the primary design options: install the latest LIM add-in (close Revit) to score other scenarios.";
        }
    }

    private string ScenarioLabel(DesignScenario scenario) => DesignScenarios.Label(scenario, _sets);

    private DesignScenario? SelectedScenario() =>
        ScenarioBox.SelectedIndex is var index and >= 0 && index < _offered.Count ? _offered[index] : null;

    private void ScenarioBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSavedResult();

    /// <summary>The picked scenario's saved connected habitat in the headline card.</summary>
    private void ShowSavedResult()
    {
        if (SelectedScenario() is { } scenario && ConnectivityScenarios.For(_savedResults, scenario) is { } saved)
        {
            ShowConnected(saved, scenario);
        }
        else
        {
            ConnectedHabitatText.Text = "—";
            ConnectedHabitatDetailText.Text = "Not calculated for this scenario yet.";
        }
    }

    private void ShowConnected(ConnectivityScenarioResult result, DesignScenario scenario)
    {
        ConnectedHabitatText.Text = $"{result.ConnectedPercent:N1}%";
        ConnectedHabitatDetailText.Text =
            $"EC {result.EquivalentConnectedArea:N0} of {result.HabitatAreaSquareMeters:N0} m² habitat · {scenario.Name}" +
            (scenario.IsExistingSite ? " (today)" : $", new trees at year {result.DesignYear}");
    }

    /// <summary>The scenarios' saved connected habitat, in the order they're offered, then any others.</summary>
    private void RenderComparison()
    {
        var settings = TryReadSettings();
        ScenarioResultRows.Clear();
        foreach (var result in _savedResults
                     .OrderBy(result => _offered.ToList().FindIndex(scenario => scenario.Name == result.Scenario) is var index and >= 0 ? index : int.MaxValue))
        {
            var scenario = _offered.FirstOrDefault(offered => offered.Name == result.Scenario);
            string? note = scenario is null
                ? "Scenario no longer saved"
                : settings is not null && (!settings.DispersalDistancesMeters.SequenceEqual(result.DispersalDistancesMeters) ||
                                           Math.Abs(settings.ProbabilityAtDistance - result.ProbabilityAtDistance) > 1e-9)
                    ? "Other distances or probability than now: compare again"
                    : null;
            ScenarioResultRows.Add(new ScenarioResultRow(result, scenario is null ? result.Scenario : ScenarioLabel(scenario), note));
        }

        ComparisonCard.Visibility = ScenarioResultRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ConnectivitySettings? TryReadSettings()
    {
        try
        {
            return ReadSettings();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>One scenario scored: the patches it counts, the floors left out and why, and the growth-year copies skipped.</summary>
    private sealed record ScenarioScore(
        DesignScenario Scenario,
        List<ConnectivityPatchItem> Included,
        List<(ConnectivityPatchItem Item, string Reason)> Excluded,
        int CopiesLeftOut,
        IReadOnlyList<ConnectivityPatch> Patches,
        ConnectivityRunResult Run);

    private async Task<ScenarioScore> ScoreAsync(DesignScenario scenario, ConnectivitySettings settings, IProgress<double> progress)
    {
        var included = new List<ConnectivityPatchItem>();
        var excluded = new List<(ConnectivityPatchItem Item, string Reason)>();
        var copies = 0;
        foreach (var item in ConnectivityScenarios.InScenario(_scan!.Items, scenario, _sets))
        {
            if (_growthCopies.Contains(item.UniqueId))
            {
                copies++;
            }
            else if (ConnectivityPatchSelector.ExclusionReason(item, settings, _growthCopies) is { } reason)
            {
                excluded.Add((item, reason));
            }
            else
            {
                included.Add(item);
            }
        }

        if (included.Count == 0)
        {
            throw new InvalidOperationException($"{scenario.Name} has no trees or planting areas to score — place Planting families, or tag floor types with a pervious LDS Type.");
        }

        var patches = included.Select(ConnectivityPatchSelector.ToPatch).ToList();
        var options = new ConnectivityOptions(settings.DispersalDistancesMeters, settings.ProbabilityAtDistance);
        var run = await Task.Run(() => ConnectivityCalculator.Calculate(patches, options, progress));
        return new ScenarioScore(scenario, included, excluded, copies, patches, run);
    }

    /// <summary>Saves these scenarios' results (and the settings) on the project, over any saved for the same scenarios.</summary>
    private async Task<string> SaveResultsAsync(ConnectivitySettings settings, IReadOnlyList<ConnectivityScenarioResult> fresh)
    {
        try
        {
            var latest = (await ProjectSettingsSync.PullAsync(GetClient()))?.ConnectivityResults ?? _savedResults;
            _savedResults = ConnectivityScenarios.Save(latest, fresh);
            await ProjectSettingsSync.PushAsync(GetClient(), connectivity: settings, connectivityResults: _savedResults);
            return string.Empty;
        }
        catch (Exception exception)
        {
            _savedResults = ConnectivityScenarios.Save(_savedResults, fresh);
            return $" (Not saved to the project: {exception.Message})";
        }
        finally
        {
            RenderComparison();
        }
    }

    private async void CompareScenarios_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var settings = ReadSettings();
        await LoadModelAsync(settings.IncludeFloors);
        var fresh = new List<ConnectivityScenarioResult>();
        var skipped = new List<string>();
        CalculationProgress.Visibility = Visibility.Visible;
        try
        {
            for (var index = 0; index < _offered.Count; index++)
            {
                var scenario = _offered[index];
                StatusText.Text = $"Scenario {index + 1:N0} of {_offered.Count:N0}: {scenario.Name}…";
                CalculationProgress.Value = 0;
                try
                {
                    var scored = await ScoreAsync(scenario, settings, new Progress<double>(value => CalculationProgress.Value = value));
                    fresh.Add(ConnectivityScenarios.Summarize(scenario, settings, scored.Patches, scored.Run, _designYear, DateTimeOffset.Now));
                }
                catch (InvalidOperationException exception)
                {
                    skipped.Add(exception.Message);
                }
            }
        }
        finally
        {
            CalculationProgress.Visibility = Visibility.Collapsed;
        }

        var saveNote = await SaveResultsAsync(settings, fresh);
        ShowSavedResult();
        StatusText.Text = $"Compared {fresh.Count:N0} scenario(s); each one's connected habitat is saved on the project for the Dashboard{saveNote}." +
                          (skipped.Count > 0 ? " " + string.Join(" ", skipped) : string.Empty);
    });

    private void ShowSettings(ConnectivitySettings settings)
    {
        DistancesBox.Text = string.Join(", ", settings.DispersalDistancesMeters.Select(distance => distance.ToString("0.##", CultureInfo.InvariantCulture)));
        ProbabilityBox.Value = settings.ProbabilityAtDistance;
        IncludeFloorsCheckBox.IsChecked = settings.IncludeFloors;
        IncludeUnclassifiedCheckBox.IsChecked = settings.IncludeUnclassifiedFloors;
    }

    private ConnectivitySettings ReadSettings()
    {
        var distances = ConnectivityCalculator.ParseDistances(DistancesBox.Text)
                        ?? throw new InvalidOperationException("Enter one or more dispersal distances in metres, e.g. 50, 250, 1000.");
        var probability = ProbabilityBox.Value;
        if (double.IsNaN(probability) || probability is <= 0 or >= 1)
        {
            throw new InvalidOperationException("The dispersal probability must be between 0 and 1 (0.5 = median dispersal distance).");
        }

        return new ConnectivitySettings(
            distances,
            probability,
            IncludeFloorsCheckBox.IsChecked == true,
            IncludeUnclassifiedCheckBox.IsChecked == true);
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

    private async void Calculate_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var settings = ReadSettings();
        await LoadModelAsync(settings.IncludeFloors);
        var scenario = SelectedScenario() ?? _offered[0];

        StatusText.Text = $"Calculating connectivity for {scenario.Name}…";
        CalculationProgress.Value = 0;
        CalculationProgress.Visibility = Visibility.Visible;
        ScenarioScore scored;
        try
        {
            scored = await ScoreAsync(scenario, settings, new Progress<double>(value => CalculationProgress.Value = value));
        }
        finally
        {
            CalculationProgress.Visibility = Visibility.Collapsed;
        }

        var (_, included, excluded, copies, patches, run) = scored;
        _lastSettings = settings;
        _lastScenario = scenario;
        var resultsById = run.Patches.ToDictionary(result => result.Id);
        ResultRows.Clear();
        foreach (var row in included
                     .Select(item => new ConnectivityRow(item, resultsById[item.UniqueId], null))
                     .OrderByDescending(row => row.SortKey)
                     .Concat(excluded.Select(entry => new ConnectivityRow(entry.Item, null, entry.Reason))))
        {
            ResultRows.Add(row);
        }

        ElementsStepItem.Text = $"2  Elements · {ResultRows.Count:N0}";
        UpdateCounts();
        EquivalentAreaText.Text = "Equivalent connected area: " + string.Join("  ·  ", settings.DispersalDistancesMeters
            .Select((distance, index) => $"{distance:0.##} m → {run.EquivalentConnectedAreaByDistance[index]:N0} m²"));
        WriteButton.IsEnabled = true;
        var summary = ConnectivityScenarios.Summarize(scenario, settings, patches, run, _designYear, DateTimeOffset.Now);
        ShowConnected(summary, scenario);
        var saveNote = await SaveResultsAsync(settings, [summary]);
        StatusText.Text = $"{_scan!.DocumentTitle}, {scenario.Name}: scored {included.Count:N0} patch(es)" +
                          (excluded.Count > 0 ? $", {excluded.Count:N0} floor(s) not counted as planting areas" : string.Empty) +
                          (copies > 0 ? $", {copies:N0} growth-year cop(ies) left out (the trees as drawn count)" : string.Empty) +
                          $". Saved on the project for the Dashboard{saveNote}.";
    });

    private async void WriteResults_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var values = ResultRows
            .Where(row => row.Result is not null)
            .Select(row => new ConnectivityResultValues(
                row.UniqueId,
                row.Result!.Band,
                ConnectivityCalculator.BandName(row.Result.Band),
                Math.Round(row.Result.Score, 4),
                Math.Round(row.Result.MeanDpcPercent, 4),
                FormatDetails(row.Result, _lastSettings)))
            .ToList();
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Calculate first — there are no results to write.");
        }

        var result = await GetClient().SendAsync<WriteConnectivityResultsResult>(
            PipeCommands.WriteConnectivityResults,
            new WriteConnectivityResultsRequest(values));

        // Main-model elements are in every scenario: they keep the bands of the scenario written last.
        var message = $"Wrote the bands of {_lastScenario?.Name ?? "the calculated scenario"} to {result.Written.Count:N0} element(s)";
        if (result.SkippedLocked.Count > 0)
        {
            message += $", left {result.SkippedLocked.Count:N0} locked element(s) alone";
        }

        if (result.Failures.Count > 0)
        {
            message += $"; {result.Failures.Count:N0} failed — {result.Failures[0].Reason}";
        }

        StatusText.Text = message + ".";
    });

    private static string FormatDetails(ConnectivityPatchResult result, ConnectivitySettings settings)
    {
        var perDistance = settings.DispersalDistancesMeters.Select((distance, index) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{distance:0.##} m: dPC {result.DpcByDistance[index] * 100:0.###}%, score {result.ScoreByDistance[index]:0.00} ({ConnectivityCalculator.BandName(result.BandByDistance[index])})"));
        var details = string.Create(CultureInfo.InvariantCulture, $"p = {settings.ProbabilityAtDistance:0.##} at ") + string.Join("; ", perDistance) + ".";
        return result.Uncertain ? details + " Uncertain: band changes by two or more between distances." : details;
    }

    private void UpdateCounts()
    {
        int Count(int band) => ResultRows.Count(row => row.Result?.Band == band);
        PatchCountText.Text = ResultRows.Count(row => row.Result is not null).ToString("N0");
        CriticalCountText.Text = Count(ConnectivityCalculator.CriticalBand).ToString("N0");
        StrongCountText.Text = Count(ConnectivityCalculator.StrongBand).ToString("N0");
        ModerateCountText.Text = Count(ConnectivityCalculator.ModerateBand).ToString("N0");
        LowCountText.Text = Count(ConnectivityCalculator.LowBand).ToString("N0");
        UncertainCountText.Text = ResultRows.Count(row => row.Result?.Uncertain == true).ToString("N0");
    }

    private IReadOnlyList<string> GetSelectedUniqueIds() =>
        ResultList.SelectedItems.Cast<ConnectivityRow>().Select(row => row.UniqueId).ToList();

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
        var statusByUniqueId = ResultRows
            .Where(row => row.ColourStatus is not null)
            .ToDictionary(row => row.UniqueId, row => row.ColourStatus!);
        var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ApplyStatusColourOverrides, new StatusColourOverrideRequest(statusByUniqueId));
        StatusText.Text = result.Message ?? "Band colours applied.";
    });

    /// <summary>Step 1 shows the settings and every scenario's connected habitat; step 2 the scored elements, the window's full height.</summary>
    private void ConnectivitySteps_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (SetupPanel is null || ElementsPanel is null)
        {
            return;
        }

        var elements = sender.SelectedItem == ElementsStepItem;
        SetupPanel.Visibility = elements ? Visibility.Collapsed : Visibility.Visible;
        ElementsPanel.Visibility = elements ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void HeatmapView_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var result = await GetClient().SendAsync<ConnectivityHeatmapViewResult>(PipeCommands.CreateConnectivityHeatmapView, null);
        StatusText.Text = $"{(result.ViewCreated ? "Made" : "Updated")} the {result.ViewName} view and opened it: {result.Scored:N0} trees and planting areas coloured by score" +
                          (result.NotAssessed > 0 ? $", {result.NotAssessed:N0} without a score in grey." : ".") +
                          (result.FiltersCreated > 0 ? $" Added {result.FiltersCreated:N0} view filters (WWP_Connectivity …)." : string.Empty);
    });

    private async void ResetColours_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var allIds = ResultRows.Select(row => row.UniqueId).ToList();
        var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ResetColourOverrides, new ElementSelectionRequest(allIds));
        StatusText.Text = result.Message ?? "Colour overrides reset.";
    });

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        CalculateButton.IsEnabled = false;
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
            CalculateButton.IsEnabled = true;
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
