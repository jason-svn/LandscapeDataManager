using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>How tree benefits were added up over an outlook.</summary>
public enum TreeOutlookMethod
{
    /// <summary>No growth modelled: the stored annual figure × years.</summary>
    AnnualTimesYears,

    /// <summary>Growth-year worksets: each year's copies as i-Tree calculated them, years in between interpolated.</summary>
    GrowthWorksets,

    /// <summary>i-Tree's own year-by-year forecast for each tree, summed year by year.</summary>
    Timeline
}

/// <summary>
/// Tree benefits accumulated over an outlook of N years (year 1 + year 2 + … + year N), in the
/// dashboard's display units. Picks the best source the model has:
/// <list type="bullet">
/// <item>i-Tree timelines (see <see cref="TreeBenefitTimeline"/>): each planted tree's forecast,
/// held at its last (mature) year after the horizon. Growth-year copies are left out — the timeline
/// already grows the original. A planted tree without a timeline falls back to annual × N.</item>
/// <item>Growth-year worksets: each year's copies give the benefit in that year (<see cref="GrowthOutlook"/>).</item>
/// <item>Otherwise the stored annual figure × N.</item>
/// </list>
/// Only Calculated trees count, as in <see cref="DashboardAggregationService.BuildGrandTotal"/>.
/// The result is a <see cref="DashboardGrandTotal"/> whose <c>…Annual</c> fields hold the totals
/// over the outlook, so any annual metric selector reads its accumulated value.
/// </summary>
public sealed class TreeOutlook
{
    private readonly IReadOnlyList<NormalizedTreeMetrics> _trees;
    private readonly IReadOnlyList<int> _growthYearStops;
    private readonly IReadOnlyDictionary<string, TreeBenefitTimeline> _timelines;
    private readonly bool _metric;
    private readonly double _usdToTargetRate;
    private readonly Dictionary<int, DashboardGrandTotal> _cache = [];

