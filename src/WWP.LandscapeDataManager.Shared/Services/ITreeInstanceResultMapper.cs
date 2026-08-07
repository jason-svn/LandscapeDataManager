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
    /// <param name="preferredCurrency">ISO code (USD, GBP, EUR, CAD, AUD, or NZD) the caller has already resolved the project's preference to.</param>
    /// <param name="usdExchangeRate">USD-to-<paramref name="preferredCurrency"/> rate the caller already looked up (1.0 for USD) — this method only applies it, it never fetches rates itself so it stays synchronous/pure.</param>
    public static IReadOnlyList<InstanceParameterWriteItem> BuildWriteItems(
        string uniqueId,
        string status,
        string? details,
        string inputSignature,
        ITreeInstanceCalculationOutcome? outcome,
        string preferredUnitSystem,
        string preferredCurrency = "USD",
        double usdExchangeRate = 1d)
    {
        var items = new List<InstanceParameterWriteItem>
        {
            Text(uniqueId, "!_S_PLT_iTreeResult_Status_Text", status),
            Text(uniqueId, "!_S_PLT_iTreeResult_Details_Text", details ?? string.Empty),
            Text(uniqueId, "!_S_PLT_iTreeResult_LastUpdated_Text", FormatLocalTimestamp()),
            Text(uniqueId, "!_S_PLT_iTreeResult_InputSignature_Text", inputSignature),
            Text(uniqueId, "!_S_PLT_iTreeResult_UnitSystem_Text", preferredUnitSystem)
        };

        if (outcome?.Fields is not { } fields)
        {
            return items;
        }

        double GetOrZero(string key) => fields.TryGetValue(key, out var value) && value is double number ? number : 0d;
        string? GetText(string key) => fields.TryGetValue(key, out var value) ? value?.ToString() : null;

        var engineVersion = GetText("Engine_Version") ?? GetText("Database_Version") ?? "i-Tree API v3";
        items.Add(Text(uniqueId, "!_S_PLT_iTreeResult_EngineVersion_Text", engineVersion));
        items.Add(Text(uniqueId, "!_S_PLT_iTreeResult_CurrencyUsed_Text", preferredCurrency));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_ExchangeRateUsed_Number", usdExchangeRate));

        var isMetric = string.Equals(preferredUnitSystem, "Metric", StringComparison.OrdinalIgnoreCase);

        double FromPounds(double pounds) => isMetric ? pounds * UnitConversions.KilogramsPerPound : pounds;
        double FromOunces(double ounces) => isMetric ? ounces * UnitConversions.KilogramsPerOunce : ounces;
        // i-Tree's API only ever reports dollars, regardless of tree location — this is the one
        // point where that USD figure becomes the project's preferred currency (usdExchangeRate is
        // 1.0 for USD itself, so this is a no-op multiply in the common case).
        double ToPreferredCurrency(double usd) => usd * usdExchangeRate;

        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number",
            FromPounds(GetOrZero("Annual_CarbonSequestered_lb"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CORemovedAnnual_Number", FromOunces(GetOrZero("Annual_CO_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_NO2RemovedAnnual_Number", FromOunces(GetOrZero("Annual_NO2_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_O3RemovedAnnual_Number", FromOunces(GetOrZero("Annual_O3_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_PM25RemovedAnnual_Number", FromOunces(GetOrZero("Annual_PM25_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_SO2RemovedAnnual_Number", FromOunces(GetOrZero("Annual_SO2_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CostSavedAnnual_Number", ToPreferredCurrency(GetOrZero("Annual_Benefit_USD"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CO2EquivalentAnnual_Number",
            FromPounds(GetOrZero("Annual_CO2Equivalent_lb"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Number", ToPreferredCurrency(GetOrZero("Annual_CarbonBenefit_USD"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Number", ToPreferredCurrency(GetOrZero("Annual_StormWaterBenefit_USD"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Number", ToPreferredCurrency(GetOrZero("Annual_AirPollutionBenefit_USD"))));

        var rainfallCubicMetres = GetOrZero("Annual_RainfallIntercepted_gal") * UnitConversions.CubicMetresPerGallon;
        var runoffCubicMetres = GetOrZero("Annual_RunoffAvoided_gal") * UnitConversions.CubicMetresPerGallon;
        items.Add(Volume(uniqueId, "!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume", rainfallCubicMetres));
        items.Add(Volume(uniqueId, "!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume", runoffCubicMetres));

        // Lifetime cumulative results, parallel to the annual set above. "Lifetime" tracks whatever
        // TreeGrowth_Years was set to on this instance (the API's "*_20yr_*" field names are a legacy
        // holdover from when Years defaulted to 20 — they actually sum one entry per requested year,
        // so a tree modeled at Years=25 produces a 25-year total here, not a fixed 20-year one).
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CO2SequesteredLifetimeTotal_Number",
            FromPounds(GetOrZero("CarbonSequestered_20yr_lb"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Number", FromOunces(GetOrZero("CO_20yr_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Number", FromOunces(GetOrZero("NO2_20yr_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Number", FromOunces(GetOrZero("O3_20yr_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Number", FromOunces(GetOrZero("PM25_20yr_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Number", FromOunces(GetOrZero("SO2_20yr_oz"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Number", ToPreferredCurrency(GetOrZero("Benefit_20yr_USD"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CO2EquivalentLifetimeTotal_Number",
            FromPounds(GetOrZero("CO2Equivalent_20yr_lb"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Number", ToPreferredCurrency(GetOrZero("CarbonBenefit_20yr_USD"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Number", ToPreferredCurrency(GetOrZero("StormWaterBenefit_20yr_USD"))));
        items.Add(Number(uniqueId, "!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Number", ToPreferredCurrency(GetOrZero("AirPollutionBenefit_20yr_USD"))));

        var lifetimeRainfallCubicMetres = GetOrZero("RainfallIntercepted_20yr_gal") * UnitConversions.CubicMetresPerGallon;
        var lifetimeRunoffCubicMetres = GetOrZero("RunoffAvoided_20yr_gal") * UnitConversions.CubicMetresPerGallon;
        items.Add(Volume(uniqueId, "!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume", lifetimeRainfallCubicMetres));
        items.Add(Volume(uniqueId, "!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume", lifetimeRunoffCubicMetres));

        return items;
    }

    private static InstanceParameterWriteItem Text(string uniqueId, string parameter, string value) =>
        new(uniqueId, parameter, "Text", value);

    private static InstanceParameterWriteItem Number(string uniqueId, string parameter, double value) =>
        new(uniqueId, parameter, "Number (no conversion)", value.ToString("G17", CultureInfo.InvariantCulture));

    private static InstanceParameterWriteItem Volume(string uniqueId, string parameter, double cubicMetres) =>
        new(uniqueId, parameter, "Auto (Revit spec)", cubicMetres.ToString("G17", CultureInfo.InvariantCulture));

    /// <summary>Local wall-clock time in the machine's own time zone, e.g. "2026-08-04, 14:23:07, Eastern Standard Time" — readable at a glance instead of a UTC ISO 8601 stamp.</summary>
    private static string FormatLocalTimestamp()
    {
        var now = DateTimeOffset.Now;
        var zone = TimeZoneInfo.Local;
        var zoneName = zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName;
        return $"{now.ToString("yyyy-MM-dd, HH:mm:ss", CultureInfo.InvariantCulture)}, {zoneName}";
    }
}
