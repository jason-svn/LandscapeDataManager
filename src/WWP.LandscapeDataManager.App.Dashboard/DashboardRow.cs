using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

internal static class DashboardUnitLabels
{
    /// <summary><c>CarbonSequestered</c> is stored/normalized on a pounds-per-kilogram basis — see <see cref="DashboardAggregationService.NormalizeTree"/>.</summary>
    public static string CarbonUnit(string unitSystem) => IsMetric(unitSystem) ? "kg" : "lb";

    /// <summary>The five pollutant-removed fields are stored/normalized on an ounces-per-kilogram basis, a different conversion basis than CO2 — see <see cref="DashboardAggregationService.NormalizeTree"/>.</summary>
    public static string PollutantUnit(string unitSystem) => IsMetric(unitSystem) ? "kg" : "oz";

    public static string FormatDesignOption(DesignOptionInfo option)
    {
        if (string.IsNullOrWhiteSpace(option.SetName) && string.IsNullOrWhiteSpace(option.OptionName))
        {
            return "Primary model";
        }

        var name = string.IsNullOrWhiteSpace(option.SetName)
            ? option.OptionName ?? "Unnamed option"
            : $"{option.SetName} : {option.OptionName ?? "Unnamed option"}";
        return option.IsPrimary ? $"{name} (Primary)" : name;
    }

    private static bool IsMetric(string unitSystem) => string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
}

public sealed class SpeciesSubtotalRow
{
    public SpeciesSubtotalRow(SpeciesSubtotal subtotal, string unitSystem, string currencyCode, bool showAnnual)
    {
        SpeciesCode = subtotal.SpeciesCode;
        CommonName = subtotal.CommonName;
        TreeCount = subtotal.TreeCount;

        var carbon = showAnnual ? subtotal.CarbonSequesteredAnnual : subtotal.CarbonSequesteredLifetimeTotal;
        var pollutionMass = showAnnual
            ? subtotal.PM25RemovedAnnual + subtotal.NO2RemovedAnnual + subtotal.O3RemovedAnnual + subtotal.SO2RemovedAnnual + subtotal.CORemovedAnnual
            : subtotal.PM25RemovedLifetimeTotal + subtotal.NO2RemovedLifetimeTotal + subtotal.O3RemovedLifetimeTotal + subtotal.SO2RemovedLifetimeTotal + subtotal.CORemovedLifetimeTotal;
        var carbonCost = showAnnual ? subtotal.CarbonCostSavedAnnual : subtotal.CarbonCostSavedLifetimeTotal;
        var stormWaterCost = showAnnual ? subtotal.StormWaterCostSavedAnnual : subtotal.StormWaterCostSavedLifetimeTotal;
        var airPollutionCost = showAnnual ? subtotal.AirPollutionCostSavedAnnual : subtotal.AirPollutionCostSavedLifetimeTotal;

        CarbonSequestered = $"{carbon:N1} {DashboardUnitLabels.CarbonUnit(unitSystem)}";
        PollutionMassRemoved = $"{pollutionMass:N2} {DashboardUnitLabels.PollutantUnit(unitSystem)}";
        CarbonCostSaved = $"{carbonCost:N2} {currencyCode}";
        StormWaterCostSaved = $"{stormWaterCost:N2} {currencyCode}";
        AirPollutionCostSaved = $"{airPollutionCost:N2} {currencyCode}";
    }

    public string SpeciesCode { get; }
    public string CommonName { get; }
    public int TreeCount { get; }
    public string CarbonSequestered { get; }
    public string PollutionMassRemoved { get; }
    public string CarbonCostSaved { get; }
    public string StormWaterCostSaved { get; }
    public string AirPollutionCostSaved { get; }
}

/// <summary><see cref="CostSaved"/> is labeled "as calculated" — Floor Calculator never records which currency it used, so there is nothing reliable to normalize from (see <see cref="FloorTypeSubtotal"/>).</summary>
public sealed class FloorSubtotalRow
{
    public FloorSubtotalRow(FloorTypeSubtotal subtotal)
    {
        LdsType = subtotal.LdsType;
        FloorCount = subtotal.FloorCount;
        AreaSquareMeters = $"{subtotal.AreaSquareMeters:N1} m²";
        CarbonSequestered = $"{subtotal.CarbonSequesteredAnnual:N1} kg";
        RunoffAvoided = $"{subtotal.RunoffAvoidedAnnual:N1} m³";
        PollutionMassRemoved = $"{subtotal.PollutionMassRemovedAnnual:N2} kg";
        TotalGwp = subtotal.TotalGwp.ToString("N1");
        CostSaved = $"{subtotal.CostSavedAnnual:N2} (as calculated)";
    }

    public string LdsType { get; }
    public int FloorCount { get; }
    public string AreaSquareMeters { get; }
    public string CarbonSequestered { get; }
    public string RunoffAvoided { get; }
    public string PollutionMassRemoved { get; }
    public string TotalGwp { get; }
    public string CostSaved { get; }
}