    public TreeOutlook(
        IReadOnlyList<NormalizedTreeMetrics> treesAllGrowthYears,
        IReadOnlyList<int> growthYearStops,
        IEnumerable<TreeBenefitTimeline>? timelines,
        string unitSystem,
        double usdToTargetRate)
    {
        _trees = treesAllGrowthYears;
        _growthYearStops = growthYearStops;
        _timelines = (timelines ?? []).GroupBy(timeline => timeline.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        _metric = string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        _usdToTargetRate = usdToTargetRate;

        Method = _trees.Any(tree => tree.CountsTowardTotals && IsPlanted(tree.Source) && HasTimeline(tree.Source))
            ? TreeOutlookMethod.Timeline
            : _growthYearStops.Count > 0 && _trees.Any(tree => GrowthYears.Of(tree.Source) is not null)
                ? TreeOutlookMethod.GrowthWorksets
                : TreeOutlookMethod.AnnualTimesYears;
    }

    public TreeOutlookMethod Method { get; }

    /// <summary>Trees forecast by a timeline; the rest of the planted trees fell back to annual × years.</summary>
    public int TreesWithTimeline => Method == TreeOutlookMethod.Timeline
        ? _trees.Count(tree => tree.CountsTowardTotals && IsPlanted(tree.Source) && HasTimeline(tree.Source))
        : 0;

    /// <summary>The accumulated value of <paramref name="annualMetric"/> over <paramref name="years"/>.</summary>
    public double Accumulate(Func<DashboardGrandTotal, double> annualMetric, int years) =>
        Method == TreeOutlookMethod.GrowthWorksets
            ? GrowthOutlook.Accumulate(_growthYearStops.Select(year => (year, annualMetric(TotalIn(year)))), years)
            : annualMetric(Over(years));

    /// <summary>Totals over <paramref name="years"/> (timeline or annual × years); for growth worksets, see <see cref="Accumulate"/>.</summary>
    public DashboardGrandTotal Over(int years)
    {
        if (_cache.TryGetValue(years, out var cached))
        {
            return cached;
        }

        var accumulated = Method switch
        {
            TreeOutlookMethod.Timeline => _trees
                .Where(tree => IsPlanted(tree.Source))
                .Select(tree => HasTimeline(tree.Source) ? FromTimeline(tree, _timelines[tree.Source.TimelineKey!], years) : Scale(tree, years))
                .ToList(),
            _ => _trees.Select(tree => Scale(tree, years)).ToList()
        };
        var total = DashboardAggregationService.BuildGrandTotal(accumulated, []);
        _cache[years] = total;
        return total;
    }

    private DashboardGrandTotal TotalIn(int growthYear) =>
        DashboardAggregationService.BuildGrandTotal(_trees.Where(tree => GrowthYears.CountsIn(tree.Source, growthYear)).ToList(), []);

    /// <summary>A planted tree, not a growth-year copy of one (a copy sits on a growth-year workset after year 0).</summary>
    private static bool IsPlanted(DashboardTreeItem tree) => GrowthYears.Of(tree) is null or 0;

    private bool HasTimeline(DashboardTreeItem tree) =>
        tree.TimelineKey is { } key && _timelines.TryGetValue(key, out var timeline) && timeline.Years.Count > 0;

    private static NormalizedTreeMetrics Scale(NormalizedTreeMetrics tree, int years) => tree with
    {
        CarbonSequesteredAnnual = tree.CarbonSequesteredAnnual * years,
        CORemovedAnnual = tree.CORemovedAnnual * years,
        NO2RemovedAnnual = tree.NO2RemovedAnnual * years,
        O3RemovedAnnual = tree.O3RemovedAnnual * years,
        PM25RemovedAnnual = tree.PM25RemovedAnnual * years,
        SO2RemovedAnnual = tree.SO2RemovedAnnual * years,
        CostSavedAnnual = tree.CostSavedAnnual * years,
        CarbonCostSavedAnnual = tree.CarbonCostSavedAnnual * years,
        StormWaterCostSavedAnnual = tree.StormWaterCostSavedAnnual * years,
        AirPollutionCostSavedAnnual = tree.AirPollutionCostSavedAnnual * years,
        RainfallInterceptedAnnual = tree.RainfallInterceptedAnnual * years,
        RunoffAvoidedAnnual = tree.RunoffAvoidedAnnual * years,
        CO2EquivalentAnnual = tree.CO2EquivalentAnnual * years,
        OxygenProducedAnnual = tree.OxygenProducedAnnual * years,
        MaintenanceCostAnnual = tree.MaintenanceCostAnnual * years
    };

    private NormalizedTreeMetrics FromTimeline(NormalizedTreeMetrics tree, TreeBenefitTimeline timeline, int years)
    {
        var sum = TimelineSum.Over(timeline, years);
        double Pounds(double kilograms) => _metric ? kilograms : kilograms / UnitConversions.KilogramsPerPound;
        double Ounces(double kilograms) => _metric ? kilograms : kilograms / UnitConversions.KilogramsPerOunce;
        var carbonCost = sum.CarbonUsd * _usdToTargetRate;
        var stormCost = sum.StormWaterUsd * _usdToTargetRate;
        var airCost = sum.AirPollutionUsd * _usdToTargetRate;
        var co2Equivalent = Pounds(sum.CarbonKg * UnitConversions.CarbonToCo2MassRatio);
        return tree with
        {
            CarbonSequesteredAnnual = Pounds(sum.CarbonKg),
            CORemovedAnnual = Ounces(sum.CoKg),
            NO2RemovedAnnual = Ounces(sum.No2Kg),
            O3RemovedAnnual = Ounces(sum.O3Kg),
            PM25RemovedAnnual = Ounces(sum.Pm25Kg),
            SO2RemovedAnnual = Ounces(sum.So2Kg),
            CostSavedAnnual = carbonCost + stormCost + airCost,
            CarbonCostSavedAnnual = carbonCost,
            StormWaterCostSavedAnnual = stormCost,
            AirPollutionCostSavedAnnual = airCost,
            RainfallInterceptedAnnual = sum.InterceptionM3,
            RunoffAvoidedAnnual = sum.RunoffM3,
            CO2EquivalentAnnual = co2Equivalent,
            OxygenProducedAnnual = co2Equivalent * UnitConversions.OxygenToCo2MassRatio,
            MaintenanceCostAnnual = tree.MaintenanceCostAnnual * years
        };
    }
}

/// <summary>Sums a timeline's years 1..N; past the horizon, the last (mature) year repeats.</summary>
public static class TimelineSum
{
    public static TreeBenefitYear Over(TreeBenefitTimeline timeline, int years)
    {
        var ordered = timeline.Years.OrderBy(year => year.Year).ToList();
        if (ordered.Count == 0 || years <= 0)
        {
            return new TreeBenefitYear(years, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        double carbon = 0, runoff = 0, interception = 0, co = 0, no2 = 0, o3 = 0, so2 = 0, pm25 = 0, carbonUsd = 0, stormUsd = 0, airUsd = 0;
        for (var year = 1; year <= years; year++)
        {
            var entry = ordered[Math.Min(year, ordered.Count) - 1];
            carbon += entry.CarbonKg;
            runoff += entry.RunoffM3;
            interception += entry.InterceptionM3;
            co += entry.CoKg;
            no2 += entry.No2Kg;
            o3 += entry.O3Kg;
            so2 += entry.So2Kg;
            pm25 += entry.Pm25Kg;
            carbonUsd += entry.CarbonUsd;
            stormUsd += entry.StormWaterUsd;
            airUsd += entry.AirPollutionUsd;
        }

        return new TreeBenefitYear(years, carbon, runoff, interception, co, no2, o3, so2, pm25, carbonUsd, stormUsd, airUsd);
    }
}
