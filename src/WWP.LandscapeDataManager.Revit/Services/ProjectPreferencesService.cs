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
    private const string SettingsJsonParameter = "!_S_PLT_Settings_Json_Text";

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

        return new PublishPreferredCurrencyResult(document.Title, request.CurrencyCode);
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
