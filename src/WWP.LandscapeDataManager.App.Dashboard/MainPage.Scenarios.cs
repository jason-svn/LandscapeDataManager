using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>An entry in the scenario pickers: a saved scenario, the primary one, or one shown but not saved.</summary>
public sealed class ScenarioChoice
{
    public ScenarioChoice(DesignScenario scenario, string label, bool isSaved)
    {
        Scenario = scenario;
        Label = label;
        IsSaved = isSaved;
    }

    public DesignScenario Scenario { get; }
    public string Label { get; }
    public bool IsSaved { get; }
}

/// <summary>Design scenarios: the picker, the Edit… dialog, "Use current view", and the scenario overview page.</summary>
public sealed partial class MainPage
{
    private bool _scenarioExplicitlyChosen;

    /// <summary>
    /// The primary scenario (unless a saved one already picks every primary), the saved ones, any
    /// shown but not saved, then the existing site when the model has a first phase to show.
    /// </summary>
    private IReadOnlyList<DesignScenario> OfferedScenarios()
    {
        var offered = DesignScenarios.WithPrimary(_savedScenarios, _sets).ToList();
        if (_unsavedScenario is { } unsaved && offered.All(scenario => !DesignScenarios.SameChoices(scenario, unsaved, _sets)))
        {
            offered.Add(unsaved);
        }

        if (_lastReport is not null && SitePhases.HasExistingSite(_lastReport))
        {
            offered.Add(DesignScenarios.ExistingSite(_sets));
        }

        return offered;
    }

    /// <summary>The scenario differences are measured against: the one chosen, else the existing site, else the default.</summary>
    private static DesignScenario? ReferenceScenario(IReadOnlyList<DesignScenario> offered, string? name) =>
        (name is null ? offered.FirstOrDefault(scenario => scenario.IsExistingSite) : null) ?? DesignScenarios.Find(offered, name);

    private bool IsSaved(DesignScenario scenario) =>
        _savedScenarios.Any(saved => string.Equals(saved.Name, scenario.Name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Fills both scenario pickers and keeps the current pick, else opens on the "Baseline" scenario or the primary one.</summary>
    private void RebuildScenarioOptions()
    {
        var offered = OfferedScenarios();
        var choices = offered.Select(scenario => new ScenarioChoice(scenario, ChoiceLabel(scenario), IsSaved(scenario))).ToList();
        var current = (_scenarioExplicitlyChosen ? choices.FirstOrDefault(choice => choice.Scenario.Name == _scenario.Name) : null)
                      ?? choices.FirstOrDefault(choice => choice.Scenario == DesignScenarios.Find(offered, null))
                      ?? choices[0];
        var reference = choices.FirstOrDefault(choice => choice.Scenario == ReferenceScenario(offered, _referenceScenarioName)) ?? choices[0];

        var wasSuppressed = _suppressFilterEvents;
        _suppressFilterEvents = true;
        try
        {
            ScenarioBox.ItemsSource = choices;
            ScenarioBox.SelectedItem = current;
            ToolTipService.SetToolTip(ScenarioBox, DesignScenarios.Label(current.Scenario, _sets));
            ReferenceScenarioBox.ItemsSource = choices;
            ReferenceScenarioBox.SelectedItem = reference;
        }
        finally
        {
            _suppressFilterEvents = wasSuppressed;
        }

        _scenario = current.Scenario;
        SyncProjectionYearsFromScenario();
    }

    private string ChoiceLabel(DesignScenario scenario)
    {
        var label = DesignScenarios.Label(scenario, _sets);
        return _unsavedScenario == scenario && !IsSaved(scenario) ? $"{label} (not saved)" : label;
    }

    private void ScenarioBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || ScenarioBox.SelectedItem is not ScenarioChoice choice || ReferenceEquals(choice.Scenario, _scenario))
        {
            return;
        }

        _scenario = choice.Scenario;
        _scenarioExplicitlyChosen = true;
        ToolTipService.SetToolTip(ScenarioBox, choice.Label);
        var wasSuppressed = _suppressFilterEvents;
        _suppressFilterEvents = true;
        try
        {
            SyncProjectionYearsFromScenario();
        }
        finally
        {
            _suppressFilterEvents = wasSuppressed;
        }

        if (DesignScenarios.MissingChoices(_scenario, _sets) is { Count: > 0 } missing)
        {
            StatusText.Text = $"\"{_scenario.Name}\" names options that no longer exist ({string.Join(", ", missing)}); their sets show the primary option. Edit and save it to update.";
        }

        RebuildRows();
    }

