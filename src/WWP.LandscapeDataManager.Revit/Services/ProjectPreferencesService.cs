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
    private const string SettingsJsonParameter = "!_S_PLT_Settings_Json_Text";

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
