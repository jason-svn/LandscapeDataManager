using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

internal static class DashboardUnitLabels
{
    /// <summary><c>CarbonSequestered</c> is stored/normalized on a pounds-per-kilogram basis — see <see cref="DashboardAggregationService.NormalizeTree"/>.</summary>
    public static string CarbonUnit(string unitSystem) => IsMetric(unitSystem) ? "kg" : "lb";

    /// <summary>The five pollutant-removed fields are stored/normalized on an ounces-per-kilogram basis, a different conversion basis than CO2 — see <see cref="DashboardAggregationService.NormalizeTree"/>.</summary>
    public static string PollutantUnit(string unitSystem) => IsMetric(unitSystem) ? "kg" : "oz";

    public const string MainModelLabel = "Primary model";

    /// <summary>In no design option: Revit shows main-model elements in every design option.</summary>
    public static bool IsMainModel(DesignOptionInfo option) => DesignScenarios.IsMainModel(option);

    public static string FormatDesignOption(DesignOptionInfo option)
    {
        if (IsMainModel(option))
        {
            return MainModelLabel;
        }

        // Revit appends " <primary>" to the primary option's own name; "(Primary)" below says it once.
        var optionName = option.OptionName?.Replace("<primary>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrEmpty(optionName))
        {
            optionName = "Unnamed option";
        }

        var name = string.IsNullOrWhiteSpace(option.SetName) ? optionName : $"{option.SetName} : {optionName}";
        return option.IsPrimary ? $"{name} (Primary)" : name;
    }

    /// <summary>
    /// Parses a growth-year figure (5/10/15/20/25) out of a design option's formatted label, e.g.
    /// "Growth Timeline : 15 Years" -> 15. Shared by the Outlook projection dropdown and the
    /// per-design-option scenario breakdown (<see cref="DashboardJsonExportService"/>) so both agree
    /// on which years a project's growth-stage design options can carry.
    /// </summary>
    public static int? ExtractProjectionYears(string designOptionLabel) => GrowthYears.FromName(designOptionLabel);

    private static bool IsMetric(string unitSystem) => string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The dashboard's display units for one unit-system choice, and the conversions from the canonical
/// kilograms/cubic metres/square metres. Carbon, CO₂e and oxygen display per pound, pollutants per
/// ounce (see <see cref="DashboardAggregationService.NormalizeTree"/>).
/// </summary>
internal sealed record DisplayUnits(bool Metric)
{
    public static DisplayUnits For(string unitSystem) => new(string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase));

    public string Carbon => Metric ? "kg" : "lb";
    public string Pollutant => Metric ? "kg" : "oz";
    public string Water => Metric ? "m³" : "gal";
    public string Area => Metric ? "m²" : "ft²";

    public double Mass(double kilograms) => Metric ? kilograms : kilograms / UnitConversions.KilogramsPerPound;
    public double PollutantMass(double kilograms) => Metric ? kilograms : kilograms / UnitConversions.KilogramsPerOunce;
    public double Volume(double cubicMetres) => Metric ? cubicMetres : cubicMetres / UnitConversions.CubicMetresPerGallon;
    public double AreaOf(double squareMetres) => Metric ? squareMetres : squareMetres * 10.7639104167d;
}

internal static class DashboardFormat
{
    /// <summary>Thousands separated, with fewer decimals as numbers grow: 1,234 · 56.7 · 0.42.</summary>
    public static string Number(double value) => Math.Abs(value) switch
    {
        >= 1000d => value.ToString("N0"),
        >= 10d => value.ToString("N1"),
        _ => value.ToString("N2")
    };
}

/// <summary>A Data explorer species row: plain numbers, with the units in the column headers.</summary>
public sealed class SpeciesSubtotalRow
{
    public SpeciesSubtotalRow(SpeciesSubtotal subtotal, bool showAnnual)
    {
        SpeciesCode = subtotal.SpeciesCode;
        CommonName = subtotal.CommonName;
        TreeCount = subtotal.TreeCount;
        CarbonSequestered = DashboardFormat.Number(showAnnual ? subtotal.CarbonSequesteredAnnual : subtotal.CarbonSequesteredLifetimeTotal);
        OxygenProduced = DashboardFormat.Number(showAnnual ? subtotal.OxygenProducedAnnual : subtotal.OxygenProducedLifetimeTotal);
        PollutionMassRemoved = DashboardFormat.Number(showAnnual ? subtotal.PollutionRemovedAnnual : subtotal.PollutionRemovedLifetimeTotal);
        CarbonCostSaved = DashboardFormat.Number(showAnnual ? subtotal.CarbonCostSavedAnnual : subtotal.CarbonCostSavedLifetimeTotal);
        StormWaterCostSaved = DashboardFormat.Number(showAnnual ? subtotal.StormWaterCostSavedAnnual : subtotal.StormWaterCostSavedLifetimeTotal);
        AirPollutionCostSaved = DashboardFormat.Number(showAnnual ? subtotal.AirPollutionCostSavedAnnual : subtotal.AirPollutionCostSavedLifetimeTotal);
    }

