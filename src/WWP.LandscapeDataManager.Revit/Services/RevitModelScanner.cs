using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

internal static class RevitModelScanner
{
    private static readonly BuiltInCategory[] SupportedCategories =
    [
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Floors
    ];

    public static ModelScanResult Scan(UIApplication application, ModelScanOptions options)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before scanning.");

        var categoryFilter = new ElementMulticategoryFilter(SupportedCategories);
        var elements = new FilteredElementCollector(document)
            .WherePasses(categoryFilter)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(element => !options.PrimaryDesignOptionsOnly || IsInPrimaryDesignOption(element))
            .ToList();

        var items = elements
            .Select(element => CreateSourceRow(document, element))
            .GroupBy(row => new { row.Category, row.TypeName, row.TypeId, row.CalculationType })
            .Select(group => new ModelScanItem(
                group.Key.Category,
                group.Key.TypeName,
                group.Key.TypeId,
                group.Count(),
                Math.Round(group.Sum(row => row.AreaSquareMetres), 2),
                group.Key.CalculationType))
            .OrderBy(item => item.Category)
            .ThenBy(item => item.TypeName)
            .ToList();

        return new ModelScanResult(document.Title, items);
    }

    public static ParameterCatalogResult GetParameterCatalog(
        UIApplication application,
        ModelScanOptions options)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before reading parameters.");

        var elements = GetSupportedElements(document, options).ToList();
        var descriptors = new List<ParameterSource>();

        foreach (var element in elements)
        {
            AddParameters(descriptors, element, "Instance", element.Category?.Name ?? "Unknown");
        }

        foreach (var elementType in elements
                     .Select(element => document.GetElement(element.GetTypeId()) as ElementType)
                     .Where(elementType => elementType is not null)
                     .DistinctBy(elementType => elementType!.Id.Value))
        {
            AddParameters(
                descriptors,
                elementType!,
                "Type",
                elementType!.Category?.Name ?? "Unknown");
        }

        var parameters = descriptors
            .GroupBy(item => new
            {
                item.Name,
                item.Scope,
                item.StorageType,
                item.DataTypeId
            })
            .Select(group => new RevitParameterDescriptor(
                group.Key.Name,
                group.Key.Scope,
                group.Key.StorageType,
                group.Key.DataTypeId,
                group.Any(item => item.IsWritable),
                group.Select(item => item.Category).Distinct().Order().ToList()))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Scope)
            .ToList();

        return new ParameterCatalogResult(document.Title, parameters);
    }

    private static SourceRow CreateSourceRow(Document document, Element element)
    {
        var elementType = document.GetElement(element.GetTypeId()) as ElementType;
        var typeName = elementType?.Name ?? element.Name;
        var typeId = elementType?.Id.Value ?? element.GetTypeId().Value;
        var calculationType = GetStringParameter(element, "WWP_LDS_CalculationType")
                              ?? (elementType is null
                                  ? null
                                  : GetStringParameter(elementType, "WWP_LDS_CalculationType"));

        var area = 0d;
        if (element is Floor)
        {
            var areaParameter = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (areaParameter is not null && areaParameter.HasValue)
            {
                area = UnitUtils.ConvertFromInternalUnits(
                    areaParameter.AsDouble(),
                    UnitTypeId.SquareMeters);
            }
        }

        return new SourceRow(
            element.Category?.Name ?? "Unknown",
            typeName,
            typeId,
            area,
            calculationType);
    }

    private static IEnumerable<Element> GetSupportedElements(
        Document document,
        ModelScanOptions options)
    {
        var categoryFilter = new ElementMulticategoryFilter(SupportedCategories);
        return new FilteredElementCollector(document)
            .WherePasses(categoryFilter)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(element => !options.PrimaryDesignOptionsOnly || IsInPrimaryDesignOption(element));
    }

    private static void AddParameters(
        ICollection<ParameterSource> destination,
        Element element,
        string scope,
        string category)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            var definition = parameter.Definition;
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name))
            {
                continue;
            }

            string dataTypeId;
            try
            {
                dataTypeId = definition.GetDataType().TypeId;
            }
            catch
            {
                dataTypeId = string.Empty;
            }

            destination.Add(new ParameterSource(
                definition.Name,
                scope,
                parameter.StorageType.ToString(),
                dataTypeId,
                !parameter.IsReadOnly,
                category));
        }
    }

    private static string? GetStringParameter(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return parameter?.StorageType == StorageType.String
            ? parameter.AsString()
            : null;
    }

    private static bool IsInPrimaryDesignOption(Element element)
    {
        var designOption = element.DesignOption;
        return designOption is null || designOption.IsPrimary;
    }

    private sealed record SourceRow(
        string Category,
        string TypeName,
        long TypeId,
        double AreaSquareMetres,
        string? CalculationType);

    private sealed record ParameterSource(
        string Name,
        string Scope,
        string StorageType,
        string DataTypeId,
        bool IsWritable,
        string Category);
}
