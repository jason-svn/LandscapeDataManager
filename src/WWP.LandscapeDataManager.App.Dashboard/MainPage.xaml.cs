using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
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

namespace WWP.LandscapeDataManager.App.Dashboard;

public sealed partial class MainPage : Page
{
    private const string AllDesignOptions = "All design options";
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
    private string? _activeStatusFilter;
    private string _selectedDesignOption = AllDesignOptions;
    private string _selectedLevel = AllLevels;
    private bool _suppressFilterEvents;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<SpeciesSubtotalRow> SpeciesSubtotals { get; } = [];
    public ObservableCollection<FloorSubtotalRow> FloorSubtotals { get; } = [];
    public ObservableCollection<DashboardTreeRow> TreeRows { get; } = [];
    public ObservableCollection<DashboardFloorRow> FloorRows { get; } = [];
    public ObservableCollection<KpiBarRow> ProjectionTrendRows { get; } = [];
    public ObservableCollection<KpiBarRow> ImpactProfileRows { get; } = [];
    public ObservableCollection<KpiBarRow> SpeciesMixRows { get; } = [];
    public ObservableCollection<KpiBarRow> FloorMixRows { get; } = [];
    public ObservableCollection<KpiBarRow> StatusBreakdownRows { get; } = [];
    public ObservableCollection<ScenarioComparisonRow> ScenarioRows { get; } = [];

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
            await ApplyReportAsync(cached.Report);
            CacheStatusText.Text = $"Cached {cached.SavedAt.LocalDateTime:g}";
            StatusText.Text = $"Showing cached snapshot from {cached.SavedAt.LocalDateTime:g}. Select Refresh model for current Revit data.";
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