    public string SpeciesCode { get; }
    public string CommonName { get; }
    public int TreeCount { get; }
    public string CarbonSequestered { get; }
    public string OxygenProduced { get; }
    public string PollutionMassRemoved { get; }
    public string CarbonCostSaved { get; }
    public string StormWaterCostSaved { get; }
    public string AirPollutionCostSaved { get; }
}

/// <summary>A Data explorer planting-area row in the display units; cost is as Floor Calculator wrote it, in the project currency.</summary>
public sealed class FloorSubtotalRow
{
    internal FloorSubtotalRow(FloorTypeSubtotal subtotal, DisplayUnits units)
    {
        LdsType = subtotal.LdsType;
        FloorCount = subtotal.FloorCount;
        AreaSquareMeters = DashboardFormat.Number(units.AreaOf(subtotal.AreaSquareMeters));
        CarbonSequestered = DashboardFormat.Number(units.Mass(subtotal.CarbonSequesteredAnnual));
        RunoffAvoided = DashboardFormat.Number(units.Volume(subtotal.RunoffAvoidedAnnual));
        PollutionMassRemoved = DashboardFormat.Number(units.PollutantMass(subtotal.PollutionMassRemovedAnnual));
        OxygenProduced = DashboardFormat.Number(units.Mass(subtotal.OxygenProducedAnnual));
        TotalGwp = DashboardFormat.Number(units.Mass(subtotal.TotalGwp));
        MaintenanceCost = DashboardFormat.Number(subtotal.MaintenanceCostAnnual);
        CostSaved = DashboardFormat.Number(subtotal.CostSavedAnnual);
    }

    public string LdsType { get; }
    public int FloorCount { get; }
    public string AreaSquareMeters { get; }
    public string CarbonSequestered { get; }
    public string RunoffAvoided { get; }
    public string PollutionMassRemoved { get; }
    public string OxygenProduced { get; }
    public string TotalGwp { get; }
    public string MaintenanceCost { get; }
    public string CostSaved { get; }
}

/// <summary>One species in the Executive overview's tree palette: its trees and their benefits over the outlook.</summary>
public sealed class SpeciesPaletteRow
{
    internal SpeciesPaletteRow(string speciesCode, string commonName, int count, double value, double co2, double runoff, double pollution, double oxygen, DisplayUnits units, string currency)
    {
        SpeciesCode = speciesCode;
        CommonName = commonName;
        Count = count;
        Value = value;
        Co2 = co2;
        Runoff = runoff;
        Pollution = pollution;
        Oxygen = oxygen;
        CountText = count.ToString("N0");
        ValueText = DashboardFormat.Number(value);
        Co2Text = DashboardFormat.Number(co2);
        RunoffText = DashboardFormat.Number(runoff);
        PollutionText = DashboardFormat.Number(pollution);
        OxygenText = DashboardFormat.Number(oxygen);
    }

    public string SpeciesCode { get; }
    public string CommonName { get; }
    public int Count { get; }
    public double Value { get; }
    public double Co2 { get; }
    public double Runoff { get; }
    public double Pollution { get; }
    public double Oxygen { get; }
    public string CountText { get; }
    public string ValueText { get; }
    public string Co2Text { get; }
    public string RunoffText { get; }
    public string PollutionText { get; }
    public string OxygenText { get; }
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
