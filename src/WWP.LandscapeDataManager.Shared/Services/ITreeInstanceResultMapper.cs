using System.Globalization;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Turns one instance's calculation outcome into the exact set of parameter writes the Calculate
/// &amp; QC tool needs: the 4 required tracking parameters plus (on success) the numeric benefit
/// results, converted to the project's preferred unit system. Pure/testable — the Revit-side
/// write itself reuses the existing <see cref="InstanceParameterWriter"/> from Planting Data Sync.
/// </summary>
public static class ITreeInstanceResultMapper
{
    private const double KilogramsPerPound = 0.45359237d;
    private const double KilogramsPerOunce = 0.028349523125d;
    private const double CubicMetresPerGallon = 0.00378541d;

    public static IReadOnlyList<InstanceParameterWriteItem> BuildWriteItems(
        string uniqueId,
        string status,
        string? details,
        string inputSignature,
        ITreeInstanceCalculationOutcome? outcome,
        string preferredUnitSystem)
    {
        var items = new List<InstanceParameterWriteItem>
        {
            Text(uniqueId, "!_S_PLANTING_iTreeResult_Status_Text", status),
            Text(uniqueId, "!_S_PLANTING_iTreeResult_Details_Text", details ?? string.Empty),
            Text(uniqueId, "!_S_PLANTING_iTreeResult_LastUpdated_Text", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            Text(uniqueId, "!_S_PLANTING_iTreeResult_InputSignature_Text", inputSignature),
            Text(uniqueId, "!_S_PLANTING_iTreeResult_UnitSystem_Text", preferredUnitSystem)
        };

        if (outcome?.Fields is not { } fields)
        {
            return items;
        }

        double GetOrZero(string key) => fields.TryGetValue(key, out var value) && value is double number ? number : 0d;
        string? GetText(string key) => fields.TryGetValue(key, out var value) ? value?.ToString() : null;

        var engineVersion = GetText("Engine_Version") ?? GetText("Database_Version") ?? "i-Tree API v3";
        items.Add(Text(uniqueId, "!_S_PLANTING_iTreeResult_EngineVersion_Text", engineVersion));

        var isMetric = string.Equals(preferredUnitSystem, "Metric", StringComparison.OrdinalIgnoreCase);

        double FromPounds(double pounds) => isMetric ? pounds * KilogramsPerPound : pounds;
        double FromOunces(double ounces) => isMetric ? ounces * KilogramsPerOunce : ounces;

        items.Add(Number(uniqueId, "!_S_PLANTING_iTreeCarbon_CO2SequesteredAnnual_Number",
            FromPounds(GetOrZero("Annual_CarbonSequestered_lb"))));
        items.Add(Number(uniqueId, "!_S_PLANTING_iTreeAir_CORemovedAnnual_Number", FromOunces(GetOrZero("Annual_CO_oz"))));
        items.Add(Number(uniqueId, "!_S_PLANTING_iTreeAir_NO2RemovedAnnual_Number", FromOunces(GetOrZero("Annual_NO2_oz"))));
        items.Add(Number(uniqueId, "!_S_PLANTING_iTreeAir_O3RemovedAnnual_Number", FromOunces(GetOrZero("Annual_O3_oz"))));
        items.Add(Number(uniqueId, "!_S_PLANTING_iTreeAir_PM25RemovedAnnual_Number", FromOunces(GetOrZero("Annual_PM25_oz"))));
        items.Add(Number(uniqueId, "!_S_PLANTING_iTreeAir_SO2RemovedAnnual_Number", FromOunces(GetOrZero("Annual_SO2_oz"))));

        var rainfallCubicMetres = GetOrZero("Annual_RainfallIntercepted_gal") * CubicMetresPerGallon;
        var runoffCubicMetres = GetOrZero("Annual_RunoffAvoided_gal") * CubicMetresPerGallon;
        items.Add(Volume(uniqueId, "!_S_PLANTING_iTreeWater_RainfallInterceptedAnnual_Volume", rainfallCubicMetres));
        items.Add(Volume(uniqueId, "!_S_PLANTING_iTreeWater_RunoffAvoidedAnnual_Volume", runoffCubicMetres));

        return items;
    }

    private static InstanceParameterWriteItem Text(string uniqueId, string parameter, string value) =>
        new(uniqueId, parameter, "Text", value);

    private static InstanceParameterWriteItem Number(string uniqueId, string parameter, double value) =>
        new(uniqueId, parameter, "Number (no conversion)", value.ToString("G17", CultureInfo.InvariantCulture));

    private static InstanceParameterWriteItem Volume(string uniqueId, string parameter, double cubicMetres) =>
        new(uniqueId, parameter, "Auto (Revit spec)", cubicMetres.ToString("G17", CultureInfo.InvariantCulture));
}
