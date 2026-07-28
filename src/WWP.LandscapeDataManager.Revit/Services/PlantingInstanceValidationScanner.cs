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
    private const string SpeciesCodeParameter = "!_S_PLANTING_iTreeSpecies_Code_Text";
    private const string YearsParameter = "!_S_PLANTING_TreeGrowth_Years_Number";
    private const string ConditionParameter = "!_S_PLANTING_iTreeInput_Condition_Text";
    private const string CrownExposureParameter = "!_S_PLANTING_iTreeInput_CrownExposure_Number";
    private const string DbhParameter = "!_S_PLANTING_TreeTrunk_DBH_Diameter";
    private const string LatitudeParameter = "!_S_PLANTING_iTreeLocation_Latitude_Number";
    private const string LongitudeParameter = "!_S_PLANTING_iTreeLocation_Longitude_Number";
    private const string StatusParameter = "!_S_PLANTING_iTreeResult_Status_Text";
    private const string DetailsParameter = "!_S_PLANTING_iTreeResult_Details_Text";
    private const string LastUpdatedParameter = "!_S_PLANTING_iTreeResult_LastUpdated_Text";
    private const string EngineVersionParameter = "!_S_PLANTING_iTreeResult_EngineVersion_Text";
    private const string InputSignatureParameter = "!_S_PLANTING_iTreeResult_InputSignature_Text";

    public static ValidatePlantingInstancesResult Scan(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before validating planting instances.");

        var projectInfo = document.ProjectInformation;
        var latitude = GetNullableDouble(projectInfo, LatitudeParameter);
        var longitude = GetNullableDouble(projectInfo, LongitudeParameter);

        var items = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsNotElementType()
            .ToElements()
            .Select(element =>
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
                    GetNullableText(element, InputSignatureParameter) ?? string.Empty);
            })
            .OrderBy(item => item.FamilyName)
            .ThenBy(item => item.TypeName)
            .ToList();

        return new ValidatePlantingInstancesResult(document.Title, items);
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