    /// <summary>
    /// Older models carried the growth year in a design option's name ("Growth Timeline : 15 Years");
    /// when no growth-year worksets exist, a chosen option with a year sets the outlook.
    /// </summary>
    private void SyncProjectionYearsFromScenario()
    {
        if (_growthYearsFromWorksets)
        {
            return;
        }

        var optionYears = _sets
            .Select(set => DashboardUnitLabels.ExtractProjectionYears(DesignScenarios.ChosenOption(_scenario, set)))
            .FirstOrDefault(years => years is not null);
        if (optionYears is { } years)
        {
            _projectionYears = years;
            SetGrowthYearSliderValue();
        }
    }

    private async void EditScenario_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard first.";
            return;
        }

        var editingSaved = IsSaved(_scenario);
        var nameBox = new TextBox
        {
            Header = "Scenario name",
            Text = editingSaved || _unsavedScenario == _scenario ? _scenario.Name : NextScenarioName(),
            PlaceholderText = "e.g. Scheme A"
        };
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
        var optionBoxes = new List<(DesignOptionSetItem Set, ComboBox Box)>();
        var content = new StackPanel { Spacing = 12, MinWidth = 420 };
        content.Children.Add(nameBox);

        void UpdatePreview() => preview.Text = DesignScenarios.Describe(Picked(), _sets);
        DesignScenario Picked() => new(
            string.IsNullOrWhiteSpace(nameBox.Text) ? "Unnamed scenario" : nameBox.Text.Trim(),
            optionBoxes.ToDictionary(pair => pair.Set.SetName, pair => pair.Box.SelectedItem as string ?? DesignScenarios.PrimaryOptionOf(pair.Set), StringComparer.Ordinal));

        if (_sets.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "This model has no design options, so there is one scenario: the main model.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var set in _sets)
        {
            var box = new ComboBox { Header = set.SetName, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in set.Options)
            {
                box.Items.Add(option.Name);
            }

            box.SelectedItem = DesignScenarios.ChosenOption(_scenario, set);
            box.SelectionChanged += (_, _) => UpdatePreview();
            optionBoxes.Add((set, box));
            content.Children.Add(box);
        }

        var viewButton = new Button { Content = "Fill from Revit's active view", IsEnabled = _sets.Count > 0 };
        ToolTipService.SetToolTip(viewButton, "Choose, for every set, the option the active view shows.");
        viewButton.Click += async (_, _) =>
        {
            if (await TryGetViewScenarioAsync() is not { } view)
            {
                preview.Text = "Couldn't read the active view from Revit.";
                return;
            }

            foreach (var (set, box) in optionBoxes)
            {
                if (view.Choices.TryGetValue(set.SetName, out var option) && box.Items.Contains(option))
                {
                    box.SelectedItem = option;
                }
            }

            UpdatePreview();
            if (view.Notes.Count > 0)
            {
                preview.Text += Environment.NewLine + string.Join(Environment.NewLine, view.Notes);
            }
        };
        content.Children.Add(viewButton);
        content.Children.Add(preview);

        var deleteRequested = false;
        ContentDialog? dialog = null;
        if (editingSaved)
        {
            var deleteButton = new HyperlinkButton { Content = $"Delete saved scenario \"{_scenario.Name}\"" };
            deleteButton.Click += (_, _) =>
            {
                deleteRequested = true;
                dialog?.Hide();
            };
            content.Children.Add(deleteButton);
        }

        UpdatePreview();
        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Design scenario",
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
            PrimaryButtonText = "Save and show",
            SecondaryButtonText = "Show without saving",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (deleteRequested)
        {
            await SaveScenariosAsync(DesignScenarios.Remove(_savedScenarios, _scenario.Name), $"Deleted scenario \"{_scenario.Name}\".");
            _scenarioExplicitlyChosen = false;
            RebuildScenarioOptions();
            RebuildRows();
            return;
        }

        if (result == ContentDialogResult.None)
        {
            return;
        }

        var picked = Picked();
        if (result == ContentDialogResult.Primary)
        {
            if (string.Equals(picked.Name, DesignScenarios.PrimaryScenarioName, StringComparison.CurrentCultureIgnoreCase))
            {
                picked = picked with { Name = NextScenarioName() };
            }

            await SaveScenariosAsync(DesignScenarios.Upsert(_savedScenarios, picked), $"Saved scenario \"{picked.Name}\" on the project.");
        }
        else
        {
            _unsavedScenario = picked;
        }

        Show(picked);
    }

    private async void UseCurrentView_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard first.";
            return;
        }

        if (await TryGetViewScenarioAsync() is not { } view)
        {
            StatusText.Text = "Couldn't read Revit's active view. Open the model in Revit and try again.";
            return;
        }

        var fromView = new DesignScenario($"View: {view.ViewName}", view.Choices);
        if (OfferedScenarios().FirstOrDefault(scenario => DesignScenarios.SameChoices(scenario, fromView, _sets)) is { } existing)
        {
            Show(existing);
            StatusText.Text = $"The active view \"{view.ViewName}\" shows \"{existing.Name}\".";
        }
        else
        {
            _unsavedScenario = fromView;
            Show(fromView);
            StatusText.Text = $"Showing what \"{view.ViewName}\" shows: {DesignScenarios.Describe(fromView, _sets)}. Edit… to save it.";
        }

        if (view.Notes.Count > 0)
        {
            StatusText.Text += " " + string.Join(" ", view.Notes);
        }
    }

    /// <summary>Asks Revit what the active view shows now; falls back to what it showed at the last refresh.</summary>
    private async Task<ViewDesignScenarioResult?> TryGetViewScenarioAsync()
    {
        try
        {
            return await GetClient().SendAsync<ViewDesignScenarioResult>(PipeCommands.GetViewDesignScenario);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            return _lastReport?.ViewScenario;
        }
    }

    private void Show(DesignScenario scenario)
    {
        _scenario = scenario;
        _scenarioExplicitlyChosen = true;
        RebuildScenarioOptions();
        RebuildRows();
    }

    private string NextScenarioName()
    {
        for (var index = 1; ; index++)
        {
            var name = $"Scheme {(char)('A' + (index - 1) % 26)}{(index > 26 ? (index / 26).ToString() : string.Empty)}";
            if (!IsSaved(new DesignScenario(name, new Dictionary<string, string>())))
            {
                return name;
            }
        }
    }

    private async Task SaveScenariosAsync(IReadOnlyList<DesignScenario> scenarios, string message)
    {
        try
        {
            await ProjectSettingsSync.PushAsync(GetClient(), designScenarios: scenarios);
            _savedScenarios = scenarios;
            StatusText.Text = message;
            await DashboardSnapshotCache.SaveSettingsAsync(CurrentSettings());
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            StatusText.Text = $"Couldn't save to the project ({exception.Message}). The scenario is shown but not saved.";
        }
    }

    private async void ReferenceScenarioBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A re-raised event for the reference already shown isn't a choice: don't save it.
        if (_suppressFilterEvents || ReferenceScenarioBox.SelectedItem is not ScenarioChoice choice ||
            string.Equals(choice.Scenario.Name, ReferenceScenario(OfferedScenarios(), _referenceScenarioName)?.Name, StringComparison.Ordinal))
        {
            return;
        }

        _referenceScenarioName = choice.Scenario.Name;
        RebuildRows();
        if (!choice.IsSaved && choice.Scenario.Name != DesignScenarios.PrimaryScenarioName)
        {
            return; // an unsaved scenario can't be the saved reference
        }

        try
        {
            await ProjectSettingsSync.PushAsync(GetClient(), referenceScenario: choice.Scenario.Name);
            await DashboardSnapshotCache.SaveSettingsAsync(CurrentSettings());
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private string OutlookMethodText(TreeOutlook outlook, int years) => outlook.Method switch
    {
        TreeOutlookMethod.Timeline =>
            $"{years}-year outlook · trees: i-Tree's year-by-year forecast ({outlook.TreesWithTimeline:N0} trees), held at maturity · planting areas: annual × {years}",
        TreeOutlookMethod.GrowthWorksets =>
            $"{years}-year outlook · trees: each growth-year workset as calculated, years in between interpolated · planting areas: annual × {years}",
        _ => $"{years}-year outlook · annual results × {years} (no i-Tree timelines or growth-year worksets, so trees don't grow)"
    };

    // ---- Scenario overview ----

    /// <summary>One row of the scenario overview. Values are plain numbers; the unit is under the label.</summary>
    private sealed record ScenarioMetric(string Group, string Label, string Unit, Func<ScenarioPerformance, double?> Value, Func<double, string> Format, bool HigherIsBetter, bool IsPercent = false, bool Chart = true);

    /// <summary>A metric group's swim lane: its wash and label colours, or none for the inventory counts.</summary>
    private static (string? Lane, string Text) ScenarioLane(string group) => group switch
    {
        "Canopy & greenspace" => ("LimLaneTealBrush", "LimLaneTealTextBrush"),
        "Carbon" => ("LimLaneGreenBrush", "KpiAccentTextBrush"),
        "Water" => ("LimLaneBlueBrush", "KpiWaterTextBrush"),
        "Air quality" => ("LimLaneAmberBrush", "KpiAmberTextBrush"),
        "Biodiversity" => ("LimLaneVioletBrush", "LimLaneVioletTextBrush"),
        "Value" => ("LimLaneGreyBrush", "KpiMutedBrush"),
        _ => (null, "KpiMutedBrush")
    };

    private IReadOnlyList<ScenarioMetric> ScenarioMetrics(string unitSystem, int years)
    {
        var metric = string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        var carbonUnit = DashboardUnitLabels.CarbonUnit(unitSystem);
        var pollutantUnit = DashboardUnitLabels.PollutantUnit(unitSystem);
        var waterUnit = metric ? "m³" : "gal";
        var currency = _preferredCurrency;
        var over = $"over {years} years";
        string Number(double value) => Compact(value);
        return
        [
            new("Inventory", "Trees", "count", p => p.TreeCount, value => value.ToString("N0"), true, Chart: false),
            new("Inventory", "Species", "count", p => p.SpeciesCount, value => value.ToString("N0"), true, Chart: false),
            new("Canopy & greenspace", "Canopy cover", "% of site", p => p.CanopyCoverPercent, value => $"{value:N1}", true, IsPercent: true),
            new("Canopy & greenspace", "Canopy area", metric ? "m²" : "ft²", p => metric ? p.CanopyAreaSquareMeters : p.CanopyAreaSquareMeters * 10.7639104167d, Number, true, Chart: false),
            new("Canopy & greenspace", "Greenspace ratio", "% of surfaces", p => p.GreenspaceRatioPercent, value => $"{value:N1}", true, IsPercent: true),
            new("Carbon", "CO₂e uptake", $"{carbonUnit} {over}", p => p.Co2EquivalentOutlook, Number, true),
            new("Carbon", "Oxygen produced", $"{carbonUnit} {over}", p => p.OxygenProducedOutlook, Number, true, Chart: false),
            new("Carbon", "Embodied carbon", $"{carbonUnit} CO₂e", p => p.EmbodiedCarbon, Number, false),
            new("Water", "Runoff avoided", $"{waterUnit} {over}", p => p.RunoffAvoidedOutlook, Number, true),
            new("Air quality", "Pollution removed", $"{pollutantUnit} {over}", p => p.PollutionRemovedOutlook, Number, true),
            new("Biodiversity", "BNG post-intervention", "habitat units", p => p.BngPostInterventionUnits, value => $"{value:N2}", true, Chart: false),
            new("Biodiversity", "BNG net change", "% of baseline", p => p.BngNetChangePercent, value => $"{value:+0.0;-0.0;0.0}", true, IsPercent: true),
            new("Biodiversity", "Connected habitat", "% of habitat area (EC)", p => p.ConnectedHabitatPercent, value => $"{value:N1}", true, IsPercent: true),
            new("Biodiversity", "Equivalent connected area", "m² (EC)", p => p.EquivalentConnectedArea, Number, true, Chart: false),
            new("Value", "Ecosystem value", $"{currency} {over}", p => p.EcosystemValueOutlook, Number, true),
            new("Value", "Maintenance cost", $"{currency} {over}", p => p.MaintenanceCostOutlook, Number, false),
            new("Value", "Net value", $"{currency} {over}", p => p.NetValueOutlook, Number, true),
        ];
    }

    private void RebuildScenarioOverview(string unitSystem, Func<string?, bool> matchesLevel, Func<NormalizedTreeMetrics, bool> matchesSearch)
    {
        if (_lastReport is null)
        {
            return;
        }

        var searchText = SpeciesSearchBox.Text?.Trim() ?? string.Empty;
        bool TreeMatches(DashboardTreeItem tree) => matchesLevel(tree.LevelName) &&
            (string.IsNullOrEmpty(searchText) ||
             (tree.SpeciesCode ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
             (tree.CommonName ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase));

        var inputs = new ScenarioPerformanceInputs(
            _lastReport.Trees.Where(TreeMatches).ToList(),
            _lastReport.Floors.Where(floor => matchesLevel(floor.LevelName)).ToList(),
            _sets,
            _lastReport.TreeTimelines,
            unitSystem,
            _preferredCurrency,
            _usdExchangeRate,
            _siteBoundary?.AreaSquareMeters,
            _projectionYears,
            _growthYearsFromWorksets ? _projectionYears : null,
            _growthYearsFromWorksets ? _growthYearStops : [],
            _connectivityResults);

        var offered = OfferedScenarios();
        var performances = offered.Select(scenario => ScenarioPerformanceCalculator.Build(scenario, inputs)).ToList();
        var reference = ReferenceScenario(offered, _referenceScenarioName);
        var referencePerformance = performances.FirstOrDefault(performance => ReferenceEquals(performance.Scenario, reference)) ?? performances.FirstOrDefault();
        var metrics = ScenarioMetrics(unitSystem, _projectionYears);

        ScenarioIntroText.Text = _sets.Count == 0
            ? "This model has no design options, so there's one scenario: the main model."
            : $"{performances.Count:N0} scenario(s) side by side, each one option per set ({string.Join(", ", _sets.Select(set => set.SetName))}). " +
              $"Differences are against \"{referencePerformance?.Scenario.Name}\". Save scenarios with Edit… above.";
        ProjectionMethodText.Text =
            "Each scenario counts the main model plus its chosen option in every set; elements in other options are left out. " +
            "A scheme counts what stands after development (demolished trees and planting areas are left out); the existing site counts what stood in the first phase. " +
            $"Outlook totals add up year 1 to year {_projectionYears}" + (performances.FirstOrDefault()?.Method switch
            {
                TreeOutlookMethod.Timeline => ": trees from i-Tree's year-by-year forecast, planting areas at their yearly rate.",
                TreeOutlookMethod.GrowthWorksets => ": trees interpolated between growth-year worksets, planting areas at their yearly rate.",
                _ => ": annual × years, since no tree growth is modelled."
            });

        BuildScenarioTable(performances, referencePerformance, metrics);
        BuildScenarioCharts(performances, referencePerformance, metrics);
    }

    private void BuildScenarioTable(IReadOnlyList<ScenarioPerformance> performances, ScenarioPerformance? reference, IReadOnlyList<ScenarioMetric> metrics)
    {
        var host = ScenarioTableHost;
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        foreach (var _ in performances)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        }

        var muted = (Brush)Resources["KpiMutedBrush"];
        var border = (Brush)Resources["KpiBorderBrush"];
        var badge = (Brush)Resources["KpiBadgeBrush"];
        var eyebrow = (Style)Resources["KpiEyebrowStyle"];
        Brush Brush(string key) => (Brush)(key.StartsWith("Kpi", StringComparison.Ordinal) ? Resources[key] : Application.Current.Resources[key]);

        // Header: the metric column's name, then each scenario's name, description and badge,
        // right-aligned over its numbers.
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(new TextBlock { Text = "Metric", Style = eyebrow, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 10, 10) }, 0, 0);
        for (var column = 0; column < performances.Count; column++)
        {
            var performance = performances[column];
            var isReference = ReferenceEquals(performance, reference);
            var header = new StackPanel { Spacing = 2, Padding = new Thickness(10, 6, 12, 10), VerticalAlignment = VerticalAlignment.Bottom };
            if (isReference)
            {
                // Above the name, so every scenario's name and description line up at the bottom.
                header.Children.Add(new Border
                {
                    Background = badge,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Child = new TextBlock { Text = "REFERENCE", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Resources["KpiAccentTextBrush"] }
                });
            }

            header.Children.Add(new TextBlock { Text = performance.Scenario.Name, FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Right });
            header.Children.Add(new TextBlock { Text = performance.Description, Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextAlignment = TextAlignment.Right });

            ToolTipService.SetToolTip(header, DesignScenarios.Label(performance.Scenario, _sets));
            Place(header, 0, column + 1);
        }

        // Metrics are rows here, so each group's swim lane runs across: a pale band behind the
        // group's name and its rows, with a little white space between neighbouring bands.
        var gridRow = 1;
        foreach (var group in metrics.GroupBy(metric => metric.Group))
        {
            var (lane, text) = ScenarioLane(group.Key);
            var firstRow = gridRow;
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(new TextBlock { Text = group.Key.ToUpperInvariant(), Style = eyebrow, Foreground = Brush(text), Margin = new Thickness(12, 10, 10, 2) }, gridRow++, 0);

            var first = true;
            foreach (var metric in group)
            {
                host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (!first)
                {
                    var separator = new Border { BorderBrush = border, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(12, 0, 12, 0) };
                    Grid.SetColumnSpan(separator, performances.Count + 1);
                    Place(separator, gridRow, 0);
                }

                first = false;
                var label = new StackPanel { Margin = new Thickness(12, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = metric.Label, TextWrapping = TextWrapping.Wrap });
                label.Children.Add(new TextBlock { Text = metric.Unit, Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                Place(label, gridRow, 0);

                var referenceValue = reference is null ? null : metric.Value(reference);
                for (var column = 0; column < performances.Count; column++)
                {
                    var performance = performances[column];
                    var value = metric.Value(performance);
                    var cell = new StackPanel { Padding = new Thickness(10, 7, 12, 7), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                    cell.Children.Add(new TextBlock { Text = value is { } number ? metric.Format(number) : "—", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
                    if (!ReferenceEquals(performance, reference) && value is not null && referenceValue is not null)
                    {
                        var difference = DifferenceText(metric, value, referenceValue);
                        difference.HorizontalAlignment = HorizontalAlignment.Right;
                        cell.Children.Add(difference);
                    }

                    Place(cell, gridRow, column + 1);
                }

                gridRow++;
            }

            if (lane is not null)
            {
                var band = new Border { Background = Brush(lane), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 3, 0, 3) };
                Grid.SetRow(band, firstRow);
                Grid.SetRowSpan(band, gridRow - firstRow);
                Grid.SetColumnSpan(band, performances.Count + 1);
                host.Children.Insert(0, band);
            }
        }

        void Place(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            host.Children.Add(element);
        }
    }

    private static TextBlock DifferenceText(ScenarioMetric metric, double? value, double? reference)
    {
        var (absolute, percent) = ScenarioPerformanceCalculator.Difference(value, reference);
        var delta = absolute ?? 0d;
        var text = metric.IsPercent
            ? $"{delta:+0.0;-0.0;0.0} pts"
            : $"{(delta >= 0 ? "+" : "−")}{metric.Format(Math.Abs(delta))}{(percent is { } relative ? $" ({relative:+0;-0;0}%)" : string.Empty)}";
        var better = metric.HigherIsBetter ? delta > 1e-9 : delta < -1e-9;
        var worse = metric.HigherIsBetter ? delta < -1e-9 : delta > 1e-9;
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources[better ? "LimSuccessBrush" : worse ? "LimErrorBrush" : "LimNeutralBrush"]
        };
    }

    private void BuildScenarioCharts(IReadOnlyList<ScenarioPerformance> performances, ScenarioPerformance? reference, IReadOnlyList<ScenarioMetric> metrics)
    {
        var host = ScenarioChartsHost;
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();
        const int columns = 3;
        for (var column = 0; column < columns; column++)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var charted = metrics.Where(metric => metric.Chart).ToList();
        for (var index = 0; index < charted.Count; index++)
        {
            if (index % columns == 0)
            {
                host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var metric = charted[index];
            var values = performances.Select(performance => (Performance: performance, Value: metric.Value(performance))).ToList();
            var maximum = values.Max(pair => Math.Abs(pair.Value ?? 0d));
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(new TextBlock { Text = metric.Label, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            card.Children.Add(new TextBlock
            {
                Text = $"{metric.Unit} · {(metric.HigherIsBetter ? "higher is better" : "lower is better")}",
                FontSize = 11,
                Foreground = (Brush)Resources["KpiMutedBrush"]
            });

            foreach (var (performance, value) in values)
            {
                var isReference = ReferenceEquals(performance, reference);
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                var label = new TextBlock
                {
                    Text = performance.Scenario.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = isReference ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTipService.SetToolTip(label, DesignScenarios.Label(performance.Scenario, _sets));
                var bar = new ProgressBar
                {
                    Maximum = 100,
                    Value = maximum <= 0 || value is null ? 0 : 100d * Math.Abs(value.Value) / maximum,
                    Height = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Resources[value is < 0 ? "KpiChartAmberBrush" : isReference ? "KpiForestBrush" : "KpiLeafBrush"],
                    Background = (Brush)Resources["KpiChartTrackBrush"]
                };
                var number = new TextBlock
                {
                    Text = value is { } v ? metric.Format(v) : "—",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(bar, 1);
                Grid.SetColumn(number, 2);
                row.Children.Add(label);
                row.Children.Add(bar);
                row.Children.Add(number);
                card.Children.Add(row);
            }

            var surface = new Border { Style = (Style)Resources["KpiSurfaceStyle"], Padding = new Thickness(18, 14, 18, 14), Child = card };
            Grid.SetRow(surface, index / columns);
            Grid.SetColumn(surface, index % columns);
            host.Children.Add(surface);
        }

        ScenarioChartCaptionText.Text = performances.Count < 2
            ? "Save a second scenario (Edit… above) to compare them here."
            : $"Each bar is one scenario, scaled to the largest; the reference (\"{reference?.Scenario.Name}\") is darker. Amber bars are negative values.";
    }
}
