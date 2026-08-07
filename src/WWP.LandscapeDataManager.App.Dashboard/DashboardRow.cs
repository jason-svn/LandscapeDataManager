using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

internal static class DashboardUnitLabels
{
    /// <summary><c>CO2Sequestered</c> is stored/normalized on a pounds-per-kilogram basis — see <see cref="DashboardAggregationService.NormalizeTree"/>.</summary>
    public static string Co2Unit(string unitSystem) => IsMetric(unitSystem) ? "kg" : "lb";

    /// <summary>The five pollutant-removed fields are stored/normalized on an ounces-per-kilogram basis, a different conversion basis than CO2 — see <see cref="DashboardAggregationService.NormalizeTree"/>.</summary>
    public static string PollutantUnit(string unitSystem) => IsMetric(unitSystem) ? "kg" : "oz";

    public static string FormatDesignOption(DesignOptionInfo option) =>
        option.IsPrimary ? "Primary" : $"{option.SetName} : {option.OptionName}";

    private static bool IsMetric(string unitSystem) => string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
}

public sealed class SpeciesSubtotalRow
{
    public SpeciesSubtotalRow(SpeciesSubtotal subtotal, string unitSystem, string currencyCode, bool showAnnual)
    {
        SpeciesCode = subtotal.SpeciesCode;
        CommonName = subtotal.CommonName;
        TreeCount = subtotal.TreeCount;

        var co2 = showAnnual ? subtotal.CO2SequesteredAnnual : subtotal.CO2SequesteredLifetimeTotal;
        var pollutionMass = showAnnual
            ? subtotal.PM25RemovedAnnual + subtotal.NO2RemovedAnnual + subtotal.O3RemovedAnnual + subtotal.SO2RemovedAnnual + subtotal.CORemovedAnnual
            : subtotal.PM25RemovedLifetimeTotal + subtotal.NO2RemovedLifetimeTotal + subtotal.O3RemovedLifetimeTotal + subtotal.SO2RemovedLifetimeTotal + subtotal.CORemovedLifetimeTotal;
        var cost = showAnnual ? subtotal.CostSavedAnnual : subtotal.CostSavedLifetimeTotal;

        CO2Sequestered = $"{co2:N1} {DashboardUnitLabels.Co2Unit(unitSystem)}";
        PollutionMassRemoved = $"{pollutionMass:N2} {DashboardUnitLabels.PollutantUnit(unitSystem)}";
        CostSaved = $"{cost:N2} {currencyCode}";
    }

    public string SpeciesCode { get; }
    public string CommonName { get; }
    public int TreeCount { get; }
    public string CO2Sequestered { get; }
    public string PollutionMassRemoved { get; }
    public string CostSaved { get; }
}

/// <summary><see cref="CostSaved"/> is labeled "as calculated" — Floor Calculator never records which currency it used, so there is nothing reliable to normalize from (see <see cref="FloorTypeSubtotal"/>).</summary>
public sealed class FloorSubtotalRow
{
    public FloorSubtotalRow(FloorTypeSubtotal subtotal)
    {
        LdsType = subtotal.LdsType;
        FloorCount = subtotal.FloorCount;
        AreaSquareMeters = $"{subtotal.AreaSquareMeters:N1} m²";
        CO2Sequestered = $"{subtotal.CO2SequesteredAnnual:N1} kg";
        TotalGwp = subtotal.TotalGwp.ToString("N1");
        CostSaved = $"{subtotal.CostSavedAnnual:N2} (as calculated)";
    }

    public string LdsType { get; }
    public int FloorCount { get; }
    public string AreaSquareMeters { get; }
    public string CO2Sequestered { get; }
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

        var co2 = showAnnual ? metrics.CO2SequesteredAnnual : metrics.CO2SequesteredLifetimeTotal;
        var pollutionMass = showAnnual
            ? metrics.PM25RemovedAnnual + metrics.NO2RemovedAnnual + metrics.O3RemovedAnnual + metrics.SO2RemovedAnnual + metrics.CORemovedAnnual
            : metrics.PM25RemovedLifetimeTotal + metrics.NO2RemovedLifetimeTotal + metrics.O3RemovedLifetimeTotal + metrics.SO2RemovedLifetimeTotal + metrics.CORemovedLifetimeTotal;
        var cost = showAnnual ? metrics.CostSavedAnnual : metrics.CostSavedLifetimeTotal;

        CO2Sequestered = $"{co2:N1} {DashboardUnitLabels.Co2Unit(unitSystem)}";
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
    public string CO2Sequestered { get; }
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
        CO2Sequestered = $"{item.CO2SequesteredAnnual:N1} kg";
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
    public string CO2Sequestered { get; }
    public string TotalGwp { get; }
    public string CostSaved { get; }
}
