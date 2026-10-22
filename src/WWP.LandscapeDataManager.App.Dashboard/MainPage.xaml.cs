using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.Storage;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.SharedUI;

namespace WWP.LandscapeDataManager.App.Dashboard;

public sealed partial class MainPage : Page
{
    private const string AllLevels = "All levels";
    private const string ProjectDefaultUnitSystem = "Project default";

    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;

    private DashboardReportResult? _lastReport;
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";
    private double _usdExchangeRate = 1d;

    private string _unitSystemOverride = ProjectDefaultUnitSystem;
    private bool _showAnnual = true;
    private int _projectionYears = 10;

    // The growth-year slider's stops. From tree worksets when the model has growth-year worksets
    // (then the slider also filters which trees count), else the standard outlook years.
    private IReadOnlyList<int> _growthYearStops = GrowthYears.StandardYears;
    private bool _growthYearsFromWorksets;
    private string? _activeStatusFilter;
    private string _selectedLevel = AllLevels;
    private bool _suppressFilterEvents;

    // Design scenarios: one option per design option set. Every number on the page is for _scenario;
    // elements in options it doesn't choose are left out (see DesignScenarios).
    private IReadOnlyList<DesignOptionSetItem> _sets = [];
    private IReadOnlyList<DesignScenario> _savedScenarios = [];
    private DesignScenario _scenario = new(DesignScenarios.PrimaryScenarioName, new Dictionary<string, string>());

    // A scenario shown but not saved (from Edit… or Use current view); listed until another is picked.
    private DesignScenario? _unsavedScenario;
    private string? _referenceScenarioName;

    // True when a growth year's benefit figures come from the trees' i-Tree forecasts (see RebuildRows).
    private bool _benefitsFromForecast;

    private OriginalDataWindow? _originalDataWindow;

    // When set, the report on screen came from DashboardSnapshotCache rather than live Revit.
    private DateTimeOffset? _cachedSnapshotSavedAt;

    // Canopy Cover and Softscape Surface Ratio divide by this; see SiteBoundaryResolver.
    private SiteBoundaryOption? _siteBoundary;
    private string? _savedSiteBoundaryKey;
    private bool _siteBoundaryPicked;

    public MainPage()
    {
        InitializeComponent();
        // Rebuild code-generated chart strokes, metric colours and active-card borders as well as XAML.
        ThemeResources.Observe(this, RebuildRows);
    }

