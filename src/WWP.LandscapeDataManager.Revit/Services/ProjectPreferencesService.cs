using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Writes project-wide i-Tree reporting preferences onto Project Information — currently just the
/// preferred currency (see <see cref="RevitModelScanner"/> for the read side and the offered
/// currency list). Mirrors <see cref="ProjectLocationService.Publish"/>'s transaction pattern.
/// </summary>
internal static class ProjectPreferencesService
{
    private const string PreferredCurrencyParameter = "!_S_PLT_iTreeUnits_PreferredCurrency_Text";
    private const string PreferredCurrencyFactorParameter = "!_S_PLT_iTreeUnits_PreferredCurrencyFactor_Number";
    private const string PreferredUnitSystemParameter = "!_S_PLT_iTreeUnits_PreferredSystem_Text";
    private const string SettingsJsonParameter = "!_S_PLT_Settings_Json_Text";
    private const string CurrencyUsedParameter = "!_S_PLT_iTreeResult_CurrencyUsed_Text";
    private const string ExchangeRateUsedParameter = "!_S_PLT_iTreeResult_ExchangeRateUsed_Number";
    private const double MassVolumeAccuracy = 0.001;
    private const double CurrencyAccuracy = 0.01;

    private static readonly BuiltInCategory[] RescaleCategories =
    [
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Floors
    ];

    /// <summary>
    /// Every Currency-spec result parameter this app computes (as opposed to imports verbatim from
    /// Airtable — <c>!_S_PLT_LDS_MaintenanceCostAnnual_Currency</c> and
    /// <c>!_S_PLT_LDS_OxygenCostSavedAnnual_Currency</c> are deliberately excluded, since rescaling
    /// externally-sourced data would corrupt it). Trees have all eight; floors only ever populate
    /// the first.
    /// </summary>
    private static readonly IReadOnlyList<string> RescalableCurrencyParameters =
    [
        "!_S_PLT_iTreeResult_CostSavedAnnual_Currency",
        "!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Currency",
        "!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Currency",
        "!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Currency",
        "!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Currency",
        "!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Currency",
        "!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Currency",
        "!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Currency"
    ];