public sealed class DashboardTreeRow
{
    public DashboardTreeRow(NormalizedTreeMetrics metrics, string unitSystem, string currencyCode, bool showAnnual)
    {
        var item = metrics.Source;
        Metrics = metrics;
        UniqueId = item.UniqueId;
        ElementId = item.ElementId;
        FamilyType = $"{item.FamilyName} : {item.TypeName}";
        SpeciesCode = item.SpeciesCode ?? "—";
        CommonName = item.CommonName ?? "—";
        LevelName = item.LevelName ?? "—";
        DesignOption = DashboardUnitLabels.FormatDesignOption(item.DesignOption);
        Status = item.Status;
        CountsTowardTotals = metrics.CountsTowardTotals;

        var carbon = showAnnual ? metrics.CarbonSequesteredAnnual : metrics.CarbonSequesteredLifetimeTotal;
        var pollutionMass = showAnnual
            ? metrics.PM25RemovedAnnual + metrics.NO2RemovedAnnual + metrics.O3RemovedAnnual + metrics.SO2RemovedAnnual + metrics.CORemovedAnnual
            : metrics.PM25RemovedLifetimeTotal + metrics.NO2RemovedLifetimeTotal + metrics.O3RemovedLifetimeTotal + metrics.SO2RemovedLifetimeTotal + metrics.CORemovedLifetimeTotal;
        var cost = showAnnual ? metrics.CostSavedAnnual : metrics.CostSavedLifetimeTotal;

        CarbonSequestered = $"{carbon:N1} {DashboardUnitLabels.CarbonUnit(unitSystem)}";
        PollutionMassRemoved = $"{pollutionMass:N2} {DashboardUnitLabels.PollutantUnit(unitSystem)}";
        CostSaved = $"{cost:N2} {currencyCode}";
    }

    public NormalizedTreeMetrics Metrics { get; }
    public string UniqueId { get; }
    public long ElementId { get; }
    public string FamilyType { get; }
    public string SpeciesCode { get; }
    public string CommonName { get; }
    public string LevelName { get; }
    public string DesignOption { get; }
    public string Status { get; }
    public bool CountsTowardTotals { get; }
    public string CarbonSequestered { get; }
    public string PollutionMassRemoved { get; }
    public string CostSaved { get; }
}

public sealed class DashboardFloorRow
{
    public DashboardFloorRow(DashboardFloorItem item)
    {
        Item = item;
        UniqueId = item.UniqueId;
        ElementId = item.ElementId;
        FamilyType = $"{item.FamilyName} : {item.TypeName}";
        LdsType = item.LdsType ?? "—";
        LevelName = item.LevelName ?? "—";
        DesignOption = DashboardUnitLabels.FormatDesignOption(item.DesignOption);
        AreaSquareMeters = $"{item.AreaSquareMeters:N1} m²";
        CarbonSequestered = $"{item.CarbonSequesteredAnnual:N1} kg";
        RunoffAvoided = $"{item.RunoffAvoidedAnnual:N1} m³";
        PollutionMassRemoved = $"{item.PollutionMassRemovedAnnual:N2} kg";
        TotalGwp = item.TotalGwp.ToString("N1");
        CostSaved = $"{item.CostSavedAnnual:N2} (as calculated)";
    }

    public DashboardFloorItem Item { get; }
    public string UniqueId { get; }
    public long ElementId { get; }
    public string FamilyType { get; }
    public string LdsType { get; }
    public string LevelName { get; }
    public string DesignOption { get; }
    public string AreaSquareMeters { get; }
    public string CarbonSequestered { get; }
    public string RunoffAvoided { get; }
    public string PollutionMassRemoved { get; }
    public string TotalGwp { get; }
    public string CostSaved { get; }
}

/// <summary>Small presentation-only row used by the graphic KPI pages.</summary>
public sealed class KpiBarRow
{
    public KpiBarRow(string label, string valueText, double percent, string detail = "")
    {
        Label = label;
        ValueText = valueText;
        Percent = Math.Clamp(percent, 0d, 100d);
        Detail = detail;
    }

    public string Label { get; }
    public string ValueText { get; }
    public double Percent { get; }
    public string Detail { get; }
}

public sealed class ScenarioComparisonRow
{
    public ScenarioComparisonRow(string designOption, string inventory, string speciesCount, string projectedCarbon, double percent)
    {
        DesignOption = designOption;
        Inventory = inventory;
        SpeciesCount = speciesCount;
        ProjectedCarbon = projectedCarbon;
        Percent = Math.Clamp(percent, 0d, 100d);
    }

    public string DesignOption { get; }
    public string Inventory { get; }

    /// <summary>Biodiversity Added's species-richness metric, per design option — see <see cref="DashboardAggregationService.BuildSiteKpiSummary"/>.</summary>
    public string SpeciesCount { get; }
    public string ProjectedCarbon { get; }
    public double Percent { get; }
}