    public ObservableCollection<SpeciesSubtotalRow> SpeciesSubtotals { get; } = [];
    public ObservableCollection<FloorSubtotalRow> FloorSubtotals { get; } = [];
    public ObservableCollection<DashboardTreeRow> TreeRows { get; } = [];
    public ObservableCollection<DashboardFloorRow> FloorRows { get; } = [];
    public ObservableCollection<KpiBarRow> ProjectionTrendRows { get; } = [];
    public ObservableCollection<KpiBarRow> ImpactProfileRows { get; } = [];
    public ObservableCollection<KpiBarRow> SpeciesMixRows { get; } = [];
    public ObservableCollection<KpiBarRow> FloorMixRows { get; } = [];
    public ObservableCollection<KpiBarRow> BngHabitatRows { get; } = [];
    public ObservableCollection<KpiBarRow> StatusBreakdownRows { get; } = [];
    public ObservableCollection<KpiBarRow> AirPollutantRows { get; } = [];
    public ObservableCollection<KpiBarRow> ConnectivityBandRows { get; } = [];
    public ObservableCollection<KpiBarRow> HabitatMixRows { get; } = [];
    public ObservableCollection<SpeciesPaletteRow> PaletteRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        _pipeName = pipeName;
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

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(RefreshAsync);

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var cached = await DashboardSnapshotCache.TryLoadAsync();
        if (cached is null || _lastReport is not null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            // Flagged and labelled before applying: ApplyReportAsync can wait seconds on the rate
            // lookup, and in that window the page already shows the snapshot — an export must still
            // ask first, and the header mustn't say "No snapshot loaded".
            _cachedSnapshotSavedAt = cached.SavedAt;
            CacheStatusText.Text = $"Cached {cached.SavedAt.LocalDateTime:g}";
            StatusText.Text = $"Showing cached snapshot from {cached.SavedAt.LocalDateTime:g}. Select Refresh model for current Revit data.";
            await ApplyReportAsync(cached.Report);
        });
    }

    private async Task RefreshAsync()
    {
        var selectedOnly = ScopeBox.SelectedIndex == 1;
        try
        {
            var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
            var reportTask = GetClient().SendAsync<DashboardReportResult>(PipeCommands.GetDashboardReport, new DashboardReportRequest(selectedOnly));
            await Task.WhenAll(catalogTask, reportTask);

            var catalog = await catalogTask;
            var report = await reportTask;
            _preferredUnitSystem = catalog.PreferredUnitSystem;
            _preferredCurrency = catalog.PreferredCurrency;
            if (await TryPullProjectSettingsAsync() is { } settings)
            {
                _savedSiteBoundaryKey = settings.SiteBoundary ?? _savedSiteBoundaryKey;
                _savedScenarios = settings.DesignScenarios ?? [];
                _referenceScenarioName = settings.ReferenceScenario;
            }

            _cachedSnapshotSavedAt = null;
            await ApplyReportAsync(report);
            await DashboardSnapshotCache.SaveAsync(report);

            CacheStatusText.Text = $"Updated {DateTime.Now:t}";
            StatusText.Text = $"{report.Trees.Count:N0} tree(s), {report.Floors.Count:N0} planting area(s) — {EffectiveUnitSystem()} / {_preferredCurrency}. Snapshot cached.";
        }
        catch (Exception liveException)
        {
            var cached = await DashboardSnapshotCache.TryLoadAsync();
            if (cached is null)
            {
                throw;
            }

            // Flagged and labelled before applying, for the same reason as Page_Loaded.
            _cachedSnapshotSavedAt = cached.SavedAt;
            CacheStatusText.Text = $"Cached {cached.SavedAt.LocalDateTime:g}";
            StatusText.Text = $"Revit refresh failed ({liveException.Message}). Showing cached snapshot from {cached.SavedAt.LocalDateTime:g}.";
            await ApplyReportAsync(cached.Report);
        }
    }

    private async Task ApplyReportAsync(DashboardReportResult report)
    {
        _lastReport = report;
        _preferredUnitSystem = report.PreferredUnitSystem;
        _preferredCurrency = report.PreferredCurrency;
        CurrencyText.Text = _preferredCurrency;
        ProjectTitleText.Text = report.DocumentTitle;

        // Draw everything first: the rate lookup asks Revit for the project's fixed rate, and with
        // Revit closed (a cached snapshot) that request only fails after the pipe's ~8 s connect
        // timeout — the filters, slider and default scenario mustn't wait for it.
        RebuildGrowthYearSlider();
        RebuildFilterOptions();
        RebuildSiteBoundaryOptions();
        RebuildRows();
        _originalDataWindow?.ShowReport(report);

        // One rate lookup per report, not per tree — the rate doesn't vary by tree, only by currency.
        // It only changes figures stored in a different currency, so refresh them once it arrives.
        var rate = await ProjectExchangeRate.GetUsdRateAsync(GetClient(), _exchangeRateService, _preferredCurrency);
        if (!ReferenceEquals(_lastReport, report))
        {
            return; // a newer report arrived while this lookup was waiting
        }

        _usdExchangeRate = rate.UsdRate;
        if (rate.IsFixed)
        {
            CurrencyText.Text = $"{_preferredCurrency} · fixed {rate.UsdRate:G6}";
        }

        ToolTipService.SetToolTip(CurrencyText, rate.IsFixed
            ? $"1 USD = {rate.UsdRate:G6} {_preferredCurrency}, the project's fixed rate (Settings > Units & currency)."
            : $"1 USD = {rate.UsdRate:G6} {_preferredCurrency}, today's rate.");
        RebuildRows();
    }

    private string EffectiveUnitSystem() =>
        _unitSystemOverride == ProjectDefaultUnitSystem ? _preferredUnitSystem : _unitSystemOverride;

    private void RebuildFilterOptions()
    {
        if (_lastReport is null)
        {
            return;
        }

        _suppressFilterEvents = true;
        try
        {
            _sets = DesignScenarios.ResolveSets(
                _lastReport.DesignOptionSets,
                _lastReport.Trees.Select(tree => tree.DesignOption)
                    .Concat(_lastReport.Floors.Select(floor => floor.DesignOption))
                    .Concat(_lastReport.Lighting.Select(fixture => fixture.DesignOption)));
            RebuildScenarioOptions();

            var levels = _lastReport.Trees.Select(tree => tree.LevelName)
                .Concat(_lastReport.Floors.Select(floor => floor.LevelName))
                .Select(level => level ?? "—")
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            LevelBox.Items.Clear();
            LevelBox.Items.Add(AllLevels);
            foreach (var level in levels)
            {
                LevelBox.Items.Add(level);
            }

            LevelBox.SelectedItem = LevelBox.Items.Contains(_selectedLevel) ? _selectedLevel : AllLevels;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void RebuildRows()
    {
        if (_lastReport is null)
        {
            return;
        }

        var unitSystem = EffectiveUnitSystem();
        var normalizedTrees = _lastReport.Trees
            .Select(tree => DashboardAggregationService.NormalizeTree(tree, unitSystem, _preferredCurrency, _usdExchangeRate))
            .ToList();

        var searchText = SpeciesSearchBox.Text?.Trim() ?? string.Empty;
        bool MatchesLevel(string? levelName) => _selectedLevel == AllLevels || (levelName ?? "—") == _selectedLevel;
        bool MatchesSearch(NormalizedTreeMetrics tree) => string.IsNullOrEmpty(searchText) ||
            (tree.Source.SpeciesCode ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (tree.Source.CommonName ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase);
        bool MatchesDimensionFilters(DesignOptionInfo designOption, string? levelName) =>
            DesignScenarios.Includes(_scenario, designOption, _sets) && MatchesLevel(levelName);

        bool MatchesStatus(NormalizedTreeMetrics tree) => _activeStatusFilter is null || tree.Source.Status == _activeStatusFilter;
        var allYearTrees = normalizedTrees
            .Where(tree => MatchesDimensionFilters(tree.Source.DesignOption, tree.Source.LevelName))
            .Where(MatchesSearch)
            .ToList();
        // The growth year's trees as modelled: that year's copies plus trees on no growth workset.
        // They drive canopy and the other geometry-based site figures.
        var geometryTrees = allYearTrees
            .Where(tree => !_growthYearsFromWorksets || GrowthYears.CountsIn(tree.Source, _projectionYears))
            .ToList();

        // Same filters without the growth-year one: the outlook needs every year's copies.
        var treesForOutlook = allYearTrees.Where(MatchesStatus).ToList();
        var treeOutlook = CreateTreeOutlook(treesForOutlook, unitSystem);

        // With i-Tree forecasts, a growth year's benefits are each planted tree's forecast for that
        // year: copies whose trunk doesn't grow in the family would report the planting-size benefit.
        _benefitsFromForecast = _growthYearsFromWorksets && treeOutlook.Method == TreeOutlookMethod.Timeline;
        var treesInScope = _benefitsFromForecast
            ? CreateTreeOutlook(allYearTrees, unitSystem).InYear(_projectionYears)
            : geometryTrees;

        UpdateStatusCounts(treesInScope);

        var filteredTrees = treesInScope.Where(MatchesStatus).ToList();
        var filteredGeometryTrees = geometryTrees.Where(MatchesStatus).ToList();
        var filteredFloors = _lastReport.Floors
            .Where(floor => MatchesDimensionFilters(floor.DesignOption, floor.LevelName))
            .ToList();

        var units = DisplayUnits.For(unitSystem);
        var speciesSubtotals = DashboardAggregationService.AggregateTreesBySpecies(filteredTrees);
        SpeciesSubtotals.Clear();
        foreach (var subtotal in speciesSubtotals)
        {
            SpeciesSubtotals.Add(new SpeciesSubtotalRow(subtotal, units, _showAnnual));
        }

        var floorSubtotals = DashboardAggregationService.AggregateFloorsByType(filteredFloors);
        FloorSubtotals.Clear();
        foreach (var subtotal in floorSubtotals)
        {
            FloorSubtotals.Add(new FloorSubtotalRow(subtotal, units));
        }

        TreeRows.Clear();
        foreach (var tree in filteredTrees)
        {
            TreeRows.Add(new DashboardTreeRow(tree, unitSystem, _preferredCurrency, _showAnnual));
        }

        FloorRows.Clear();
        foreach (var floor in filteredFloors)
        {
            FloorRows.Add(new DashboardFloorRow(floor));
        }

        var grandTotal = DashboardAggregationService.BuildGrandTotal(filteredTrees, filteredFloors);
        var period = _showAnnual ? "Annual" : "Lifetime";
        var currency = _preferredCurrency;

        // Data explorer. Units live in the column headers, so table cells stay plain numbers.
        // Two-line headers — the metric, then its unit — so no header has to clip.
        var periodUnit = _showAnnual ? "per year" : "lifetime";
        SpeciesCarbonHeader.Text = $"Carbon\n{units.Carbon} {periodUnit}";
        SpeciesOxygenHeader.Text = $"Oxygen\n{units.Carbon} {periodUnit}";
        SpeciesCarbonCostHeader.Text = $"Carbon\n{currency} {periodUnit}";
        SpeciesRunoffHeader.Text = $"Runoff\n{units.Water} {periodUnit}";
        SpeciesStormCostHeader.Text = $"Water\n{currency} {periodUnit}";
        SpeciesUpkeepHeader.Text = $"Cost\n{currency} per year";
        SpeciesPollutionHeader.Text = $"Pollution\n{units.Pollutant} {periodUnit}";
        SpeciesAirCostHeader.Text = $"Air\n{currency} {periodUnit}";
        FloorAreaHeader.Text = $"Area\n{units.Area}";
        FloorCarbonHeader.Text = $"Carbon\n{units.Carbon} per year";
        FloorRunoffHeader.Text = $"Runoff\n{units.Water} per year";
        FloorPollutionHeader.Text = $"Pollution\n{units.Pollutant} per year";
        FloorOxygenHeader.Text = $"Oxygen\n{units.Carbon} per year";
        FloorEmbodiedHeader.Text = $"Embodied\n{units.Carbon} CO₂e";
        FloorMaintenanceHeader.Text = $"Cost\n{currency} per year";

        TreeCountText.Text = grandTotal.TreeCount.ToString("N0");
        var estimatedTrees = filteredTrees.Count(tree => tree.IsSheetEstimate);
        TreeExcludedText.Text = string.Join(" · ", new[]
        {
            estimatedTrees > 0 ? $"{estimatedTrees:N0} estimated from the WWP sheet (i-Tree couldn't calculate them)" : null,
            grandTotal.TreesExcludedFromTotals > 0 ? $"{grandTotal.TreesExcludedFromTotals:N0} excluded (not yet calculated)" : null
        }.Where(note => note is not null));
        // Grouped and ordered to match the public i-Tree "MyTree Benefits" report's own three
        // categories and their component metrics — each swim lane leads with that category's
        // money value, then the metrics behind it, in the report's own order.
        string Money(double value) => $"{DashboardFormat.Number(value)} {currency}";
        CarbonUptakeCostText.Text = Money(_showAnnual ? grandTotal.CarbonCostSavedAnnual : grandTotal.CarbonCostSavedLifetimeTotal);
        CarbonSequesteredSubText.Text = $"{DashboardFormat.Number(_showAnnual ? grandTotal.CarbonSequesteredAnnual : grandTotal.CarbonSequesteredLifetimeTotal)} {units.Carbon}";
        CO2EquivalentSubText.Text = $"{DashboardFormat.Number(_showAnnual ? grandTotal.CO2EquivalentAnnual : grandTotal.CO2EquivalentLifetimeTotal)} {units.Carbon}";
        OxygenSubText.Text = $"{DashboardFormat.Number(_showAnnual ? grandTotal.OxygenProducedAnnual : grandTotal.OxygenProducedLifetimeTotal)} {units.Carbon}";

        StormWaterCostText.Text = Money(_showAnnual ? grandTotal.StormWaterCostSavedAnnual : grandTotal.StormWaterCostSavedLifetimeTotal);
        RunoffAvoidedSubText.Text = $"{DashboardFormat.Number(units.Volume(_showAnnual ? grandTotal.RunoffAvoidedAnnual : grandTotal.RunoffAvoidedLifetimeTotal))} {units.Water}";
        RainfallInterceptedSubText.Text = $"{DashboardFormat.Number(units.Volume(_showAnnual ? grandTotal.RainfallInterceptedAnnual : grandTotal.RainfallInterceptedLifetimeTotal))} {units.Water}";

        AirPollutionCostText.Text = Money(_showAnnual ? grandTotal.AirPollutionCostSavedAnnual : grandTotal.AirPollutionCostSavedLifetimeTotal);

        // Bar chart: one nominal category per pollutant, all bars the same accent hue (a value-ramp
        // here would double-encode magnitude as both length and darkness — see dataviz anti-patterns).
        AirPollutantRows.Clear();
        var pollutants = new (string Label, double Value)[]
        {
            ("Carbon Monoxide", _showAnnual ? grandTotal.CORemovedAnnual : grandTotal.CORemovedLifetimeTotal),
            ("Ozone", _showAnnual ? grandTotal.O3RemovedAnnual : grandTotal.O3RemovedLifetimeTotal),
            ("Nitrogen Dioxide", _showAnnual ? grandTotal.NO2RemovedAnnual : grandTotal.NO2RemovedLifetimeTotal),
            ("Sulfur Dioxide", _showAnnual ? grandTotal.SO2RemovedAnnual : grandTotal.SO2RemovedLifetimeTotal),
            ("PM2.5", _showAnnual ? grandTotal.PM25RemovedAnnual : grandTotal.PM25RemovedLifetimeTotal),
        };
        var pollutantTotal = pollutants.Sum(p => p.Value);
        foreach (var (label, value) in pollutants)
        {
            AirPollutantRows.Add(new KpiBarRow(
                label,
                $"{DashboardFormat.Number(value)} {units.Pollutant}",
                pollutantTotal <= 0d ? 0d : 100d * value / pollutantTotal));
        }

        // Ring chart: benefit composition is genuinely part-to-whole with only 3 segments, the one
        // case the dataviz standards allow a donut for — categorical color (fixed order), a table-
        // equivalent legend with direct percentage labels so nothing is color-only.
        var carbonCostRaw = _showAnnual ? grandTotal.CarbonCostSavedAnnual : grandTotal.CarbonCostSavedLifetimeTotal;
        var stormCostRaw = _showAnnual ? grandTotal.StormWaterCostSavedAnnual : grandTotal.StormWaterCostSavedLifetimeTotal;
        var airCostRaw = _showAnnual ? grandTotal.AirPollutionCostSavedAnnual : grandTotal.AirPollutionCostSavedLifetimeTotal;
        var benefitTotal = carbonCostRaw + stormCostRaw + airCostRaw;

        RingTotalText.Text = $"{Compact(benefitTotal)} {currency}";
        RingCarbonLegendText.Text = benefitTotal > 0d ? $"{100d * carbonCostRaw / benefitTotal:N0}%" : "—";
        RingStormWaterLegendText.Text = benefitTotal > 0d ? $"{100d * stormCostRaw / benefitTotal:N0}%" : "—";
        RingAirPollutionLegendText.Text = benefitTotal > 0d ? $"{100d * airCostRaw / benefitTotal:N0}%" : "—";

        BuildDonutRing(BenefitRingHost, 150d, 18d,
        [
            (carbonCostRaw, (Brush)Resources["KpiAccentTextBrush"]),
            (stormCostRaw, (Brush)Resources["KpiWaterTextBrush"]),
            (airCostRaw, (Brush)Resources["KpiChartAmberBrush"]),
        ], (Brush)Resources["KpiChartTrackBrush"]);

        FloorCountText.Text = grandTotal.FloorCount.ToString("N0");
        FloorAreaText.Text = $"{DashboardFormat.Number(units.AreaOf(grandTotal.FloorAreaSquareMeters))} {units.Area}";
        FloorCarbonText.Text = $"{DashboardFormat.Number(units.Mass(grandTotal.FloorCarbonSequesteredAnnual))} {units.Carbon}/yr";
        FloorGwpText.Text = $"embodied {DashboardFormat.Number(units.Mass(grandTotal.FloorTotalGwp))} {units.Carbon} CO₂e, one-off";
        FloorRunoffText.Text = $"{DashboardFormat.Number(units.Volume(grandTotal.FloorRunoffAvoidedAnnual))} {units.Water}/yr";
        FloorPollutionText.Text = $"{DashboardFormat.Number(units.PollutantMass(grandTotal.FloorPollutionMassRemovedAnnual))} {units.Pollutant}/yr pollution removed";
        FloorCostSavedText.Text = $"{DashboardFormat.Number(grandTotal.FloorCostSavedAnnual)} {currency}/yr";
        MaintenanceSummaryText.Text = grandTotal.MaintenanceCostAnnual > 0
            ? $"maintenance {DashboardFormat.Number(grandTotal.MaintenanceCostAnnual)} {currency}/yr (trees {DashboardFormat.Number(grandTotal.TreeMaintenanceCostAnnual)}, areas {DashboardFormat.Number(grandTotal.FloorMaintenanceCostAnnual)})"
            : "no maintenance rates imported yet";

        PeriodLabelText.Text = $"Showing {period} benefits for {DesignScenarios.Label(_scenario, _sets)}." + (_benefitsFromForecast
            ? $" Annual = each planted tree's i-Tree forecast for growth year {_projectionYears}; lifetime = its whole forecast, planting to maturity, so it's the same at every growth year."
            : _showAnnual ? string.Empty : " Lifetime = each tree's whole i-Tree forecast.");

        RebuildGraphicDashboard(
            treesForOutlook,
            filteredTrees,
            filteredGeometryTrees,
            filteredFloors,
            speciesSubtotals,
            floorSubtotals,
            grandTotal,
            treeOutlook,
            unitSystem,
            MatchesLevel,
            MatchesSearch);
    }

    /// <summary>
    /// Tree benefits accumulated over an outlook of N years, from i-Tree's timelines when the trees
    /// have them, else growth-year worksets, else the stored annual figure × N (see <see cref="TreeOutlook"/>).
    /// </summary>
    private TreeOutlook CreateTreeOutlook(IReadOnlyList<NormalizedTreeMetrics> treesAllYears, string unitSystem) =>
        new(treesAllYears, _growthYearsFromWorksets ? _growthYearStops : [], _lastReport?.TreeTimelines, unitSystem, _usdExchangeRate);

    private void RebuildGraphicDashboard(
        IReadOnlyList<NormalizedTreeMetrics> treesForOutlook,
        IReadOnlyList<NormalizedTreeMetrics> filteredTrees,
        IReadOnlyList<NormalizedTreeMetrics> geometryTrees,
        IReadOnlyList<DashboardFloorItem> filteredFloors,
        IReadOnlyList<SpeciesSubtotal> speciesSubtotals,
        IReadOnlyList<FloorTypeSubtotal> floorSubtotals,
        DashboardGrandTotal grandTotal,
        TreeOutlook treeOutlook,
        string unitSystem,
        Func<string?, bool> matchesLevel,
        Func<NormalizedTreeMetrics, bool> matchesSearch)
    {
        var units = DisplayUnits.For(unitSystem);
        var currency = _preferredCurrency;

        // Over the outlook: trees add up year by year (see TreeOutlook). Planting areas don't grow,
        // so their Airtable rate holds every year and they're annual × years.
        var years = _projectionYears;
        double Trees(Func<DashboardGrandTotal, double> annual) => treeOutlook.Accumulate(annual, years);
        var floorCarbonAnnual = units.Mass(grandTotal.FloorCarbonSequesteredAnnual);
        var floorCo2EquivalentAnnual = units.Mass(grandTotal.FloorCarbonSequesteredAnnual * UnitConversions.CarbonToCo2MassRatio);
        var floorPollutionAnnual = units.PollutantMass(grandTotal.FloorPollutionMassRemovedAnnual);
        var floorRunoffAnnual = units.Volume(grandTotal.FloorRunoffAvoidedAnnual);
        var floorOxygenAnnual = units.Mass(grandTotal.FloorOxygenProducedAnnual);
        var treeCarbon = Trees(total => total.CarbonSequesteredAnnual);
        var treeCo2Equivalent = Trees(total => total.CO2EquivalentAnnual);
        var treePollution = Trees(total => total.TotalPollutionMassRemovedAnnual);
        var treeRunoff = units.Volume(Trees(total => total.RunoffAvoidedAnnual));
        var projectedCarbon = treeCarbon + floorCarbonAnnual * years;
        var projectedCo2Equivalent = treeCo2Equivalent + floorCo2EquivalentAnnual * years;
        var projectedPollution = treePollution + floorPollutionAnnual * years;
        var projectedWater = treeRunoff + floorRunoffAnnual * years;
        var projectedOxygen = Trees(total => total.OxygenProducedAnnual) + floorOxygenAnnual * years;
        var projectedRainfall = units.Volume(Trees(total => total.RainfallInterceptedAnnual));
        // Floor cost is already in the project currency (Floor Calculator converts it when writing).
        var treeCarbonValue = Trees(total => total.CarbonCostSavedAnnual);
        var treeWaterValue = Trees(total => total.StormWaterCostSavedAnnual);
        var treeAirValue = Trees(total => total.AirPollutionCostSavedAnnual);
        var floorValue = grandTotal.FloorCostSavedAnnual * years;
        var projectedValue = Trees(total => total.TreeCostSavedAnnual) + floorValue;
        var maintenance = grandTotal.MaintenanceCostAnnual * years;
        string Money(double value) => $"{Compact(value)} {currency}";

        ScenarioBadgeText.Text = _scenario.Name.ToUpperInvariant();
        ToolTipService.SetToolTip(ScenarioBadgeText, DesignScenarios.Describe(_scenario, _sets));
        OutlookText.Text = OutlookMethodText(treeOutlook, years);

        // Banner: the value split by benefit, then upkeep and what's left.
        KpiEcosystemValueText.Text = Money(projectedValue);
        EcosystemValueCaptionText.Text = $"over {years} years · trees (i-Tree) + planting areas (WWP sheet)";
        BannerFloorValueText.Text = Money(floorValue);
        BannerMaintenanceText.Text = maintenance > 0 ? $"−{Money(maintenance)}" : Money(0);
        BannerNetValueText.Text = Money(projectedValue - maintenance);
        BannerMaintenanceDetailText.Text = grandTotal.MaintenanceCostAnnual > 0
            ? $"{Money(grandTotal.MaintenanceCostAnnual)}/yr"
            : "no maintenance rates imported";
        KpiTreeCountText.Text = grandTotal.TreeCount.ToString("N0");
        KpiPlantingAreaText.Text = grandTotal.FloorCount.ToString("N0");

        // Category cards: value over the outlook, the amounts behind it over the outlook, and the
        // yearly rate today next to them.
        HeroCarbonCostText.Text = Money(treeCarbonValue);
        HeroCarbonSequesteredText.Text = $"{Compact(projectedCarbon)} {units.Carbon}";
        HeroCO2EquivalentText.Text = $"{Compact(projectedCo2Equivalent)} {units.Carbon}";
        HeroOxygenText.Text = $"{Compact(projectedOxygen)} {units.Carbon}";
        var rateLabel = _benefitsFromForecast ? $"In year {_projectionYears}" : "Now";
        HeroCarbonRateText.Text = $"{rateLabel} {Compact(grandTotal.CO2EquivalentAnnual + floorCo2EquivalentAnnual)} {units.Carbon} CO₂e/yr";

        HeroStormWaterCostText.Text = Money(treeWaterValue);
        HeroRunoffAvoidedText.Text = $"{Compact(projectedWater)} {units.Water}";
        HeroRainfallInterceptedText.Text = $"{Compact(projectedRainfall)} {units.Water}";
        HeroWaterRateText.Text = $"{rateLabel} {Compact(units.Volume(grandTotal.RunoffAvoidedAnnual) + floorRunoffAnnual)} {units.Water}/yr runoff avoided";

        HeroAirPollutionCostText.Text = Money(treeAirValue);
        HeroPollutionTotalText.Text = $"{Compact(projectedPollution)} {units.Pollutant}";
        HeroCarbonMonoxideText.Text = $"{Compact(Trees(total => total.CORemovedAnnual))} {units.Pollutant}";
        HeroFloorPollutionText.Text = $"{Compact(floorPollutionAnnual * years)} {units.Pollutant}";
        HeroOzoneText.Text = $"{Compact(Trees(total => total.O3RemovedAnnual))} {units.Pollutant}";
        HeroNitrogenDioxideText.Text = $"{Compact(Trees(total => total.NO2RemovedAnnual))} {units.Pollutant}";
        HeroSulfurDioxideText.Text = $"{Compact(Trees(total => total.SO2RemovedAnnual))} {units.Pollutant}";
        HeroPM25Text.Text = $"{Compact(Trees(total => total.PM25RemovedAnnual))} {units.Pollutant}";
        HeroAirRateText.Text = $"{rateLabel} {Compact(grandTotal.TotalPollutionMassRemovedAnnual + floorPollutionAnnual)} {units.Pollutant}/yr removed";

        // Readiness now sits in Data explorer next to the status cards it summarises.
        var readyPercent = filteredTrees.Count == 0 ? 0d : 100d * grandTotal.TreeCount / filteredTrees.Count;
        ReadinessRing.Value = readyPercent;
        ReadinessPercentText.Text = $"{readyPercent:N0}%";
        ReadinessDetailText.Text = $"{grandTotal.TreeCount:N0} of {filteredTrees.Count:N0} filtered tree records are Calculated.";

        // Outlooks: carbon, runoff and pollution totals at each outlook year.
        OutlookCaptionText.Text = treeOutlook.Method switch
        {
            TreeOutlookMethod.Timeline => "Totals added up year by year from i-Tree's forecast for each tree; planting areas at their yearly rate.",
            TreeOutlookMethod.GrowthWorksets => "Totals added up year by year, interpolating between growth-year worksets; planting areas at their yearly rate.",
            _ => "Annual results × years: no tree growth is modelled yet (run Tree Calculator's timeline, or Growth Projections)."
        };
        IReadOnlyList<int> trendYears = _growthYearsFromWorksets ? _growthYearStops.Where(year => year > 0).ToList() : [5, 10, 25];
        IReadOnlyList<(int Year, double Value)> Trend(Func<int, double> valueAt) => trendYears.Select(year => (year, valueAt(year))).ToList();
        var co2Trend = Trend(year => treeOutlook.Accumulate(total => total.CO2EquivalentAnnual, year) + floorCo2EquivalentAnnual * year);
        BuildOutlookBars(
        [
            ("CO₂E UPTAKE", (Brush)Resources["KpiAccentTextBrush"], units.Carbon, co2Trend),
            ("RUNOFF AVOIDED", (Brush)Resources["KpiWaterTextBrush"], units.Water,
                Trend(year => units.Volume(treeOutlook.Accumulate(total => total.RunoffAvoidedAnnual, year)) + floorRunoffAnnual * year)),
            ("POLLUTION REMOVED", (Brush)Resources["KpiChartAmberBrush"], units.Pollutant,
                Trend(year => treeOutlook.Accumulate(total => total.TotalPollutionMassRemovedAnnual, year) + floorPollutionAnnual * year)),
        ]);

        // Kept for the SVG export's outlook strip.
        ProjectionTrendRows.Clear();
        var co2Maximum = co2Trend.Count == 0 ? 0d : co2Trend.Max(point => point.Value);
        foreach (var (year, value) in co2Trend)
        {
            ProjectionTrendRows.Add(new KpiBarRow($"{year} yr", $"{Compact(value)} {units.Carbon}", co2Maximum <= 0d ? 0d : 100d * value / co2Maximum));
        }

        // Ordered Carbon / Stormwater / Air Quality — the same three i-Tree benefit categories,
        // in the same order, as the KPI cards and donut ring.
        ImpactProfileRows.Clear();
        AddImpactShare("Carbon", treeCarbon, floorCarbonAnnual * years, units.Carbon);
        AddImpactShare("Runoff", treeRunoff, floorRunoffAnnual * years, units.Water);
        AddImpactShare("Pollution", treePollution, floorPollutionAnnual * years, units.Pollutant);

        // Project snapshot.
        var site = DashboardAggregationService.BuildSiteKpiSummary(geometryTrees, filteredFloors, [], _siteBoundary?.AreaSquareMeters, null);
        SnapshotCanopyText.Text = site.CanopyCoverPercent is { } canopyPercent ? $"{canopyPercent:N1}%" : "—";
        SnapshotCanopyDetailText.Text = site.CanopyCoverPercent is null
            ? $"{Compact(units.AreaOf(site.CanopyAreaSquareMeters))} {units.Area} canopy · pick a site boundary on Site & Biodiversity"
            : $"{Compact(units.AreaOf(site.CanopyAreaSquareMeters))} {units.Area} canopy, overlaps counted once";
        SnapshotWaterText.Text = $"{Compact(projectedWater)} {units.Water}";
        SnapshotWaterDetailText.Text = $"runoff avoided over {years} yr · {Compact(projectedRainfall)} {units.Water} rainfall intercepted by trees";
        SnapshotSpeciesText.Text = site.DistinctSpeciesCount.ToString("N0");
        var habitats = HabitatAreas(filteredFloors);
        if (habitats.Count > 0)
        {
            SnapshotHabitatText.Text = habitats.Count.ToString("N0");
            SnapshotHabitatDetailText.Text = $"BNG habitats on {Compact(units.AreaOf(habitats.Sum(habitat => habitat.Area)))} {units.Area}";
        }
        else
        {
            SnapshotHabitatText.Text = floorSubtotals.Count.ToString("N0");
            SnapshotHabitatDetailText.Text = "planting types — no BNG habitats assigned yet";
        }

        SnapshotAreaText.Text = $"{Compact(units.AreaOf(grandTotal.FloorAreaSquareMeters))} {units.Area}";
        // Embodied carbon of the hard surfaces vs the planting's yearly CO2e uptake (trees + floors).
        var embodied = units.Mass(grandTotal.FloorTotalGwp);
        var sequesteredPerYear = grandTotal.CO2EquivalentAnnual + floorCo2EquivalentAnnual;
        GwpBaselineText.Text = $"{Compact(embodied)} {units.Carbon} CO₂e";
        GwpPaybackText.Text = EmbodiedCarbon.PaybackYears(embodied, sequesteredPerYear) switch
        {
            0 => "no embodied carbon recorded",
            { } payback => $"payback ≈ {payback:N1} yrs of current planting uptake",
            null => "no sequestration yet to offset it"
        };

        // Habitat & connectivity.
        var bng = DashboardAggregationService.BuildBngSummary(filteredFloors, BngMetricCatalog.Default);
        if (bng.NetChangePercent is { } bngPercent)
        {
            OverviewBngText.Text = $"{bngPercent:+0.0;-0.0;0.0}%";
            OverviewBngText.Foreground = Tier(bngPercent, high: BngSummary.StatutoryNetGainPercent, medium: 0d);
            OverviewBngDetailText.Text = $"{bng.BaselineUnits:N2} → {bng.PostInterventionUnits:N2} habitat units" + (bng.IsComplete ? "" : " · provisional, some floors stale or not assessed");
        }
        else
        {
            OverviewBngText.Text = "—";
            OverviewBngText.ClearValue(TextBlock.ForegroundProperty);
            OverviewBngDetailText.Text = "No BNG baseline yet — assess floors in Floor Calculator's BNG tab.";
        }

        ConnectivityBandRows.Clear();
        var bands = site.ConnectivityBandCounts;
        var assessed = bands.Take(4).Sum();
        string[] bandNames = ["1 · Low", "2 · Moderate", "3 · Strong connector", "4 · Critical"];
        if (assessed > 0)
        {
            for (var band = 3; band >= 0; band--)
            {
                ConnectivityBandRows.Add(new KpiBarRow(bandNames[band], bands[band].ToString("N0"), 100d * bands[band] / assessed,
                    $"{bands[band]:N0} trees and planting areas in band {band + 1}"));
            }
        }

        ConnectivityEmptyText.Text = assessed == 0
            ? "No connectivity results yet — run the Connectivity Calculator."
            : bands[4] > 0 ? $"{bands[4]:N0} not assessed (new since the last Connectivity run, or not in a primary option)." : string.Empty;

        // Planting-area mix: planting types by area, and the BNG habitat types they make.
        FloorMixRows.Clear();
        var totalArea = floorSubtotals.Sum(row => row.AreaSquareMeters);
        foreach (var row in floorSubtotals.OrderByDescending(row => row.AreaSquareMeters).Take(8))
        {
            FloorMixRows.Add(new KpiBarRow(
                row.LdsType,
                $"{Compact(units.AreaOf(row.AreaSquareMeters))} {units.Area}",
                totalArea <= 0d ? 0d : 100d * row.AreaSquareMeters / totalArea,
                $"{row.LdsType}: {row.FloorCount:N0} floor(s)"));
        }

        HabitatMixRows.Clear();
        var habitatArea = habitats.Sum(habitat => habitat.Area);
        foreach (var habitat in habitats.Take(8))
        {
            HabitatMixRows.Add(new KpiBarRow(
                habitat.Name,
                $"{Compact(units.AreaOf(habitat.Area))} {units.Area}",
                habitatArea <= 0d ? 0d : 100d * habitat.Area / habitatArea,
                $"{habitat.Name}: {habitat.Floors:N0} floor(s)"));
        }

        HabitatMixEmptyText.Text = habitats.Count == 0 ? "No habitats assigned yet — Floor Calculator › BNG." : string.Empty;

        // Kept for the SVG export's species strip: the five most planted species.
        SpeciesMixRows.Clear();
        var totalTrees = Math.Max(1, speciesSubtotals.Sum(row => row.TreeCount));
        foreach (var row in speciesSubtotals.OrderByDescending(row => row.TreeCount).Take(5))
        {
            SpeciesMixRows.Add(new KpiBarRow(row.SpeciesCode, row.TreeCount.ToString("N0"), 100d * row.TreeCount / totalTrees, row.CommonName));
        }

        RebuildPalette(treesForOutlook, filteredTrees, unitSystem, years);
        RebuildScenarioOverview(unitSystem, matchesLevel, matchesSearch);
        UpdateSiteKpiCards(geometryTrees, filteredFloors);

        // Both values are already totals over the outlook.
        void AddImpactShare(string label, double treeValue, double floorValue, string valueUnit)
        {
            var total = treeValue + floorValue;
            var treePercent = total <= 0d ? 0d : 100d * treeValue / total;
            ImpactProfileRows.Add(new KpiBarRow(
                label,
                $"{treePercent:N0}% trees",
                treePercent,
                $"{Compact(total)} {valueUnit} over {years} years"));
        }
    }

    /// <summary>Floor area per BNG habitat: the proposed habitat, or the baseline one for a retained floor; lost floors are gone.</summary>
    private static IReadOnlyList<(string Name, double Area, int Floors)> HabitatAreas(IReadOnlyList<DashboardFloorItem> floors) =>
        floors
            .Where(floor => floor.Bng is { } bng && bng.PhaseRole != BngPhaseRoles.Lost && bng.PhaseRole != BngPhaseRoles.Excluded)
            .Select(floor => (Floor: floor, Name: floor.Bng!.PhaseRole == BngPhaseRoles.Retained ? floor.Bng.BaselineHabitat : floor.Bng.ProposedHabitat))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .GroupBy(pair => pair.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, group.Sum(pair => pair.Floor.AreaSquareMeters), group.Count()))
            .OrderByDescending(habitat => habitat.Item2)
            .ToList();

    // ---- Tree palette: one row per species, over the outlook, sortable ----

    private string _paletteSortKey = "Value";
    private bool _paletteDescending = true;

    private void RebuildPalette(IReadOnlyList<NormalizedTreeMetrics> treesForOutlook, IReadOnlyList<NormalizedTreeMetrics> treesThisYear, string unitSystem, int years)
    {
        var units = DisplayUnits.For(unitSystem);
        var counts = treesThisYear
            .Where(tree => tree.CountsTowardTotals)
            .GroupBy(SpeciesKey)
            .ToDictionary(group => group.Key, group => group.Count());
        var rows = treesForOutlook
            .Where(tree => tree.CountsTowardTotals)
            .GroupBy(SpeciesKey)
            .Select(group =>
            {
                var outlook = CreateTreeOutlook(group.ToList(), unitSystem);
                double Over(Func<DashboardGrandTotal, double> annual) => outlook.Accumulate(annual, years);
                return new SpeciesPaletteRow(
                    group.Key.Code,
                    group.Key.Name,
                    counts.GetValueOrDefault(group.Key),
                    Over(total => total.TreeCostSavedAnnual),
                    Over(total => total.CO2EquivalentAnnual),
                    units.Volume(Over(total => total.RunoffAvoidedAnnual)),
                    Over(total => total.TotalPollutionMassRemovedAnnual),
                    Over(total => total.OxygenProducedAnnual),
                    Over(total => total.TreeMaintenanceCostAnnual),
                    units,
                    _preferredCurrency);
            })
            .ToList();

        Func<SpeciesPaletteRow, IComparable> key = _paletteSortKey switch
        {
            "Name" => row => row.SpeciesCode,
            "Count" => row => row.Count,
            "Co2" => row => row.Co2,
            "Runoff" => row => row.Runoff,
            "Pollution" => row => row.Pollution,
            "Oxygen" => row => row.Oxygen,
            "Upkeep" => row => row.Upkeep,
            _ => row => row.Value
        };
        var sorted = _paletteDescending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        PaletteRows.Clear();
        foreach (var row in sorted)
        {
            PaletteRows.Add(row);
        }

        string Arrow(string column) => column == _paletteSortKey ? (_paletteDescending ? " ▼" : " ▲") : string.Empty;
        // Two-line headers like the Data explorer: the metric (and sort arrow), then its unit.
        PaletteNameHeader.Text = "Species" + Arrow("Name");
        PaletteCountHeader.Text = "Trees" + Arrow("Count");
        PaletteValueHeader.Text = $"Money saved{Arrow("Value")}\n{_preferredCurrency}";
        PaletteCo2Header.Text = $"CO₂e{Arrow("Co2")}\n{units.Carbon}";
        PaletteOxygenHeader.Text = $"Oxygen{Arrow("Oxygen")}\n{units.Carbon}";
        PaletteRunoffHeader.Text = $"Runoff{Arrow("Runoff")}\n{units.Water}";
        PalettePollutionHeader.Text = $"Pollution{Arrow("Pollution")}\n{units.Pollutant}";
        PaletteUpkeepHeader.Text = $"Cost{Arrow("Upkeep")}\n{_preferredCurrency}";
        PaletteCaptionText.Text = rows.Count == 0
            ? "No calculated trees in this scenario yet."
            : $"{rows.Count:N0} species · totals over {years} years · select a column to sort";

        static (string Code, string Name) SpeciesKey(NormalizedTreeMetrics tree) =>
            (tree.Source.SpeciesCode ?? "Unassigned", tree.Source.CommonName ?? tree.Source.SpeciesCode ?? "Unassigned");
    }

    private void PaletteSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string column })
        {
            return;
        }

        _paletteDescending = column == _paletteSortKey ? !_paletteDescending : column != "Name";
        _paletteSortKey = column;
        RebuildRows();
    }
    private void UpdateSiteKpiCards(IReadOnlyList<NormalizedTreeMetrics> filteredTrees, IReadOnlyList<DashboardFloorItem> filteredFloors)
    {
        if (_lastReport is null)
        {
            return;
        }

        var siteArea = _siteBoundary?.AreaSquareMeters;
        var summary = DashboardAggregationService.BuildSiteKpiSummary(
            filteredTrees, filteredFloors, FilteredLighting(), siteArea, _lastReport.HabitatConnectivityScore);

        const string noSiteArea = "Pick a site boundary above: a property line, or the area entered on Project Information (!_S_PLT_Site_TotalArea_Area).";
        SetPercentCard(CanopyCoverText, CanopyCoverDetailText, summary.CanopyCoverPercent,
            $"{summary.CanopyAreaSquareMeters:N0} m² canopy over {siteArea:N0} m² site — overlapping crowns counted once" +
            (summary.CanopySummedAreaSquareMeters > summary.CanopyAreaSquareMeters + 0.5
                ? $" ({summary.CanopySummedAreaSquareMeters - summary.CanopyAreaSquareMeters:N0} m² of overlap)."
                : "."),
            noSiteArea,
            high: 30d, medium: 15d);

        var unclassified = filteredFloors.Count(floor => DashboardAggregationService.ResolveSurfaceClass(floor) is null);
        SetPercentCard(SoftscapeRatioText, SoftscapeRatioDetailText, summary.SoftscapeSurfaceRatioPercent,
            $"{summary.PerviousAreaSquareMeters:N0} m² pervious over {siteArea:N0} m² site" +
            (unclassified > 0
                ? $" · {unclassified:N0} floor(s) not classified — their type name and LDS type say nothing about the surface; match them in Floor Calculator, or tag !_S_PLT_LDS_SurfaceClass_Text (Pervious/Impervious) on the floor type."
                : string.Empty),
            noSiteArea,
            high: 50d, medium: 25d);

        var leftOut = new List<string>();
        if (summary.UnclassifiedFloorCount > 0)
        {
            leftOut.Add($"{summary.UnclassifiedFloorCount:N0} unclassified floor(s), {summary.UnclassifiedAreaSquareMeters:N0} m² (match them in Floor Calculator or tag !_S_PLT_LDS_SurfaceClass_Text)");
        }

        if (summary.BuildingSlabCount > 0)
        {
            leftOut.Add($"{summary.BuildingSlabCount:N0} building slab(s), {summary.BuildingSlabAreaSquareMeters:N0} m²");
        }

        SetPercentCard(GreenspaceRatioText, GreenspaceRatioDetailText, summary.GreenspaceRatioPercent,
            $"{summary.PerviousAreaSquareMeters:N0} m² pervious : {summary.ImperviousAreaSquareMeters:N0} m² impervious" +
            (leftOut.Count > 0 ? $" · left out: {string.Join("; ", leftOut)}." : "."),
            leftOut.Count > 0 ? $"No pervious or impervious floors yet · left out: {string.Join("; ", leftOut)}." : "No classified floors in this scenario.",
            high: 50d, medium: 25d);

        BiodiversityAddedText.Text = $"{summary.DistinctSpeciesCount:N0} species";
        BiodiversityAddedDetailText.Text = "Species richness in this scenario. Design scenarios compares it with the reference scenario.";

        SetPercentCard(NativeSpeciesRatioText, NativeSpeciesRatioDetailText, summary.NativeSpeciesRatioPercent,
            $"{summary.NativeTreeCount:N0} native : {summary.AdaptiveTreeCount:N0} adaptive of {summary.TreesWithNativeStatusCount:N0} tagged trees",
            "Tag !_S_PLT_iTreeSpecies_NativeStatus_Text on Planting Types to see a ratio.",
            high: 80d, medium: 60d);

        PhenologicalResilienceText.Text = $"{summary.DistinctBloomMonthsCount} months";
        PhenologicalResilienceDetailText.Text = "Distinct calendar months with a bloom/seed source, from !_S_PLT_iTreeSpecies_BloomMonths_Text.";
        PhenologicalResilienceText.Foreground = Tier(summary.DistinctBloomMonthsCount, high: 9d, medium: 6d);

        FunctionalDiversityText.Text = $"{summary.DistinctEcologicalFunctionsCount} function{(summary.DistinctEcologicalFunctionsCount == 1 ? "" : "s")}";
        FunctionalDiversityDetailText.Text = $"{summary.MultiFunctionAreaSquareMeters:N0} m² of canopy from multi-function species.";

        // The Connectivity Calculator's bands come first; the hand-entered GIS score is the fallback.
        var bands = summary.ConnectivityBandCounts;
        var assessed = bands.Take(4).Sum();
        if (assessed > 0)
        {
            var connectors = bands[2] + bands[3];
            HabitatConnectivityText.Text = $"{100d * connectors / assessed:N0}% connectors";
            HabitatConnectivityDetailText.Text =
                $"Band 4 critical {bands[3]:N0} · 3 strong {bands[2]:N0} · 2 moderate {bands[1]:N0} · 1 low {bands[0]:N0}" +
                (bands[4] > 0 ? $" · {bands[4]:N0} not assessed" : string.Empty) +
                (summary.HabitatConnectivityScore is { } gisScore ? $" · GIS score {gisScore:N1}/10" : string.Empty);
        }
        else if (summary.HabitatConnectivityScore is { } connectivityScore)
        {
            HabitatConnectivityText.Text = $"{connectivityScore:N1} / 10";
            HabitatConnectivityDetailText.Text = "Entered from external GIS connectivity modelling (site-wide, not per design option). Run the Connectivity Calculator for per-element bands.";
        }
        else
        {
            HabitatConnectivityText.Text = "—";
            HabitatConnectivityDetailText.Text = "Run the Connectivity Calculator, or enter !_S_PLT_Site_HabitatConnectivityScore_Number on Project Information after GIS analysis.";
        }

        SetPercentCard(LightingImpactText, LightingImpactDetailText, summary.LightingCompliancePercent,
            $"{summary.DarkSkyCompliantFixtureCount:N0} of {summary.LightingFixtureCount:N0} fixtures compliant",
            "Tag !_S_PLT_Lighting_DarkSkyCompliant_Text on Lighting Fixture Types to see a percentage.",
            high: 80d, medium: 50d);

        // From the WWP landscape data sheet, which i-Tree doesn't model: Tree Calculator and Floor
        // Calculator store pollen per tree and per planting area; allergenicity and irrigation demand
        // are on the tree types (Excel Importer).
        var pollen = summary.TreePollenAnnual + summary.FloorPollenAnnual;
        PollenText.Text = pollen > 0 ? $"{DashboardFormat.Number(pollen)} kg/yr" : "—";
        PollenDetailText.Text = pollen > 0
            ? $"Trees {DashboardFormat.Number(summary.TreePollenAnnual)} kg · planting areas {DashboardFormat.Number(summary.FloorPollenAnnual)} kg a year, from the WWP sheet."
            : "Fill trees from the WWP sheet in Tree Calculator or Floor Calculator, and calculate planting areas, to store pollen (!_S_PLT_Result_PollenAnnual_Mass).";

        var treeCount = filteredTrees.Count;
        AllergenicText.Text = treeCount > 0 ? summary.AllergenicTreeCount.ToString("N0") : "—";
        AllergenicDetailText.Text = treeCount > 0
            ? $"{100d * summary.AllergenicTreeCount / treeCount:N0}% of {treeCount:N0} trees are species the WWP sheet flags allergenic (!_S_PLT_iTreeSpecies_Allergenic_Text)."
            : "No trees in this scenario.";

        IrrigationText.Text = summary.MeanIrrigationDemandFactor is { } plantFactor ? $"{plantFactor:N2} plant factor" : "—";
        IrrigationDetailText.Text = summary.TreesWithIrrigationFactorCount > 0
            ? $"Average of {summary.TreesWithIrrigationFactorCount:N0} trees, 0–1 (lower needs less water); {100d * summary.LowWaterTreeCount / summary.TreesWithIrrigationFactorCount:N0}% are low-water (≤ {DashboardAggregationService.LowWaterIrrigationFactor:0.0})."
            : "Import the sheet's Irrigation_Demand_Plant Factor onto the tree types (Excel Importer) to see it.";

        UpdateBngCards(filteredFloors);
    }

    private void UpdateBngCards(IReadOnlyList<DashboardFloorItem> filteredFloors)
    {
        var bng = DashboardAggregationService.BuildBngSummary(filteredFloors, BngMetricCatalog.Default);

        static string Floors(int count) => $"{count:N0} floor{(count == 1 ? "" : "s")}";

        if (bng.NetChangePercent is { } percent)
        {
            BngNetChangeText.Text = $"{percent:+0.0;-0.0;0.0}%";
            BngNetChangeText.Foreground = Tier(percent, high: BngSummary.StatutoryNetGainPercent, medium: 0d);
            BngNetChangeDetailText.Text =
                $"{bng.NetChangeUnits:+0.00;-0.00;0.00} HU · {(bng.MeetsStatutoryNetGain == true ? "meets" : "below")} the statutory {BngSummary.StatutoryNetGainPercent:0}% gain" +
                (bng.IsComplete ? "." : " — provisional: some floors are stale or not assessed.");
        }
        else
        {
            BngNetChangeText.Text = "—";
            BngNetChangeText.ClearValue(TextBlock.ForegroundProperty);
            BngNetChangeDetailText.Text = bng.CurrentFloorCount > 0
                ? "No baseline yet — assess the Existing-phase floors in Floor Calculator's BNG tab."
                : "No current BNG results — calculate floors in Floor Calculator's BNG tab.";
        }

        var baselineFloors = bng.RetainedFloorCount + bng.EnhancedFloorCount + bng.LostFloorCount;
        BngBaselineText.Text = baselineFloors > 0 ? $"{bng.BaselineUnits:N2} HU" : "—";
        BngBaselineDetailText.Text = baselineFloors > 0
            ? $"{Floors(bng.RetainedFloorCount)} retained, {bng.EnhancedFloorCount:N0} enhanced, {bng.LostFloorCount:N0} lost ({bng.LostUnits:N2} HU lost). {bng.MetricVersion}"
            : "No assessed Existing-phase floors in the current filter.";

        BngPostText.Text = bng.CurrentFloorCount > 0 ? $"{bng.PostInterventionUnits:N2} HU" : "—";
        BngPostDetailText.Text = bng.CurrentFloorCount > 0
            ? $"Retained {bng.RetainedUnits:N2} + enhanced {bng.EnhancedUnits:N2} + created {bng.CreatedUnits:N2} ({Floors(bng.CreatedFloorCount)} new)."
            : "Retained + enhanced + created habitat units.";

        var attention = bng.StaleFloorCount + bng.CheckDataFloorCount + bng.NotAssessedFloorCount;
        BngAttentionText.Text = attention.ToString("N0");
        BngAttentionText.Foreground = (Brush)Application.Current.Resources[
            attention == 0 ? "LimSuccessBrush" : "LimWarningBrush"];
        BngAttentionDetailText.Text =
            $"{bng.StaleFloorCount:N0} stale (inputs, area or phases changed — excluded), {bng.CheckDataFloorCount:N0} flagged Check data, {bng.NotAssessedFloorCount:N0} not assessed" +
            (bng.ExcludedFloorCount > 0 ? $"; {bng.ExcludedFloorCount:N0} excluded (demolished in the phase they were created)." : ".");

        BngHabitatRows.Clear();
        var postTotal = bng.ByBroadHabitat.Sum(group => group.HabitatUnits);
        foreach (var group in bng.ByBroadHabitat)
        {
            BngHabitatRows.Add(new KpiBarRow(
                group.Name,
                $"{group.HabitatUnits:N2} HU",
                postTotal <= 0d ? 0d : 100d * group.HabitatUnits / postTotal,
                $"{Floors(group.FloorCount)} · {group.AreaHectares:N3} ha"));
        }

        BngHabitatEmptyText.Visibility = BngHabitatRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BngHabitatEmptyText.Text = "No current BNG results in the current filter.";
    }

    private static void SetPercentCard(TextBlock valueText, TextBlock detailText, double? percent, string detailWithValue, string detailWithoutValue, double high, double medium)
    {
        if (percent is { } value)
        {
            valueText.Text = $"{value:N0}%";
            valueText.Foreground = Tier(value, high, medium);
            detailText.Text = detailWithValue;
        }
        else
        {
            valueText.Text = "—";
            valueText.ClearValue(TextBlock.ForegroundProperty);
            detailText.Text = detailWithoutValue;
        }
    }

    private static Brush Tier(double value, double high, double medium) => (Brush)Application.Current.Resources[value switch
    {
        _ when value >= high => "LimSuccessBrush",
        _ when value >= medium => "LimWarningBrush",
        _ => "LimErrorBrush"
    }];

    /// <summary>
    /// Draws a donut ring by stacking one stroked <see cref="Ellipse"/> per segment on <paramref name="host"/>,
    /// each dashed to show only its own arc length (dash units are multiples of stroke thickness — WinUI's
    /// stroke-dasharray convention) and rotated to start where the previous segment ended, beginning at 12
    /// o'clock. A ~2px gap is carved out of each arc so adjacent fills don't visually merge, per the dataviz
    /// mark spec. Rebuilt from scratch on every refresh since segment shares change with the data.
    /// </summary>
    private static void BuildDonutRing(Panel host, double diameter, double thickness, IReadOnlyList<(double Value, Brush Brush)> segments, Brush trackBrush)
    {
        host.Children.Clear();

        host.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = diameter,
            Height = diameter,
            Stroke = trackBrush,
            StrokeThickness = thickness
        });

        var total = segments.Sum(segment => segment.Value);
        if (total <= 0d)
        {
            return;
        }

        var radius = (diameter - thickness) / 2d;
        var circumference = 2d * Math.PI * radius;
        var cumulativeFraction = 0d;

        foreach (var (value, brush) in segments)
        {
            if (value <= 0d)
            {
                continue;
            }

            var fraction = value / total;
            var arcLength = circumference * fraction;
            var gap = Math.Min(arcLength * 0.06, 3d);
            var visibleLength = Math.Max(arcLength - gap, 0d);

            host.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = diameter,
                Height = diameter,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = [visibleLength / thickness, (circumference - visibleLength) / thickness],
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = cumulativeFraction * 360d - 90d }
            });

            cumulativeFraction += fraction;
        }
    }

    /// <summary>
    /// One bar per metric with every outlook year layered on it, each a shade of the metric's colour:
    /// the longest outlook is the full, lightest bar and shorter ones sit on top, darker, so the bar
    /// reads as the total growing from year 5 to year 25. A legend under each bar gives the values.
    /// </summary>
    private void BuildOutlookBars(IReadOnlyList<(string Title, Brush Brush, string Unit, IReadOnlyList<(int Year, double Value)> Points)> metrics)
    {
        OutlookBarsHost.Children.Clear();
        double[] opacities = [1.0, 0.62, 0.36, 0.2];
        var track = (Brush)Resources["KpiChartTrackBrush"];
        foreach (var (title, brush, unit, points) in metrics)
        {
            var color = (brush as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Gray;
            var ordered = points.OrderBy(point => point.Year).ToList();
            var maximum = ordered.Count == 0 ? 0d : ordered.Max(point => point.Value);
            Brush Shade(int index) => new SolidColorBrush(color) { Opacity = opacities[Math.Min(index, opacities.Length - 1)] };

            var section = new StackPanel { Spacing = 6 };
            section.Children.Add(new TextBlock { Text = title, Style = (Style)Resources["KpiEyebrowStyle"], Foreground = brush });

            var bar = new Grid { Height = 16 };
            bar.Children.Add(new Border { Background = track, CornerRadius = new CornerRadius(8) });
            // Longest first, so each shorter (darker) layer is drawn over it.
            for (var index = ordered.Count - 1; index >= 0; index--)
            {
                var share = maximum <= 0d ? 0d : Math.Clamp(ordered[index].Value / maximum, 0d, 1d);
                var layer = new Grid();
                layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
                layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d - share, GridUnitType.Star) });
                layer.Children.Add(new Border { Background = Shade(index), CornerRadius = new CornerRadius(8) });
                ToolTipService.SetToolTip(layer, $"{ordered[index].Year} years: {Compact(ordered[index].Value)} {unit}");
                bar.Children.Add(layer);
            }

            section.Children.Add(bar);

            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var index = 0; index < ordered.Count; index++)
            {
                var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                entry.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = Shade(index), VerticalAlignment = VerticalAlignment.Center });
                entry.Children.Add(new TextBlock { Text = $"{ordered[index].Year} yr", FontSize = 12, Foreground = (Brush)Resources["KpiMutedBrush"] });
                entry.Children.Add(new TextBlock { Text = $"{Compact(ordered[index].Value)} {unit}", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                legend.Children.Add(entry);
            }

            section.Children.Add(legend);
            OutlookBarsHost.Children.Add(section);
        }
    }

    private static string Compact(double value)
    {
        var absolute = Math.Abs(value);
        return absolute switch
        {
            >= 1_000_000_000d => $"{value / 1_000_000_000d:N1}B",
            >= 1_000_000d => $"{value / 1_000_000d:N1}M",
            >= 1_000d => $"{value / 1_000d:N1}K",
            _ => value.ToString("N1")
        };
    }

    private void UpdateStatusCounts(IReadOnlyList<NormalizedTreeMetrics> treesInScope)
    {
        int Count(string status) => treesInScope.Count(tree => tree.Source.Status == status);
        CalculatedCountText.Text = Count("Calculated").ToString("N0");
        ReadyCountText.Text = Count("Ready").ToString("N0");
        StaleCountText.Text = Count("Stale").ToString("N0");
        MissingCountText.Text = Count("MissingInput").ToString("N0");
        InvalidCountText.Text = Count("InvalidInput").ToString("N0");
        WarningCountText.Text = Count("APIWarning").ToString("N0");
        ErrorCountText.Text = Count("APIError").ToString("N0");
        EstimatedCountText.Text = Count(WwpSheetTreeEstimates.EstimatedStatus).ToString("N0");

        StatusBreakdownRows.Clear();
        foreach (var (status, label) in new[]
                 {
                     ("Calculated", "Calculated"),
                     ("Ready", "Ready"),
                     ("Stale", "Stale"),
                     ("MissingInput", "Missing input"),
                     ("InvalidInput", "Invalid input"),
                     ("APIWarning", "API warning"),
                     ("APIError", "API error"),
                     (WwpSheetTreeEstimates.EstimatedStatus, "Estimated (WWP sheet)")
                 })
        {
            var count = Count(status);
            var percent = treesInScope.Count == 0 ? 0d : 100d * count / treesInScope.Count;
            StatusBreakdownRows.Add(new KpiBarRow(label, $"{count:N0} · {percent:N0}%", percent));
        }

        foreach (var card in new[] { CalculatedCard, ReadyCard, StaleCard, MissingCard, InvalidCard, WarningCard, ErrorCard, EstimatedCard })
        {
            var isActive = _activeStatusFilter is not null && (string)card.Tag == _activeStatusFilter;
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
    }

    private void StatusCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var status = (string)((FrameworkElement)sender).Tag;
        _activeStatusFilter = _activeStatusFilter == status ? null : status;
        RebuildRows();
    }

    private void RebuildGrowthYearSlider()
    {
        var worksetYears = _lastReport is null ? [] : GrowthYears.InModel(_lastReport.Trees);
        _growthYearsFromWorksets = worksetYears.Count > 0;
        _growthYearStops = _growthYearsFromWorksets ? worksetYears : GrowthYears.StandardYears;
        if (!_growthYearStops.Contains(_projectionYears))
        {
            _projectionYears = _growthYearStops.Contains(10) ? 10 : _growthYearStops[0];
        }

        GrowthYearHeaderText.Text = _growthYearsFromWorksets ? "Growth year (worksets)" : "Outlook";

        // Labels sit under their stops: stop i is i/(n-1) along the track, so with 2(n-1) equal
        // columns each inner label spans the two columns either side of it.
        var count = _growthYearStops.Count;
        GrowthYearTicks.Children.Clear();
        GrowthYearTicks.ColumnDefinitions.Clear();
        for (var column = 0; column < Math.Max(1, 2 * (count - 1)); column++)
        {
            GrowthYearTicks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var index = 0; index < count; index++)
        {
            var stop = index;
            var label = new TextBlock
            {
                Text = $"{_growthYearStops[index]} yr",
                FontSize = 11,
                Opacity = 0.75,
                HorizontalAlignment = index == 0 ? HorizontalAlignment.Left : index == count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center
            };
            label.Tapped += (_, _) => GrowthYearSlider.Value = stop;
            Grid.SetColumn(label, index == 0 ? 0 : 2 * index - 1);
            Grid.SetColumnSpan(label, index == 0 || index == count - 1 ? 1 : 2);
            GrowthYearTicks.Children.Add(label);
        }

        GrowthYearSlider.Maximum = Math.Max(0, count - 1);
        GrowthYearSlider.IsEnabled = count > 1;
        SetGrowthYearSliderValue();
    }

    /// <summary>Moves the thumb to <see cref="_projectionYears"/> and bolds its label, without re-filtering.</summary>
    private void SetGrowthYearSliderValue()
    {
        var index = Math.Max(0, _growthYearStops.ToList().IndexOf(_projectionYears));
        var wasSuppressed = _suppressFilterEvents;
        _suppressFilterEvents = true;
        try
        {
            GrowthYearSlider.Value = index;
        }
        finally
        {
            _suppressFilterEvents = wasSuppressed;
        }

        for (var i = 0; i < GrowthYearTicks.Children.Count; i++)
        {
            if (GrowthYearTicks.Children[i] is TextBlock label)
            {
                label.FontWeight = i == index ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                label.Opacity = i == index ? 1 : 0.75;
            }
        }
    }

    private void LevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || LevelBox.SelectedItem is not string value)
        {
            return;
        }

        _selectedLevel = value;
        RebuildRows();
    }

    private void SpeciesSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildRows();

    private void UnitSystemBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || UnitSystemBox.SelectedItem is not ComboBoxItem { Content: string value })
        {
            return;
        }

        _unitSystemOverride = value;
        RebuildRows();
    }

    private void GrowthYearSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var index = (int)Math.Round(e.NewValue);
        if (_suppressFilterEvents || index < 0 || index >= _growthYearStops.Count || _growthYearStops[index] == _projectionYears)
        {
            return;
        }

        _projectionYears = _growthYearStops[index];
        SetGrowthYearSliderValue();
        RebuildRows();
    }

    private void PeriodToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _showAnnual = PeriodToggle.IsOn;
        RebuildRows();
    }

    private void RebuildSiteBoundaryOptions()
    {
        if (_lastReport is null)
        {
            return;
        }

        // The project's saved boundary wins over a default picked while a cached snapshot was showing;
        // only a boundary picked by hand this session overrides it.
        var options = SiteBoundaryResolver.BuildOptions(_lastReport.SiteTotalAreaSquareMeters, _lastReport.PropertyLines);
        var chosen = SiteBoundaryResolver.Resolve(options, _siteBoundaryPicked ? _siteBoundary?.Key : _savedSiteBoundaryKey ?? _siteBoundary?.Key);
        _siteBoundary = chosen;

        _suppressFilterEvents = true;
        try
        {
            SiteBoundaryBox.ItemsSource = options;
            SiteBoundaryBox.SelectedItem = options.First(option => option.Key == chosen.Key);
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private async void SiteBoundaryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ComboBox can re-raise SelectionChanged for the item already shown (e.g. when its Pivot page
        // reloads); that's not a choice, so it mustn't overwrite the project's setting.
        if (_suppressFilterEvents || SiteBoundaryBox.SelectedItem is not SiteBoundaryOption option || option.Key == _siteBoundary?.Key)
        {
            return;
        }

        _siteBoundary = option;
        _siteBoundaryPicked = true;
        _savedSiteBoundaryKey = option.Key;
        RebuildRows();

        // Remembered on the project, so everyone opening the Dashboard sees the same site boundary.
        // Best effort: a cached snapshot with Revit closed just keeps the choice for this session.
        try
        {
            await ProjectSettingsSync.PushAsync(GetClient(), siteBoundary: option.Key);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private async Task<ProjectSettingsSnapshot?> TryPullProjectSettingsAsync()
    {
        try
        {
            return await ProjectSettingsSync.PullAsync(GetClient());
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The whole model goes into the export — every design option, and with growth-year worksets
    /// every option x year (see DashboardJsonExportService.Build) — with the chosen site area.
    /// </summary>
    private DashboardReportResult ReportForJsonExport() => ReportWithChosenSiteArea(_lastReport!);

    /// <summary>Lighting under the current design option and level filters, by the same main-model rule as trees and floors.</summary>
    private IReadOnlyList<DashboardLightingItem> FilteredLighting() =>
        DesignScenarios.Filter(_lastReport?.Lighting ?? [], fixture => fixture.DesignOption, _scenario, _sets)
            .Where(fixture => _selectedLevel == AllLevels || (fixture.LevelName ?? "—") == _selectedLevel)
            .ToList();

    /// <summary>The report as the KPIs see it: its site area replaced by the chosen site boundary's.</summary>
    private DashboardReportResult ReportWithChosenSiteArea(DashboardReportResult report) =>
        report with { SiteTotalAreaSquareMeters = _siteBoundary?.AreaSquareMeters };

    private void ShowOriginalData_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard or load a cached snapshot first.";
            return;
        }

        if (_originalDataWindow is null)
        {
            _originalDataWindow = new OriginalDataWindow(_lastReport, _revitClient);
            _originalDataWindow.Closed += (_, _) => _originalDataWindow = null;
        }

        _originalDataWindow.Activate();
    }

    private async void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard before exporting.";
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Landscape Dashboard {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("Excel workbook", [".xlsx"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var unitSystem = EffectiveUnitSystem();
            var normalizedTrees = TreeRows.Select(row => row.Metrics).ToList();
            var floors = FloorRows.Select(row => row.Item).ToList();
            var grandTotal = DashboardAggregationService.BuildGrandTotal(normalizedTrees, floors);
            var request = new DashboardExcelExportRequest(
                file.Path,
                _lastReport.DocumentTitle,
                unitSystem,
                _preferredCurrency,
                _showAnnual ? "Annual" : "Lifetime Total",
                grandTotal,
                DashboardAggregationService.AggregateTreesBySpecies(normalizedTrees),
                DashboardAggregationService.AggregateFloorsByType(floors),
                normalizedTrees,
                floors);

            var result = await DashboardExcelExportService.ExportAsync(request);
            StatusText.Text = $"Exported dashboard workbook to {Path.GetFileName(result.FilePath)}.";
        });
    }

    private async void ExportImage_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard or load a cached snapshot before exporting.";
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"Landscape Dashboard {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            DashboardPages.SelectedIndex = 0;
            await Task.Delay(100);
            var renderTarget = new RenderTargetBitmap();
            await renderTarget.RenderAsync(CaptureRoot);
            var pixels = await renderTarget.GetPixelsAsync();

            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            stream.Size = 0;
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)renderTarget.PixelWidth,
                (uint)renderTarget.PixelHeight,
                96, 96,
                pixels.ToArray());
            await encoder.FlushAsync();

            StatusText.Text = $"Exported dashboard image to {file.Name}.";
        });
    }

    private async void ExportSvg_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard or load a cached snapshot before exporting.";
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"Environmental KPI Dashboard {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("SVG vector image", [".svg"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var snapshot = new DashboardSvgSnapshot(
                _lastReport.DocumentTitle,
                DesignScenarios.Label(_scenario, _sets),
                $"{_projectionYears}-year outlook",
                KpiEcosystemValueText.Text,
                HeroCarbonCostText.Text,
                HeroStormWaterCostText.Text,
                HeroPollutionTotalText.Text,
                SnapshotAreaText.Text,
                ReadinessPercentText.Text,
                ProjectionTrendRows.ToList(),
                SpeciesMixRows.ToList(),
                DateTimeOffset.Now);
            await DashboardSvgExportService.ExportAsync(file.Path, snapshot);
            StatusText.Text = $"Exported vector dashboard to {file.Name}.";
        });
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard or load a cached snapshot before exporting.";
            return;
        }

        // The snapshot cache isn't tied to a project, so after a failed refresh this could be the
        // last project's data. Say so before writing a file meant to be shared.
        if (_cachedSnapshotSavedAt is { } savedAt)
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Export a cached snapshot?",
                Content = $"This is a cached snapshot of \"{_lastReport.DocumentTitle}\" from {savedAt.LocalDateTime:g}, not live Revit data. " +
                          "Open the model in Revit and select Refresh model first if you need current numbers.",
                PrimaryButtonText = "Export snapshot",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Landscape Dashboard {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("JSON data", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var location = await ResolveLocationAsync();
            var export = DashboardJsonExportService.Build(ReportForJsonExport(), _preferredCurrency, _usdExchangeRate, location, _sets, OfferedScenarios());
            await DashboardJsonExportService.ExportAsync(file.Path, export);
            StatusText.Text = $"Exported dashboard data to {file.Name}.";
        });
    }

    /// <summary>Best-effort — the pipe may be unavailable (e.g. viewing a cached snapshot with Revit closed), in which case the export simply ships without a location.</summary>
    private async Task<DashboardJsonLocation> ResolveLocationAsync()
    {
        if (_revitClient is null)
        {
            return new DashboardJsonLocation(null, null, null);
        }

        try
        {
            var result = await _revitClient.SendAsync<ProjectSiteLocationResult>(PipeCommands.GetProjectSiteLocation);
            return new DashboardJsonLocation(result.Latitude, result.Longitude, result.PlaceName);
        }
        catch (IOException)
        {
            return new DashboardJsonLocation(null, null, null);
        }
        catch (TimeoutException)
        {
            return new DashboardJsonLocation(null, null, null);
        }
        catch (OperationCanceledException)
        {
            // RevitPipeClient's connect timeout surfaces as a cancellation when Revit isn't running.
            return new DashboardJsonLocation(null, null, null);
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
        _originalDataWindow?.Close();
        if (_revitClient is not null)
        {
            await _revitClient.DisposeAsync();
        }
    }
}