    /// <summary>
    /// Revit's Currency spec has one symbol for the whole project (Project Units), not a per-parameter
    /// or per-instance choice — but since this app's currency preference is also one-per-project, we
    /// can keep the two in sync automatically. There's no separate symbol for CAD/AUD/NZD (Revit only
    /// offers one dollar sign), so they share <see cref="SymbolTypeId.UsDollar"/> with USD; the symbol
    /// itself is still just a display affectation — the ISO code that actually matters for math lives
    /// in <see cref="PreferredCurrencyParameter"/> and each instance's own CurrencyUsed_Text.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ForgeTypeId> CurrencySymbols = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = SymbolTypeId.UsDollar,
        ["CAD"] = SymbolTypeId.UsDollar,
        ["AUD"] = SymbolTypeId.UsDollar,
        ["NZD"] = SymbolTypeId.UsDollar,
        ["GBP"] = SymbolTypeId.UkPound,
        ["EUR"] = SymbolTypeId.EuroPrefix
    };

    public static PublishPreferredCurrencyResult PublishPreferredCurrency(
        UIApplication application,
        PublishPreferredCurrencyRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before publishing a currency preference.");

        if (!RevitModelScanner.SupportedCurrencyCodes.Contains(request.CurrencyCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{request.CurrencyCode}' is not a supported currency. Use one of: {string.Join(", ", RevitModelScanner.SupportedCurrencyCodes)}.");
        }

        var projectInfo = document.ProjectInformation;
        using var transaction = new Transaction(document, "LIM Publish Preferred Currency");
        transaction.Start();
        try
        {
            var parameter = projectInfo.LookupParameter(PreferredCurrencyParameter);
            if (parameter is { IsReadOnly: false })
            {
                parameter.Set(request.CurrencyCode);
            }

            var factorParameter = projectInfo.LookupParameter(PreferredCurrencyFactorParameter);
            if (factorParameter is { IsReadOnly: false })
            {
                factorParameter.Set(request.CurrencyFactor);
            }

            SetCurrencySymbol(document, request.CurrencyCode);
            RescaleExistingCurrencyValues(document, request.CurrencyCode, request.CurrencyFactor);

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new PublishPreferredCurrencyResult(document.Title, request.CurrencyCode, request.CurrencyFactor);
    }

    /// <summary>
    /// Rescales every already-calculated planting instance's and floor's monetary results from
    /// whatever rate they were last calculated under to the newly-selected currency, so switching
    /// currency doesn't leave stale-currency dollar figures behind on data calculated before the
    /// switch. Not-yet-calculated elements (no <see cref="ExchangeRateUsedParameter"/> value) are
    /// left alone — they pick up the new preference naturally the next time they're calculated.
    /// Uses each element's own stored rate as the "old" rate (not the project's prior preference),
    /// since different elements may have last been calculated under different rates.
    /// </summary>
    private static void RescaleExistingCurrencyValues(Document document, string newCurrencyCode, double newRate)
    {
        var categoryFilter = new ElementMulticategoryFilter(RescaleCategories);
        var elements = new FilteredElementCollector(document)
            .WherePasses(categoryFilter)
            .WhereElementIsNotElementType();

        foreach (var element in elements)
        {
            var rateParameter = element.LookupParameter(ExchangeRateUsedParameter);
            // Pre-existing floors calculated before this parameter applied to Floors still have a
            // CostSavedAnnual_Currency value, just no recorded rate — treat that as an implicit 1.0/USD
            // rather than skipping them.
            var hasCostSaved = element.LookupParameter(RescalableCurrencyParameters[0]) is { HasValue: true };
            if (rateParameter is not { HasValue: true } && !hasCostSaved)
            {
                continue;
            }

            var storedRate = rateParameter is { HasValue: true } ? rateParameter.AsDouble() : 1d;
            if (storedRate <= 0d)
            {
                continue;
            }

            var scale = newRate / storedRate;
            foreach (var parameterName in RescalableCurrencyParameters)
            {
                var parameter = element.LookupParameter(parameterName);
                if (parameter is { HasValue: true, IsReadOnly: false })
                {
                    parameter.Set(parameter.AsDouble() * scale);
                }
            }

            var currencyUsedParameter = element.LookupParameter(CurrencyUsedParameter);
            if (currencyUsedParameter is { IsReadOnly: false })
            {
                currencyUsedParameter.Set(newCurrencyCode);
            }

            if (rateParameter is { IsReadOnly: false })
            {
                rateParameter.Set(newRate);
            }
        }
    }

    public static PublishPreferredUnitSystemResult PublishPreferredUnitSystem(
        UIApplication application,
        PublishPreferredUnitSystemRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before publishing a unit system preference.");

        if (!string.Equals(request.UnitSystem, "Metric", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unit system must be 'Metric' or 'Imperial'.");
        }

        var projectInfo = document.ProjectInformation;
        using var transaction = new Transaction(document, "LIM Publish Preferred Unit System");
        transaction.Start();
        try
        {
            var parameter = projectInfo.LookupParameter(PreferredUnitSystemParameter);
            if (parameter is { IsReadOnly: false })
            {
                parameter.Set(request.UnitSystem);
            }

            var warning = SetDisplayUnits(document, request.UnitSystem);

            transaction.Commit();
            return new PublishPreferredUnitSystemResult(document.Title, request.UnitSystem, warning);
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
    }

    /// <summary>
    /// Sets the project's Mass and Volume display units to match the preference — no data rewrite
    /// needed, since every LIM Mass/Volume result parameter already stores raw SI (kg/m³) and
    /// displays per these project settings. Mass and Volume are set independently (and their
    /// failures reported, not swallowed) so a problem with one spec doesn't hide a problem with, or
    /// block, the other — unlike <see cref="SetCurrencySymbol"/>, whose only effect is cosmetic.
    /// </summary>
    private static string? SetDisplayUnits(Document document, string unitSystem)
    {
        var isMetric = string.Equals(unitSystem, "Metric", StringComparison.OrdinalIgnoreCase);
        var units = document.GetUnits();
        var failures = new List<string>();

        // Mutate the existing FormatOptions rather than constructing a fresh one — a fresh
        // FormatOptions(unitId) resets symbol/accuracy/rounding to bare defaults (no unit symbol at
        // all), wiping out whatever the project template already had configured.
        try
        {
            var massOptions = units.GetFormatOptions(SpecTypeId.Mass);
            massOptions.SetUnitTypeId(isMetric ? UnitTypeId.Kilograms : UnitTypeId.PoundsMass);
            SetSymbolIfValid(massOptions, isMetric ? SymbolTypeId.Kg : SymbolTypeId.LbMass);
            SetAccuracyIfValid(massOptions, MassVolumeAccuracy);
            units.SetFormatOptions(SpecTypeId.Mass, massOptions);
        }
        catch (Exception exception)
        {
            failures.Add($"Mass: {exception.Message}");
        }

        try
        {
            var volumeOptions = units.GetFormatOptions(SpecTypeId.Volume);
            volumeOptions.SetUnitTypeId(isMetric ? UnitTypeId.CubicMeters : UnitTypeId.UsGallons);
            SetSymbolIfValid(volumeOptions, isMetric ? SymbolTypeId.MSup3 : SymbolTypeId.Gal);
            SetAccuracyIfValid(volumeOptions, MassVolumeAccuracy);
            units.SetFormatOptions(SpecTypeId.Volume, volumeOptions);
        }
        catch (Exception exception)
        {
            failures.Add($"Volume: {exception.Message}");
        }

        try
        {
            document.SetUnits(units);
        }
        catch (Exception exception)
        {
            failures.Add($"SetUnits: {exception.Message}");
        }

        return failures.Count == 0
            ? null
            : $"The unit-system preference was saved, but the project's display units could not be updated — {string.Join("; ", failures)}";
    }

    /// <summary>
    /// Without this, changing just the unit type (kg vs lb) leaves whatever symbol the project
    /// already had — often none, so the Properties palette shows a bare number with no "kg"/"lb"/"m³"
    /// label at all. Guarded the same way <see cref="SetCurrencySymbol"/> guards its own symbol call.
    /// </summary>
    private static void SetSymbolIfValid(FormatOptions options, ForgeTypeId symbol)
    {
        if (options.CanHaveSymbol() && options.IsValidSymbol(symbol))
        {
            options.SetSymbolTypeId(symbol);
        }
    }

    /// <summary>
    /// Rounds to the given accuracy and disables trailing-zero suppression, so Mass/Volume
    /// consistently show 3 decimal digits (0.001) and Currency shows the usual 2 (0.01) — e.g.
    /// "3.000"/"£1.22", not "3"/"£1.2" — instead of whatever coarser accuracy the project template
    /// happened to have. Without disabling suppression, an exact value would still collapse back to
    /// fewer decimals (e.g. "3" instead of "3.000").
    /// </summary>
    private static void SetAccuracyIfValid(FormatOptions options, double accuracy)
    {
        if (options.IsValidAccuracy(accuracy))
        {
            options.Accuracy = accuracy;
        }

        if (options.CanSuppressTrailingZeros())
        {
            options.SuppressTrailingZeros = false;
        }
    }

    /// <summary>
    /// Best-effort — the currency preference itself (above) is the operation that actually matters;
    /// if the native Currency symbol can't be set for any reason, that's cosmetic and shouldn't fail
    /// the whole publish.
    /// </summary>
    private static void SetCurrencySymbol(Document document, string currencyCode)
    {
        if (!CurrencySymbols.TryGetValue(currencyCode, out var symbol))
        {
            return;
        }

        try
        {
            var units = document.GetUnits();
            var formatOptions = units.GetFormatOptions(SpecTypeId.Currency);
            if (!formatOptions.CanHaveSymbol() || !formatOptions.IsValidSymbol(symbol))
            {
                return;
            }

            formatOptions.SetSymbolTypeId(symbol);
            SetAccuracyIfValid(formatOptions, CurrencyAccuracy);
            units.SetFormatOptions(SpecTypeId.Currency, formatOptions);
            document.SetUnits(units);
        }
        catch
        {
            // Ignored — see method summary.
        }
    }

    public static GetProjectSettingsJsonResult GetSettingsJson(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before reading its settings.");

        var parameter = document.ProjectInformation.LookupParameter(SettingsJsonParameter);
        var json = parameter?.StorageType == StorageType.String ? parameter.AsString() : null;
        return new GetProjectSettingsJsonResult(document.Title, json);
    }

    public static PublishProjectSettingsJsonResult PublishSettingsJson(
        UIApplication application,
        PublishProjectSettingsJsonRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before publishing settings.");

        var projectInfo = document.ProjectInformation;
        using var transaction = new Transaction(document, "LIM Publish Project Settings");
        transaction.Start();
        try
        {
            var parameter = projectInfo.LookupParameter(SettingsJsonParameter);
            if (parameter is { IsReadOnly: false })
            {
                parameter.Set(request.SettingsJson);
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new PublishProjectSettingsJsonResult(document.Title);
    }
}
