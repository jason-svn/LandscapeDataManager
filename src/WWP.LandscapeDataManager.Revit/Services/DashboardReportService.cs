using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Reads back whatever i-Tree/LDS benefit results are already stored on Planting and Floor
/// instances — no calculation happens here, this only reports what <see cref="FloorLdsCalculationService"/>
/// and the i-Tree Calculator tool (<c>ITreeInstanceResultMapper</c>) have already written. Every
/// instance is returned regardless of Design Option (unlike <see cref="RevitModelScanner"/>'s
/// primary-only scans) — the Dashboard tool filters by Design Option itself, since summarizing
/// benefits across design alternatives is the point of the report.
/// </summary>
internal static class DashboardReportService
{
    private static readonly BuiltInCategory[] SupportedCategories =
    [
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Floors
    ];

    public static DashboardReportResult GetReport(UIApplication application, DashboardReportRequest request)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before running the dashboard.");
        var document = uiDocument.Document;

        IEnumerable<Element> elements;
        if (request.SelectedOnly)
        {
            var selectedIds = uiDocument.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                throw new InvalidOperationException("Select one or more Planting or Floor instances first, or switch the dashboard scope to the whole model.");
            }

            var categoryFilter = new ElementMulticategoryFilter(SupportedCategories);
            elements = selectedIds
                .Select(document.GetElement)
                .Where(element => element is not null && categoryFilter.PassesFilter(document, element.Id))
                .Cast<Element>();
        }
        else
        {
            var categoryFilter = new ElementMulticategoryFilter(SupportedCategories);
            elements = new FilteredElementCollector(document)
                .WherePasses(categoryFilter)
                .WhereElementIsNotElementType()
                .ToElements();
        }

        var trees = new List<DashboardTreeItem>();
        var floors = new List<DashboardFloorItem>();
        foreach (var element in elements)
        {
            if (element.Category?.BuiltInCategory == BuiltInCategory.OST_Planting)
            {
                trees.Add(CreateTreeItem(document, element));
            }
            else if (element.Category?.BuiltInCategory == BuiltInCategory.OST_Floors)
            {
                floors.Add(CreateFloorItem(document, element));
            }
        }

        var unitPreference = RevitModelScanner.GetPreferredUnitSystemCode(document);
        var currencyPreference = RevitModelScanner.GetPreferredCurrencyCode(document);

        return new DashboardReportResult(document.Title, unitPreference, currencyPreference, trees, floors);
    }

    private static DashboardTreeItem CreateTreeItem(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;

        double GetNumber(string name) => GetDoubleParameter(element, name);
        double GetVolumeCubicMeters(string name)
        {
            var parameter = element.LookupParameter(name);
            return parameter is { HasValue: true }
                ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.CubicMeters)
                : 0d;
        }

        return new DashboardTreeItem(
            element.UniqueId,
            element.Id.Value,
            familyName,
            elementType?.Name ?? element.Name,
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_Code_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_CommonName_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_ScientificName_Text"),
            elementType is null ? null : GetNullableText(elementType, "!_S_PLT_iTreeSpecies_Type_Text"),
            GetLevelName(document, element),
            GetDesignOptionInfo(document, element),
            GetNullableText(element, "!_S_PLT_iTreeResult_Status_Text") ?? "MissingInput",
            GetNullableText(element, "!_S_PLT_iTreeResult_UnitSystem_Text") ?? "Metric",
            GetNullableText(element, "!_S_PLT_iTreeResult_CurrencyUsed_Text") ?? "USD",
            element.LookupParameter("!_S_PLT_iTreeResult_ExchangeRateUsed_Number") is { HasValue: true } rateParam ? rateParam.AsDouble() : 1d,
            GetNumber("!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_CO2SequesteredLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_CORemovedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_CORemovedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_NO2RemovedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_NO2RemovedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_O3RemovedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_O3RemovedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_PM25RemovedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_PM25RemovedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_SO2RemovedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_SO2RemovedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_CostSavedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_CostSavedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_CarbonCostSavedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_CarbonCostSavedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_StormWaterCostSavedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_StormWaterCostSavedLifetimeTotal_Number"),
            GetNumber("!_S_PLT_iTreeResult_AirPollutionCostSavedAnnual_Number"),
            GetNumber("!_S_PLT_iTreeResult_AirPollutionCostSavedLifetimeTotal_Number"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RainfallInterceptedAnnual_Volume"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RainfallInterceptedLifetimeTotal_Volume"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume"),
            GetVolumeCubicMeters("!_S_PLT_iTreeResult_RunoffAvoidedLifetimeTotal_Volume"));
    }

    private static DashboardFloorItem CreateFloorItem(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var familyName = elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty;

        var areaParameter = element.LookupParameter("Area");
        var areaSquareMeters = areaParameter is { HasValue: true }
            ? UnitUtils.ConvertFromInternalUnits(areaParameter.AsDouble(), UnitTypeId.SquareMeters)
            : 0d;

        return new DashboardFloorItem(
            element.UniqueId,
            element.Id.Value,
            familyName,
            elementType?.Name ?? element.Name,
            GetNullableText(element, "!_S_PLT_LDS_Type_Text"),
            GetLevelName(document, element),
            GetDesignOptionInfo(document, element),
            areaSquareMeters,
            GetDoubleParameter(element, "!_S_PLT_iTreeResult_CO2SequesteredAnnual_Number"),
            GetDoubleParameter(element, "!_S_PLT_iTreeResult_CostSavedAnnual_Number"),
            GetDoubleParameter(element, "!_S_PLT_LDS_OxygenProducedAnnual_Number"),
            GetDoubleParameter(element, "!_S_PLT_LDS_TotalGWP_Number"),
            GetDoubleParameter(element, "!_S_PLT_LDS_SurfaceTempReduction_Number"),
            GetDoubleParameter(element, "!_S_PLT_LDS_AirTempReduction_Number"));
    }

    /// <summary>
    /// Resolves the option's own name and its parent set's name via the option's <see cref="BuiltInParameter.OPTION_SET_ID"/>
    /// parameter — no code elsewhere in this repo enumerates actual Design Options (only the primary/
    /// non-primary boolean), so this idiom should be re-checked against a live model with non-primary
    /// options while testing.
    /// </summary>
    private static DesignOptionInfo GetDesignOptionInfo(Document document, Element element)
    {
        var designOption = element.DesignOption;
        if (designOption is null)
        {
            return new DesignOptionInfo(null, null, true);
        }

        // DesignOptionSet itself is not a public type in the Revit API, so the set's own Name is
        // read through the base Element rather than a cast to DesignOptionSet.
        var setParameter = designOption.get_Parameter(BuiltInParameter.OPTION_SET_ID);
        var setName = setParameter is { HasValue: true }
            ? document.GetElement(setParameter.AsElementId())?.Name
            : null;

        return new DesignOptionInfo(setName, designOption.Name, designOption.IsPrimary);
    }

    private static string? GetLevelName(Document document, Element element)
    {
        if (element.LevelId != ElementId.InvalidElementId &&
            document.GetElement(element.LevelId) is Level level)
        {
            return level.Name;
        }

        var scheduleLevel = element.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
        if (scheduleLevel is { HasValue: true } &&
            document.GetElement(scheduleLevel.AsElementId()) is Level scheduledLevel)
        {
            return scheduledLevel.Name;
        }

        var familyLevel = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (familyLevel is { HasValue: true } &&
            document.GetElement(familyLevel.AsElementId()) is Level familyPlacedLevel)
        {
            return familyPlacedLevel.Name;
        }

        return null;
    }

    private static string? GetNullableText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsString() : null;
    }

    private static double GetDoubleParameter(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsDouble() : 0d;
    }
}
