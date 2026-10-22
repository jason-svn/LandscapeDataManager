using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Reads every Planting instance's raw i-Tree inputs and whatever tracking values a previous
/// calculation stored — read-only, no status classification (see
/// Shared.Services.PlantingInstanceStatusEvaluator). Distinguishes "never set" from "set to
/// zero" throughout, since a crown exposure of 0 (fully shaded) is a legitimate value.
/// </summary>
internal static class PlantingInstanceValidationScanner
{
    private const string SpeciesCodeParameter = "!_S_PLT_iTreeSpecies_Code_Text";
    private const string YearsParameter = "!_S_PLT_TreeGrowth_Years_Number";
    private const string ConditionParameter = "!_S_PLT_iTreeInput_Condition_Text";
    private const string CrownExposureParameter = "!_S_PLT_iTreeInput_CrownExposure_Number";
    private const string DbhParameter = "!_S_PLT_TreeTrunk_DBH_Diameter";
    private const string LatitudeParameter = "!_S_PLT_iTreeLocation_Latitude_Number";
    private const string LongitudeParameter = "!_S_PLT_iTreeLocation_Longitude_Number";
    private const string StatusParameter = "!_S_PLT_iTreeResult_Status_Text";
    private const string DetailsParameter = "!_S_PLT_iTreeResult_Details_Text";
    private const string LastUpdatedParameter = "!_S_PLT_iTreeResult_LastUpdated_Text";
    private const string EngineVersionParameter = "!_S_PLT_iTreeResult_EngineVersion_Text";
    private const string InputSignatureParameter = "!_S_PLT_iTreeResult_InputSignature_Text";
    private const string AgeAtPlantingParameter = "!_S_PLT_TreeGrowth_AgeAtPlanting_Number";

    /// <summary>The planting family's own type parameter (not shared): the age the tree reaches its mature size, 0 without one.</summary>
    private const string MatureAgeParameter = "Mature Age";

    public static ValidatePlantingInstancesResult Scan(UIApplication application, bool selectedOnly = false)
    {
        var uiDocument = application.ActiveUIDocument
                        ?? throw new InvalidOperationException("Open a Revit project before validating planting instances.");
        var document = uiDocument.Document;

        var (latitude, longitude) = ReadLocation(document);

        IEnumerable<Element> instances;
        if (selectedOnly)
        {
            var selectedIds = uiDocument.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Select one or more planting instances in Revit, or switch the scope to all planting instances.");
            }

            instances = selectedIds
                .Select(document.GetElement)
                .Where(element => element is not null and not ElementType &&
                                   element.Category?.BuiltInCategory == BuiltInCategory.OST_Planting);
        }
        else
        {
            instances = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Planting)
                .WhereElementIsNotElementType()
                .ToElements();
        }

        // Bring each growth-year tree's modelled age up to date first: the family regenerates its
        // DBH from that age, and the DBH read below is what i-Tree is sent.
        var instanceList = instances.ToList();
        var ageSync = TreeAgeSyncService.Apply(document, instanceList);

        var items = instanceList
            .Select(element => ReadItem(document, element, latitude, longitude))
            .OrderBy(item => item.FamilyName)
            .ThenBy(item => item.TypeName)
            .ToList();

        return new ValidatePlantingInstancesResult(document.Title, items, ageSync);
    }

    /// <summary>The project's i-Tree location (Project Information), shared by every tree.</summary>
    internal static (double? Latitude, double? Longitude) ReadLocation(Document document) =>
        (GetNullableDouble(document.ProjectInformation, LatitudeParameter), GetNullableDouble(document.ProjectInformation, LongitudeParameter));

    /// <summary>
    /// One tree's i-Tree inputs and what its last calculation stored — read only, no age sync — so the
    /// Dashboard can tell a current result from one whose inputs changed since (Stale), as Tree Calculator does.
    /// </summary>
    internal static PlantingInstanceValidationItem ReadItem(Document document, Element element, double? latitude, double? longitude)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var speciesCode = elementType is not null ? GetNullableText(elementType, SpeciesCodeParameter) : null;
        return new PlantingInstanceValidationItem(
            element.UniqueId,
            element.Id.Value,
            elementType is FamilySymbol symbol ? symbol.FamilyName : elementType?.FamilyName ?? string.Empty,
            elementType?.Name ?? element.Name,
            speciesCode,
            GetNullableInt(element, YearsParameter),
            GetNullableText(element, ConditionParameter),
            GetNullableInt(element, CrownExposureParameter),
            ConvertToInches(GetNullableDouble(element, DbhParameter)),
            latitude,
            longitude,
            GetNullableText(element, StatusParameter) ?? string.Empty,
            GetNullableText(element, DetailsParameter) ?? string.Empty,
            GetNullableText(element, LastUpdatedParameter) ?? string.Empty,
            GetNullableText(element, EngineVersionParameter) ?? string.Empty,
            GetNullableText(element, InputSignatureParameter) ?? string.Empty,
            document.IsWorkshared ? GrowthYearNames.FromName(document.GetWorksetTable().GetWorkset(element.WorksetId)?.Name) : null,
            element.GroupId != ElementId.InvalidElementId,
            GetNullableInt(element, AgeAtPlantingParameter),
            elementType is not null && GetNullableInt(elementType, MatureAgeParameter) is > 0 and var matureAge ? matureAge : null);
    }

    private static double? ConvertToInches(double? internalLengthValue) =>
        internalLengthValue is null ? null : UnitUtils.ConvertFromInternalUnits(internalLengthValue.Value, UnitTypeId.Inches);

    private static string? GetNullableText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsString() : null;
    }

    private static int? GetNullableInt(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsInteger() : null;
    }

    private static double? GetNullableDouble(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter is { HasValue: true } ? parameter.AsDouble() : null;
    }
}
