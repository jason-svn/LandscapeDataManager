using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

internal static class RevitModelScanner
{
    private const string PreferredUnitSystemParameter =
        "!_S_PLANTING_iTreeUnits_PreferredSystem_Text";

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
            .GroupBy(row => new
            {
                row.Category,
                row.TypeName,
                row.TypeId,
                row.CalculationType,
                row.IsAreaBased,
                row.FamilyName
            })
            .Select(group => new ModelScanItem(
                group.Key.Category,
                group.Key.TypeName,
                group.Key.TypeId,
                group.Count(),
                Math.Round(group.Sum(row => row.AreaSquareMetres), 2),
                group.Key.CalculationType,
                group.Key.IsAreaBased,
                group.Key.FamilyName))
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

        var unitPreference = GetPreferredUnitSystem(document);
        return new ParameterCatalogResult(
            document.Title,
            parameters,
            unitPreference.System,
            unitPreference.Source,
            unitPreference.Warning);
    }

    public static ITreeInputScanResult GetITreeInputs(
        UIApplication application,
        ITreeInputOptions options)
    {
        var uiDocument = application.ActiveUIDocument
                         ?? throw new InvalidOperationException("Open a Revit project before exporting i-Tree data.");
        var document = uiDocument.Document;

        IEnumerable<ElementType> types;
        if (options.SelectedOnly)
        {
            var selectedIds = uiDocument.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Select one or more planting instances or types, or change the i-Tree scope to all planting types.");
            }

            types = selectedIds
                .Select(document.GetElement)
                .Where(element => element is not null)
                .Select(element => element as ElementType ?? document.GetElement(element!.GetTypeId()) as ElementType)
                .Where(elementType => elementType is not null &&
                                      elementType.Category?.BuiltInCategory == BuiltInCategory.OST_Planting)
                .Cast<ElementType>();
        }
        else
        {
            types = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Planting)
                .WhereElementIsElementType()
                .Cast<ElementType>();
        }

        var distinctTypes = types
            .DistinctBy(elementType => elementType.Id.Value)
            .OrderBy(GetFamilyName)
            .ThenBy(elementType => elementType.Name)
            .ToList();
        var items = new List<ITreeRevitInput>();
        var skipped = 0;

        foreach (var elementType in distinctTypes)
        {
            var speciesCode = GetParameterText(elementType, "Species_Code")?.Trim();
            if (string.IsNullOrWhiteSpace(speciesCode))
            {
                skipped++;
                continue;
            }

            items.Add(new ITreeRevitInput(
                speciesCode,
                GetParameterText(elementType, "Common_Name")?.Trim() ?? string.Empty,
                GetParameterText(elementType, "Scientific_Name")?.Trim() ?? string.Empty,
                GetFamilyName(elementType),
                elementType.Name,
                elementType.Id.Value,
                GetFirstParameterText(elementType, "Tree_Condition", "Condition")?.Trim() ?? "excellent",
                GetParameterNumber(elementType, "Diameter_in", 10d),
                GetParameterNumber(elementType, "Latitude", 51.4545d),
                GetParameterNumber(elementType, "Longitude", -2.5879d),
                Math.Max(1, (int)Math.Round(GetParameterNumber(elementType, "Years", 20d))),
                Math.Max(0, (int)Math.Round(GetParameterNumber(elementType, "CrownExposure", 5d)))));
        }

        return new ITreeInputScanResult(document.Title, items, skipped);
    }

    private const string SourceRecordIdParameter = "!_S_PLANTING_DataSync_SourceRecordId_Text";

    /// <summary>
    /// Scans Planting instances by stable identity only (Revit <see cref="Element.UniqueId"/> and
    /// whatever external record ID a previous sync wrote back) — never by display name — so
    /// callers can match source rows to Revit elements safely.
    /// </summary>
    public static PlantingInstanceScanResult ScanPlantingInstances(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before scanning planting instances.");

        var items = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Planting)
            .WhereElementIsNotElementType()
            .ToElements()
            .Select(element =>
            {
                var elementType = document.GetElement(element.GetTypeId()) as ElementType;
                return new PlantingInstanceScanItem(
                    element.UniqueId,
                    element.Id.Value,
                    elementType is not null ? GetFamilyName(elementType) : string.Empty,
                    elementType?.Name ?? element.Name,
                    elementType?.Id.Value ?? element.GetTypeId().Value,
                    GetParameterText(element, SourceRecordIdParameter)?.Trim() ?? string.Empty);
            })
            .OrderBy(item => item.FamilyName)
            .ThenBy(item => item.TypeName)
            .ToList();

        return new PlantingInstanceScanResult(document.Title, items);
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
            calculationType,
            element is Floor,
            elementType is not null ? GetFamilyName(elementType) : string.Empty);
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

    private static string GetFamilyName(ElementType elementType) =>
        elementType is FamilySymbol familySymbol
            ? familySymbol.FamilyName
            : elementType.FamilyName;

    private static string? GetParameterText(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue)
        {
            return null;
        }

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
            StorageType.Double => parameter.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => parameter.AsValueString()
        };
    }

    private static string? GetFirstParameterText(Element element, params string[] names) =>
        names.Select(name => GetParameterText(element, name))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static double GetParameterNumber(Element element, string name, double fallback)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue)
        {
            return fallback;
        }

        if (parameter.StorageType == StorageType.Double)
        {
            return parameter.AsDouble();
        }

        if (parameter.StorageType == StorageType.Integer)
        {
            return parameter.AsInteger();
        }

        return double.TryParse(
            parameter.AsString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;
    }

    private static UnitSystemPreference GetPreferredUnitSystem(Document document)
    {
        var parameter = document.ProjectInformation.LookupParameter(PreferredUnitSystemParameter);
        var configuredValue = parameter?.StorageType == StorageType.String
            ? parameter.AsString()?.Trim()
            : null;
        if (string.Equals(configuredValue, "Metric", StringComparison.OrdinalIgnoreCase))
        {
            return new UnitSystemPreference("Metric", "Project Information", null);
        }

        if (string.Equals(configuredValue, "Imperial", StringComparison.OrdinalIgnoreCase))
        {
            return new UnitSystemPreference("Imperial", "Project Information", null);
        }

        var displayUnit = document.GetUnits()
            .GetFormatOptions(SpecTypeId.Length)
            .GetUnitTypeId();
        var fallback = displayUnit == UnitTypeId.Feet ||
                       displayUnit == UnitTypeId.FeetFractionalInches ||
                       displayUnit == UnitTypeId.FractionalInches ||
                       displayUnit == UnitTypeId.Inches
            ? "Imperial"
            : "Metric";
        var warning = parameter is null
            ? $"Project Information parameter '{PreferredUnitSystemParameter}' is missing. " +
              $"Using the Revit length display units ({fallback}) for this preview."
            : $"Project Information parameter '{PreferredUnitSystemParameter}' must be Metric or Imperial. " +
              $"Its current value is '{configuredValue ?? string.Empty}'; using the Revit length display units ({fallback}) for this preview.";
        return new UnitSystemPreference(fallback, "Revit project units fallback", warning);
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
        string? CalculationType,
        bool IsAreaBased,
        string FamilyName);

    private sealed record ParameterSource(
        string Name,
        string Scope,
        string StorageType,
        string DataTypeId,
        bool IsWritable,
        string Category);

    private sealed record UnitSystemPreference(
        string System,
        string Source,
        string? Warning);
}