            await ApplyReportAsync(cached.Report);
            CacheStatusText.Text = $"Cached {cached.SavedAt.LocalDateTime:g}";
            StatusText.Text = $"Revit refresh failed ({liveException.Message}). Showing cached snapshot from {cached.SavedAt.LocalDateTime:g}.";
        }
    }

    private async Task ApplyReportAsync(DashboardReportResult report)
    {
        _lastReport = report;
        _preferredUnitSystem = report.PreferredUnitSystem;
        _preferredCurrency = report.PreferredCurrency;
        CurrencyText.Text = _preferredCurrency;
        ProjectTitleText.Text = report.DocumentTitle;

        // One rate lookup per report, not per tree — the rate doesn't vary by tree, only by currency.
        var rate = await _exchangeRateService.GetUsdRateAsync(_preferredCurrency);
        _usdExchangeRate = rate.UsdRate;

        RebuildFilterOptions();
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
            var designOptions = _lastReport.Trees.Select(tree => tree.DesignOption)
                .Concat(_lastReport.Floors.Select(floor => floor.DesignOption))
                .Select(DashboardUnitLabels.FormatDesignOption)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            DesignOptionBox.Items.Clear();
            DesignOptionBox.Items.Add(AllDesignOptions);
            foreach (var option in designOptions)
            {
                DesignOptionBox.Items.Add(option);
            }

            DesignOptionBox.SelectedItem = DesignOptionBox.Items.Contains(_selectedDesignOption)
                ? _selectedDesignOption
                : AllDesignOptions;

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
            (_selectedDesignOption == AllDesignOptions || DashboardUnitLabels.FormatDesignOption(designOption) == _selectedDesignOption) &&
            MatchesLevel(levelName);

        var treesInScope = normalizedTrees
            .Where(tree => MatchesDimensionFilters(tree.Source.DesignOption, tree.Source.LevelName))
            .Where(MatchesSearch)
            .ToList();

        UpdateStatusCounts(treesInScope);

        var filteredTrees = treesInScope
            .Where(tree => _activeStatusFilter is null || tree.Source.Status == _activeStatusFilter)
            .ToList();
        var filteredFloors = _lastReport.Floors
            .Where(floor => MatchesDimensionFilters(floor.DesignOption, floor.LevelName))
            .ToList();

        var speciesSubtotals = DashboardAggregationService.AggregateTreesBySpecies(filteredTrees);
        SpeciesSubtotals.Clear();
        foreach (var subtotal in speciesSubtotals)
        {
            SpeciesSubtotals.Add(new SpeciesSubtotalRow(subtotal, unitSystem, _preferredCurrency, _showAnnual));
        }

        var floorSubtotals = DashboardAggregationService.AggregateFloorsByType(filteredFloors);
        FloorSubtotals.Clear();
        foreach (var subtotal in floorSubtotals)
        {
            FloorSubtotals.Add(new FloorSubtotalRow(subtotal));
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
        var co2Unit = DashboardUnitLabels.Co2Unit(unitSystem);
        var pollutantUnit = DashboardUnitLabels.PollutantUnit(unitSystem);
        var period = _showAnnual ? "Annual" : "Lifetime";

        TreeCountText.Text = grandTotal.TreeCount.ToString("N0");
        TreeExcludedText.Text = grandTotal.TreesExcludedFromTotals > 0
            ? $"{grandTotal.TreesExcludedFromTotals:N0} excluded (not yet Calculated)"
            : string.Empty;
        PollutionMassText.Text = _showAnnual
            ? $"{grandTotal.TotalPollutionMassRemovedAnnual:N2} {pollutantUnit}"
            : $"{grandTotal.TotalPollutionMassRemovedLifetimeTotal:N2} {pollutantUnit}";
        CO2SequesteredText.Text = _showAnnual
            ? $"{grandTotal.CO2SequesteredAnnual:N1} {co2Unit}"
            : $"{grandTotal.CO2SequesteredLifetimeTotal:N1} {co2Unit}";
        TreeCostSavedText.Text = _showAnnual
            ? $"{grandTotal.TreeCostSavedAnnual:N2} {_preferredCurrency}"
            : $"{grandTotal.TreeCostSavedLifetimeTotal:N2} {_preferredCurrency}";

        FloorCountText.Text = grandTotal.FloorCount.ToString("N0");
        FloorAreaText.Text = $"{grandTotal.FloorAreaSquareMeters:N1} m²";
        FloorCO2Text.Text = $"{grandTotal.FloorCO2SequesteredAnnual:N1} kg";
        FloorRunoffText.Text = $"{grandTotal.FloorRunoffAvoidedAnnual:N1} m³";
        FloorPollutionText.Text = $"{grandTotal.FloorPollutionMassRemovedAnnual:N2} kg";
        FloorGwpText.Text = grandTotal.FloorTotalGwp.ToString("N1");
        FloorCostSavedText.Text = $"{grandTotal.FloorCostSavedAnnual:N2} (as calculated)";

        PeriodLabelText.Text = $"Showing {period} benefits.";

        RebuildGraphicDashboard(
            normalizedTrees,
            filteredTrees,
            filteredFloors,
            speciesSubtotals,
            floorSubtotals,
            grandTotal,
            unitSystem,
            MatchesLevel,
            MatchesSearch);
    }

    private void RebuildGraphicDashboard(
        IReadOnlyList<NormalizedTreeMetrics> allNormalizedTrees,
        IReadOnlyList<NormalizedTreeMetrics> filteredTrees,
        IReadOnlyList<DashboardFloorItem> filteredFloors,
        IReadOnlyList<SpeciesSubtotal> speciesSubtotals,
        IReadOnlyList<FloorTypeSubtotal> floorSubtotals,
        DashboardGrandTotal grandTotal,
        string unitSystem,
        Func<string?, bool> matchesLevel,
        Func<NormalizedTreeMetrics, bool> matchesSearch)
    {
        var metric = string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        var co2Unit = DashboardUnitLabels.Co2Unit(unitSystem);
        var pollutantUnit = DashboardUnitLabels.PollutantUnit(unitSystem);
        var waterUnit = metric ? "m³" : "gal";
        var areaUnit = metric ? "m²" : "ft²";

        double FloorCo2(double kilograms) => metric ? kilograms : kilograms / UnitConversions.KilogramsPerPound;
        double FloorPollution(double kilograms) => metric ? kilograms : kilograms / UnitConversions.KilogramsPerOunce;
        double Water(double cubicMetres) => metric ? cubicMetres : cubicMetres / UnitConversions.CubicMetresPerGallon;
        double Area(double squareMetres) => metric ? squareMetres : squareMetres * 10.7639104167d;

        var countedTrees = filteredTrees.Where(tree => tree.CountsTowardTotals).ToList();
        var treeRunoffAnnual = countedTrees.Sum(tree => tree.RunoffAvoidedAnnual);
        var floorCo2Annual = FloorCo2(grandTotal.FloorCO2SequesteredAnnual);
        var floorPollutionAnnual = FloorPollution(grandTotal.FloorPollutionMassRemovedAnnual);
        var floorRunoffAnnual = Water(grandTotal.FloorRunoffAvoidedAnnual);
        var treeRunoffDisplay = Water(treeRunoffAnnual);
        var projectedCarbon = (grandTotal.CO2SequesteredAnnual + floorCo2Annual) * _projectionYears;
        var projectedPollution = (grandTotal.TotalPollutionMassRemovedAnnual + floorPollutionAnnual) * _projectionYears;
        var projectedWater = (treeRunoffDisplay + floorRunoffAnnual) * _projectionYears;
        var projectedValue = grandTotal.TreeCostSavedAnnual * _projectionYears;

        var readyPercent = filteredTrees.Count == 0 ? 0d : 100d * grandTotal.TreeCount / filteredTrees.Count;
        var scenarioLabel = _selectedDesignOption == AllDesignOptions ? "All design options" : _selectedDesignOption;
        ScenarioBadgeText.Text = scenarioLabel.ToUpperInvariant();
        OutlookText.Text = $"{_projectionYears}-year outlook · current annual stored results × {_projectionYears}";
        KpiEcosystemValueText.Text = $"{Compact(projectedValue)} {_preferredCurrency}";
        KpiCarbonText.Text = $"{Compact(projectedCarbon)} {co2Unit}";
        KpiWaterText.Text = $"{Compact(projectedWater)} {waterUnit}";
        KpiPollutionText.Text = $"{Compact(projectedPollution)} {pollutantUnit}";
        KpiTreeCountText.Text = grandTotal.TreeCount.ToString("N0");
        KpiPlantingAreaText.Text = grandTotal.FloorCount.ToString("N0");
        SnapshotAreaText.Text = $"{Compact(Area(grandTotal.FloorAreaSquareMeters))} {areaUnit}";
        GwpBaselineText.Text = Compact(grandTotal.FloorTotalGwp);
        SnapshotInventoryText.Text = $"{grandTotal.TreeCount + grandTotal.FloorCount:N0} assets";
        ReportingProgress.Value = readyPercent;
        ReportingCaptionText.Text = $"{readyPercent:N0}% tree results ready";
        ReadinessRing.Value = readyPercent;
        ReadinessPercentText.Text = $"{readyPercent:N0}%";
        ReadinessDetailText.Text = $"{grandTotal.TreeCount:N0} of {filteredTrees.Count:N0} filtered tree records are Calculated.";

        ProjectionTrendRows.Clear();
        foreach (var years in new[] { 5, 10, 20, 25 })
        {
            var value = (grandTotal.CO2SequesteredAnnual + floorCo2Annual) * years;
            ProjectionTrendRows.Add(new KpiBarRow($"{years} yr", $"{Compact(value)} {co2Unit}", 100d * years / 25d));
        }

        ImpactProfileRows.Clear();
        AddImpactShare("Carbon", grandTotal.CO2SequesteredAnnual, floorCo2Annual, co2Unit);
        AddImpactShare("Pollution", grandTotal.TotalPollutionMassRemovedAnnual, floorPollutionAnnual, pollutantUnit);
        AddImpactShare("Runoff", treeRunoffDisplay, floorRunoffAnnual, waterUnit);

        SpeciesMixRows.Clear();
        var totalTrees = Math.Max(1, speciesSubtotals.Sum(row => row.TreeCount));
        foreach (var row in speciesSubtotals.OrderByDescending(row => row.TreeCount).Take(5))
        {
            SpeciesMixRows.Add(new KpiBarRow(
                row.SpeciesCode,
                row.TreeCount.ToString("N0"),
                100d * row.TreeCount / totalTrees,
                row.CommonName));
        }

        FloorMixRows.Clear();
        var totalArea = floorSubtotals.Sum(row => row.AreaSquareMeters);
        foreach (var row in floorSubtotals.OrderByDescending(row => row.AreaSquareMeters).Take(5))
        {
            var displayArea = Area(row.AreaSquareMeters);
            FloorMixRows.Add(new KpiBarRow(
                row.LdsType,
                $"{Compact(displayArea)} {areaUnit}",
                totalArea <= 0d ? 0d : 100d * row.AreaSquareMeters / totalArea));
        }

        RebuildScenarioRows(allNormalizedTrees, unitSystem, matchesLevel, matchesSearch);
        UpdateSiteKpiCards(filteredTrees, filteredFloors);

        void AddImpactShare(string label, double treeValue, double floorValue, string valueUnit)
        {
            var total = treeValue + floorValue;
            var treePercent = total <= 0d ? 0d : 100d * treeValue / total;
            ImpactProfileRows.Add(new KpiBarRow(
                label,
                $"{treePercent:N0}% trees",
                treePercent,
                $"{Compact(total * _projectionYears)} {valueUnit} over {_projectionYears} years"));
        }
    }

    private void RebuildScenarioRows(
        IReadOnlyList<NormalizedTreeMetrics> allTrees,
        string unitSystem,
        Func<string?, bool> matchesLevel,
        Func<NormalizedTreeMetrics, bool> matchesSearch)
    {
        if (_lastReport is null)
        {
            return;
        }

        var metric = string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        var co2Unit = DashboardUnitLabels.Co2Unit(unitSystem);
        double FloorCo2(double kilograms) => metric ? kilograms : kilograms / UnitConversions.KilogramsPerPound;

        var treeGroups = allTrees
            .Where(tree => matchesLevel(tree.Source.LevelName) && matchesSearch(tree))
            .GroupBy(tree => DashboardUnitLabels.FormatDesignOption(tree.Source.DesignOption))
            .ToDictionary(group => group.Key, group => group.ToList());
        var floorGroups = _lastReport.Floors
            .Where(floor => matchesLevel(floor.LevelName))
            .GroupBy(floor => DashboardUnitLabels.FormatDesignOption(floor.DesignOption))
            .ToDictionary(group => group.Key, group => group.ToList());
        var optionNames = treeGroups.Keys.Concat(floorGroups.Keys).Distinct().OrderBy(ExtractProjectionYears).ThenBy(name => name).ToList();

        var values = optionNames.Select(option =>
        {
            var trees = treeGroups.GetValueOrDefault(option) ?? [];
            var floors = floorGroups.GetValueOrDefault(option) ?? [];
            var total = DashboardAggregationService.BuildGrandTotal(trees, floors);
            var years = ExtractProjectionYears(option) ?? _projectionYears;
            var carbon = (total.CO2SequesteredAnnual + FloorCo2(total.FloorCO2SequesteredAnnual)) * years;
            var speciesCount = trees
                .Select(tree => tree.Source.SpeciesCode ?? tree.Source.CommonName ?? "Unassigned")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return (Option: option, Total: total, Years: years, Carbon: carbon, SpeciesCount: speciesCount);
        }).ToList();
        var maximum = values.Count == 0 ? 0d : values.Max(value => value.Carbon);

        // Biodiversity Added's "% species added" needs a baseline to compare against — the option
        // flagged (Primary) if one exists (the model's "existing" design), otherwise the first option
        // in projection-year order.
        var baselineSpeciesCount = values
            .FirstOrDefault(value => value.Option.Contains("(Primary)", StringComparison.Ordinal)).SpeciesCount;
        if (baselineSpeciesCount == 0 && values.Count > 0)
        {
            baselineSpeciesCount = values[0].SpeciesCount;
        }

        ScenarioRows.Clear();
        foreach (var value in values)
        {
            var speciesCountText = baselineSpeciesCount > 0 && value.SpeciesCount != baselineSpeciesCount
                ? $"{value.SpeciesCount:N0} species ({100d * (value.SpeciesCount - baselineSpeciesCount) / baselineSpeciesCount:+0;-0}% vs. baseline)"
                : $"{value.SpeciesCount:N0} species";
            ScenarioRows.Add(new ScenarioComparisonRow(
                value.Option,
                $"{value.Total.TreeCount:N0} trees · {value.Total.FloorCount:N0} areas",
                speciesCountText,
                $"{Compact(value.Carbon)} {co2Unit} / {value.Years} yr",
                maximum <= 0d ? 0d : 100d * value.Carbon / maximum));
        }
    }

    private void UpdateSiteKpiCards(IReadOnlyList<NormalizedTreeMetrics> filteredTrees, IReadOnlyList<DashboardFloorItem> filteredFloors)
    {
        if (_lastReport is null)
        {
            return;
        }

        var summary = DashboardAggregationService.BuildSiteKpiSummary(
            filteredTrees, filteredFloors, _lastReport.Lighting, _lastReport.SiteTotalAreaSquareMeters, _lastReport.HabitatConnectivityScore);

        SetPercentCard(CanopyCoverText, CanopyCoverDetailText, summary.CanopyCoverPercent,
            $"{summary.CanopyAreaSquareMeters:N0} m² canopy over site area",
            "Enter !_S_PLT_Site_TotalArea_Area on Project Information to see a percentage.",
            high: 30d, medium: 15d);

        SetPercentCard(SoftscapeRatioText, SoftscapeRatioDetailText, summary.SoftscapeSurfaceRatioPercent,
            $"{summary.PerviousAreaSquareMeters:N0} m² pervious over site area",
            "Enter !_S_PLT_Site_TotalArea_Area on Project Information to see a percentage.",
            high: 50d, medium: 25d);

        BiodiversityAddedText.Text = $"{summary.DistinctSpeciesCount:N0} species";
        BiodiversityAddedDetailText.Text = "Species richness in the current filter. See Design scenarios for the % added vs. the baseline design option.";

        SetPercentCard(NativeSpeciesRatioText, NativeSpeciesRatioDetailText, summary.NativeSpeciesRatioPercent,
            $"{summary.NativeTreeCount:N0} native : {summary.AdaptiveTreeCount:N0} adaptive of {summary.TreesWithNativeStatusCount:N0} tagged trees",
            "Tag !_S_PLT_iTreeSpecies_NativeStatus_Text on Planting Types to see a ratio.",
            high: 80d, medium: 60d);

        PhenologicalResilienceText.Text = $"{summary.DistinctBloomMonthsCount} months";
        PhenologicalResilienceDetailText.Text = "Distinct calendar months with a bloom/seed source, from !_S_PLT_iTreeSpecies_BloomMonths_Text.";
        PhenologicalResilienceText.Foreground = Tier(summary.DistinctBloomMonthsCount, high: 9d, medium: 6d);

        FunctionalDiversityText.Text = $"{summary.DistinctEcologicalFunctionsCount} function{(summary.DistinctEcologicalFunctionsCount == 1 ? "" : "s")}";
        FunctionalDiversityDetailText.Text = $"{summary.MultiFunctionAreaSquareMeters:N0} m² of canopy from multi-function species.";

        if (summary.HabitatConnectivityScore is { } connectivityScore)
        {
            HabitatConnectivityText.Text = $"{connectivityScore:N1} / 10";
            HabitatConnectivityDetailText.Text = "Entered from external GIS connectivity modelling (site-wide, not per design option).";
        }
        else
        {
            HabitatConnectivityText.Text = "—";
            HabitatConnectivityDetailText.Text = "Enter !_S_PLT_Site_HabitatConnectivityScore_Number on Project Information after running GIS analysis.";
        }

        SetPercentCard(LightingImpactText, LightingImpactDetailText, summary.LightingCompliancePercent,
            $"{summary.DarkSkyCompliantFixtureCount:N0} of {summary.LightingFixtureCount:N0} fixtures compliant",
            "Tag !_S_PLT_Lighting_DarkSkyCompliant_Text on Lighting Fixture Types to see a percentage.",
            high: 80d, medium: 50d);
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

    private static Brush Tier(double value, double high, double medium) => value switch
    {
        _ when value >= high => new SolidColorBrush(Color.FromArgb(0xFF, 0x17, 0x6B, 0x2B)),
        _ when value >= medium => new SolidColorBrush(Color.FromArgb(0xFF, 0xB2, 0x5E, 0x00)),
        _ => new SolidColorBrush(Color.FromArgb(0xFF, 0xC4, 0x26, 0x2E))
    };

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

    private static int? ExtractProjectionYears(string value)
    {
        var match = Regex.Match(value, @"(?<!\d)(5|10|20|25)\s*(?:years?|yrs?)?", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var years) ? years : null;
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

        StatusBreakdownRows.Clear();
        foreach (var (status, label) in new[]
                 {
                     ("Calculated", "Calculated"),
                     ("Ready", "Ready"),
                     ("Stale", "Stale"),
                     ("MissingInput", "Missing input"),
                     ("InvalidInput", "Invalid input"),
                     ("APIWarning", "API warning"),
                     ("APIError", "API error")
                 })
        {
            var count = Count(status);
            var percent = treesInScope.Count == 0 ? 0d : 100d * count / treesInScope.Count;
            StatusBreakdownRows.Add(new KpiBarRow(label, $"{count:N0} · {percent:N0}%", percent));
        }

        foreach (var card in new[] { CalculatedCard, ReadyCard, StaleCard, MissingCard, InvalidCard, WarningCard, ErrorCard })
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

    private void DesignOptionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || DesignOptionBox.SelectedItem is not string value)
        {
            return;
        }

        _selectedDesignOption = value;

        var optionYears = ExtractProjectionYears(value);
        if (optionYears is not null)
        {
            _projectionYears = optionYears.Value;
            _suppressFilterEvents = true;
            try
            {
                ProjectionBox.SelectedItem = ProjectionBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), optionYears.Value.ToString(), StringComparison.Ordinal));
            }
            finally
            {
                _suppressFilterEvents = false;
            }
        }

        RebuildRows();
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

    private void ProjectionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || ProjectionBox.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var years))
        {
            return;
        }

        _projectionYears = years;
        RebuildRows();
    }

    private void PeriodToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _showAnnual = PeriodToggle.IsOn;
        RebuildRows();
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
                _selectedDesignOption == AllDesignOptions ? "All design options" : _selectedDesignOption,
                $"{_projectionYears}-year outlook",
                KpiEcosystemValueText.Text,
                KpiCarbonText.Text,
                KpiWaterText.Text,
                KpiPollutionText.Text,
                SnapshotAreaText.Text,
                ReadinessPercentText.Text,
                ProjectionTrendRows.ToList(),
                SpeciesMixRows.ToList(),
                DateTimeOffset.Now);
            await DashboardSvgExportService.ExportAsync(file.Path, snapshot);
            StatusText.Text = $"Exported vector dashboard to {file.Name}.";
        });
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
