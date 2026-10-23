using Autodesk.Revit.DB;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Project-wide setup problems the Dashboard reports beside its results: LIM's shared parameters
/// never bound to Planting or Floors, and tree types whose sizes were stored as mm. Each one would
/// otherwise make every tree or floor look wrong for the same root cause.
/// </summary>
internal static class ModelSetupChecks
{
    private const string PlantingSpeciesCodeParameter = "!_S_PLT_iTreeSpecies_Code_Text";
    private const string FloorTypeParameter = "!_S_PLT_LDS_Type_Text";

    public static IReadOnlyList<string> Warnings(Document document)
    {
        var warnings = new List<string>();
        if (!IsParameterBound(document, PlantingSpeciesCodeParameter))
        {
            warnings.Add("LIM's shared parameters aren't set up for Planting yet: run Import Shared Parameter, then Load parameters and Apply.");
        }

        if (!IsParameterBound(document, FloorTypeParameter))
        {
            warnings.Add("LIM's shared parameters aren't set up for Floors yet: run Import Shared Parameter, then Load parameters and Apply.");
        }

        if (TinyTreeTypesWarning(document) is { } tinyTrees)
        {
            warnings.Add(tinyTrees);
        }

        return warnings;
    }

    private static bool IsParameterBound(Document document, string parameterName)
    {
        var iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is InternalDefinition definition &&
                string.Equals(definition.Name, parameterName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tree types (those with a trunk growth rate) whose max height or width is under 1 m: a WWP sheet
    /// size, which is in metres, stored as mm (20 m read as 20 mm). The planting family ignores such a
    /// size, so the tree gets no Mature Age and no size cap. Null when there are none.
    /// </summary>
    private static string? TinyTreeTypesWarning(Document document)
    {
        static double? Metres(Element type, string name) =>
            type.LookupParameter(name) is { HasValue: true, StorageType: StorageType.Double } parameter && parameter.AsDouble() > 0
                ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Meters)
                : null;

        var tiny = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsElementType()
            .Where(type => type.LookupParameter("!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number") is not null)
            .Select(type => (type.Name, Height: Metres(type, "!_S_PLT_LDS_MaxHeight_Length"), Width: Metres(type, "!_S_PLT_LDS_MaxWidth_Length")))
            .Where(type => type.Height is < 1 || type.Width is < 1)
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tiny.Count == 0)
        {
            return null;
        }

        var examples = string.Join(", ", tiny.Take(5).Select(type => $"{type.Name} {type.Height:0.###} × {type.Width:0.###} m"));
        return $"{tiny.Count:N0} tree type(s) have a max height or width under 1 m ({examples}{(tiny.Count > 5 ? ", …" : string.Empty)}). " +
               "The WWP sheet's sizes are in metres, so these were probably stored as mm; the planting family ignores them, so these trees get no Mature Age. " +
               "Import the types again with the Importer, or fix the sizes on the types.";
    }
}
