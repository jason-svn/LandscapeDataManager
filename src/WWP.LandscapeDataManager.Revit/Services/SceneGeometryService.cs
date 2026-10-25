using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// The 3D view's geometry for the JSON export: every tree's trunk base and drawn size with what the
/// planting family grows it from, each planting type's growth, and every floor's top face
/// triangulated with its holes cut out. Read-only, and every design option and phase: the export
/// picks each scenario's elements itself.
/// </summary>
internal static class SceneGeometryService
{
    private const string TrunkHeightParameter = "!_S_PLT_TreeTrunk_Height";
    private const string TrunkDiameterParameter = "!_S_PLT_TreeTrunk_Diameter";
    private const string CrownHeightParameter = "!_S_PLT_TreeFoliage_Height";
    private const string CrownWidthParameter = "!_S_PLT_TreeFoliage_Width";
    private const string AgeAtPlantingParameter = "!_S_PLT_TreeGrowth_AgeAtPlanting_Number";
    private const string ProjectedYearsParameter = "!_S_PLT_TreeGrowth_ProjectedYears_Number";
    private const string ITreeHeightParameter = "!_S_PLT_iTreeResult_TreeHeight_Length";
    private const string ITreeCrownWidthParameter = "!_S_PLT_iTreeResult_CrownWidth_Length";
    private const string MaxHeightParameter = "!_S_PLT_LDS_MaxHeight_Length";
    private const string MaxWidthParameter = "!_S_PLT_LDS_MaxWidth_Length";
    private const string HeightRateParameter = "!_S_PLT_GrowthRatio_HeightbyYear_Number";
    private const string WidthRateParameter = "!_S_PLT_GrowthRatio_WidthbyYear_Number";
    private const string TrunkRateParameter = "!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number";
    private const string MatureAgeParameter = "Mature Age";

    /// <summary>Revit's tessellation detail for curved floor edges, 0 (coarse) to 1 (fine).</summary>
    private const double FloorEdgeDetail = 0.5;

    public static SceneGeometryResult Get(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before exporting the 3D view.");

        var trees = new List<SceneTreeItem>();
        var types = new List<TreeGrowthType>();
        var typesSeen = new HashSet<long>();
        foreach (var instance in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_Planting)
                     .OfClass(typeof(FamilyInstance))
                     .Cast<FamilyInstance>())
        {
            if (instance.Location is not LocationPoint location)
            {
                continue;
            }

            trees.Add(ToTree(instance, location.Point));
            if (typesSeen.Add(instance.Symbol.Id.Value) && ToGrowthType(instance.Symbol) is { } growth)
            {
                types.Add(growth);
            }
        }

        var floors = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Floors)
            .OfClass(typeof(Floor))
            .Cast<Floor>()
            .Select(ToFloor)
            .OfType<SceneFloorItem>()
            .ToList();

        return new SceneGeometryResult(document.Title, trees, types, floors);
    }

    private static SceneTreeItem ToTree(FamilyInstance instance, XYZ point)
    {
        var trunkHeight = Length(instance, TrunkHeightParameter);
        var crownHeight = Length(instance, CrownHeightParameter);
        var crownWidth = Length(instance, CrownWidthParameter);
        var trunkDiameter = Length(instance, TrunkDiameterParameter);
        if (trunkHeight + crownHeight <= 0 || crownWidth <= 0)
        {
            (trunkHeight, crownHeight, crownWidth, trunkDiameter) = FromBoundingBox(instance, point);
        }

        return new SceneTreeItem(
            instance.UniqueId,
            instance.Symbol.FamilyName,
            instance.Symbol.Name,
            Round(ToMeters(point.X)),
            Round(ToMeters(point.Y)),
            Round(ToMeters(point.Z)),
            Round(trunkHeight),
            Round(crownHeight),
            Round(crownWidth / 2d),
            Round(Math.Max(trunkDiameter, 0.05) / 2d),
            Number(instance, AgeAtPlantingParameter) ?? 0,
            Number(instance, ProjectedYearsParameter) is { } projected ? (int)Math.Round(projected) : null,
            Length(instance, ITreeHeightParameter),
            Length(instance, ITreeCrownWidthParameter));
    }

    /// <summary>A family without LIM's size parameters: its bounding box above the trunk base, a third trunk and two thirds crown.</summary>
    private static (double TrunkHeight, double CrownHeight, double CrownWidth, double TrunkDiameter) FromBoundingBox(FamilyInstance instance, XYZ point)
    {
        if (instance.get_BoundingBox(null) is not { } box)
        {
            return (0, 0, 0, 0);
        }

        var height = ToMeters(box.Max.Z - point.Z);
        var width = ToMeters(((box.Max.X - box.Min.X) + (box.Max.Y - box.Min.Y)) / 2d);
        return (height / 3d, height * 2d / 3d, width, 0.2);
    }

    /// <summary>Null for a type without the planting family's growth parameters.</summary>
    private static TreeGrowthType? ToGrowthType(FamilySymbol symbol)
    {
        if (symbol.LookupParameter(HeightRateParameter) is null && symbol.LookupParameter(MaxHeightParameter) is null)
        {
            return null;
        }

        return new TreeGrowthType(
            symbol.FamilyName,
            symbol.Name,
            Length(symbol, MaxHeightParameter),
            Length(symbol, MaxWidthParameter),
            Number(symbol, HeightRateParameter) ?? 0,
            Number(symbol, WidthRateParameter) ?? 0,
            Number(symbol, TrunkRateParameter) ?? 0,
            (int)Math.Round(Number(symbol, MatureAgeParameter) ?? 0));
    }

    private static SceneFloorItem? ToFloor(Floor floor)
    {
        var positions = new List<double>();
        var indices = new List<int>();
        var holes = 0;
        try
        {
            foreach (var reference in HostObjectUtils.GetTopFaces(floor))
            {
                if (floor.GetGeometryObjectFromReference(reference) is not Face face)
                {
                    continue;
                }

                holes += Math.Max(0, face.EdgeLoops.Size - 1);
                var mesh = face.Triangulate(FloorEdgeDetail);
                var first = positions.Count / 3;
                foreach (var vertex in mesh.Vertices)
                {
                    positions.Add(Round(ToMeters(vertex.X)));
                    positions.Add(Round(ToMeters(vertex.Y)));
                    positions.Add(Round(ToMeters(vertex.Z)));
                }

                for (var index = 0; index < mesh.NumTriangles; index++)
                {
                    var triangle = mesh.get_Triangle(index);
                    indices.Add(first + (int)triangle.get_Index(0));
                    indices.Add(first + (int)triangle.get_Index(1));
                    indices.Add(first + (int)triangle.get_Index(2));
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // A floor whose top face Revit can't give back is left out of the 3D view.
            return null;
        }

        return indices.Count == 0 ? null : new SceneFloorItem(floor.UniqueId, positions, indices, holes);
    }

    /// <summary>A length parameter in metres; 0 when it's missing or empty.</summary>
    private static double Length(Element element, string name) =>
        ElementParameters.LookupOnInstanceOrType(element, name) is { HasValue: true, StorageType: StorageType.Double } parameter
            ? ToMeters(parameter.AsDouble())
            : 0d;

    private static double? Number(Element element, string name) =>
        ElementParameters.LookupOnInstanceOrType(element, name) switch
        {
            { HasValue: true, StorageType: StorageType.Double } parameter => parameter.AsDouble(),
            { HasValue: true, StorageType: StorageType.Integer } parameter => parameter.AsInteger(),
            _ => null
        };

    private static double ToMeters(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);

    private static double Round(double meters) => Math.Round(meters, 3);
}
