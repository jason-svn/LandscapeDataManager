using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

internal static class RevitParameterWriter
{
    private static readonly BuiltInCategory[] SupportedCategories =
    [
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Floors
    ];

    public static ParameterWriteResult Preview(
        UIApplication application,
        ParameterWriteBatch batch)
    {
        var document = GetDocument(application);
        var prepared = Prepare(document, batch);
        return new ParameterWriteResult(
            document.Title,
            prepared.Select(item => item.Row).ToList(),
            0,
            0,
            false);
    }

    public static ParameterWriteResult Apply(
        UIApplication application,
        ParameterWriteBatch batch)
    {
        var document = GetDocument(application);
        var prepared = Prepare(document, batch);
        var failures = prepared
            .Where(item => item.Row.Status == "Invalid")
            .Select(item => $"{item.Row.TypeName} / {item.Row.RevitParameter}: {item.Row.Message}")
            .ToList();
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The write batch changed or is no longer valid. Build a new preview. " +
                string.Join(" | ", failures.Take(5)));
        }

        var changes = prepared.Where(item => item.Row.CanApply).ToList();
        if (changes.Count == 0)
        {
            return new ParameterWriteResult(
                document.Title,
                prepared.Select(item => item.Row).ToList(),
                0,
                0,
                true);
        }

        using var transaction = new Transaction(document, "LIM Apply Landscape Data");
        transaction.Start();
        try
        {
            foreach (var change in changes)
            {
                foreach (var target in change.Targets)
                {
                    SetValue(target.Parameter, change.Value);
                }
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

        var appliedRows = prepared.Select(item => item.Row.CanApply
            ? item.Row with { Status = "Applied", CanApply = false }
            : item.Row).ToList();
        return new ParameterWriteResult(
            document.Title,
            appliedRows,
            changes.Sum(item => item.Targets.Count),
            changes.SelectMany(item => item.Targets).Select(item => item.Element.Id.Value).Distinct().Count(),
            true);
    }

    private static IReadOnlyList<PreparedWrite> Prepare(Document document, ParameterWriteBatch batch)
    {
        var instancesByType = GetSupportedElements(document, batch.Options)
            .GroupBy(element => element.GetTypeId().Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var prepared = new List<PreparedWrite>(batch.Items.Count);

        for (var index = 0; index < batch.Items.Count; index++)
        {
            var item = batch.Items[index];
            try
            {
                var elements = ResolveElements(document, instancesByType, item);
                var targets = elements
                    .Select(element => new ParameterTarget(
                        element,
                        element.LookupParameter(item.RevitParameter)
                        ?? throw new InvalidOperationException("Parameter is missing.")))
                    .ToList();
                if (targets.Any(target => target.Parameter.IsReadOnly))
                {
                    throw new InvalidOperationException("Parameter is read-only.");
                }

                var storageTypes = targets.Select(target => target.Parameter.StorageType).Distinct().ToList();
                if (storageTypes.Count != 1)
                {
                    throw new InvalidOperationException("Parameter storage type is inconsistent across targets.");
                }

                var value = ConvertValue(targets[0].Parameter, item.SourceValue, item.Conversion);
                var unchanged = targets.All(target => ValuesEqual(target.Parameter, value));
                var currentValues = targets
                    .Select(target => FormatCurrentValue(target.Parameter))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var current = currentValues.Count == 1
                    ? currentValues[0]
                    : $"<multiple values: {currentValues.Count}>";
                var row = new ParameterWritePreviewRow(
                    index,
                    item.TypeName,
                    item.RevitParameter,
                    item.Scope,
                    current,
                    FormatProposedValue(targets[0].Parameter, value),
                    targets.Count,
                    unchanged ? "No change" : "Ready",
                    item.UnitMessage,
                    !unchanged);
                prepared.Add(new PreparedWrite(row, targets, value));
            }
            catch (Exception exception)
            {
                prepared.Add(new PreparedWrite(
                    new ParameterWritePreviewRow(
                        index,
                        item.TypeName,
                        item.RevitParameter,
                        item.Scope,
                        "—",
                        item.SourceValue,
                        0,
                        "Invalid",
                        exception.Message,
                        false),
                    [],
                    null));
            }
        }

        return prepared;
    }

    private static IReadOnlyList<Element> ResolveElements(
        Document document,
        IReadOnlyDictionary<long, List<Element>> instancesByType,
        ParameterWriteItem item)
    {
        if (string.Equals(item.Scope, "Type", StringComparison.OrdinalIgnoreCase))
        {
            var elementType = document.GetElement(new ElementId(item.TypeId)) as ElementType
                              ?? throw new InvalidOperationException("Revit type no longer exists.");
            return [elementType];
        }

        if (!string.Equals(item.Scope, "Instance", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported parameter scope '{item.Scope}'.");
        }

        return instancesByType.TryGetValue(item.TypeId, out var instances) && instances.Count > 0
            ? instances
            : throw new InvalidOperationException("No matching Revit instances were found.");
    }

    private static object ConvertValue(Parameter parameter, string source, string conversion)
    {
        return parameter.StorageType switch
        {
            StorageType.String => source,
            StorageType.Integer => ParseInteger(source),
            StorageType.Double => ConvertDouble(parameter, ParseDouble(source), conversion),
            StorageType.ElementId => throw new InvalidOperationException(
                "ElementId parameters cannot be populated from text source data."),
            _ => throw new InvalidOperationException("Unsupported parameter storage type.")
        };
    }

    private static int ParseInteger(string source)
    {
        if (bool.TryParse(source, out var boolean))
        {
            return boolean ? 1 : 0;
        }

        if (int.TryParse(source, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ||
            int.TryParse(source, NumberStyles.Integer, CultureInfo.CurrentCulture, out integer))
        {
            return integer;
        }

        throw new InvalidOperationException($"'{source}' is not a valid integer or Yes/No value.");
    }

    private static double ParseDouble(string source)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands |
                                    NumberStyles.AllowCurrencySymbol;
        if (double.TryParse(source, styles, CultureInfo.InvariantCulture, out var number) ||
            double.TryParse(source, styles, CultureInfo.CurrentCulture, out number))
        {
            return number;
        }

        throw new InvalidOperationException($"'{source}' is not a valid number.");
    }

    private static double ConvertDouble(Parameter parameter, double value, string conversion)
    {
        if (string.Equals(conversion, "Metres → Revit length", StringComparison.OrdinalIgnoreCase))
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
        }

        if (string.Equals(conversion, "Square metres → Revit area", StringComparison.OrdinalIgnoreCase))
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);
        }

        if (!string.Equals(conversion, "Auto (Revit spec)", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var dataType = parameter.Definition.GetDataType();
        if (dataType == SpecTypeId.Length)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
        }

        if (dataType == SpecTypeId.Area)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);
        }

        if (dataType == SpecTypeId.Volume)
        {
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.CubicMeters);
        }

        return value;
    }

    private static bool ValuesEqual(Parameter parameter, object? value) =>
        parameter.StorageType switch
        {
            StorageType.String => string.Equals(parameter.AsString() ?? string.Empty, (string?)value ?? string.Empty),
            StorageType.Integer => parameter.AsInteger() == (int)value!,
            StorageType.Double => Math.Abs(parameter.AsDouble() - (double)value!) < 1e-9,
            _ => false
        };

    private static string FormatCurrentValue(Parameter parameter) => parameter.StorageType switch
    {
        StorageType.String => parameter.AsString() ?? string.Empty,
        StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
        StorageType.Double => parameter.AsValueString() ?? parameter.AsDouble().ToString("G", CultureInfo.InvariantCulture),
        StorageType.ElementId => parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    private static string FormatProposedValue(Parameter parameter, object? value) => parameter.StorageType switch
    {
        StorageType.String => (string?)value ?? string.Empty,
        StorageType.Integer => ((int)value!).ToString(CultureInfo.InvariantCulture),
        StorageType.Double => FormatInternalDouble(parameter, (double)value!),
        _ => value?.ToString() ?? string.Empty
    };

    private static string FormatInternalDouble(Parameter parameter, double value)
    {
        try
        {
            return UnitFormatUtils.Format(
                parameter.Element.Document.GetUnits(),
                parameter.Definition.GetDataType(),
                value,
                false);
        }
        catch
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }
    }

    private static void SetValue(Parameter parameter, object? value)
    {
        var succeeded = parameter.StorageType switch
        {
            StorageType.String => parameter.Set((string?)value ?? string.Empty),
            StorageType.Integer => parameter.Set((int)value!),
            StorageType.Double => parameter.Set((double)value!),
            _ => false
        };
        if (!succeeded)
        {
            throw new InvalidOperationException($"Revit rejected parameter '{parameter.Definition.Name}'.");
        }
    }

    private static IEnumerable<Element> GetSupportedElements(
        Document document,
        ModelScanOptions options)
    {
        var filter = new ElementMulticategoryFilter(SupportedCategories);
        return new FilteredElementCollector(document)
            .WherePasses(filter)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(element => !options.PrimaryDesignOptionsOnly ||
                              element.DesignOption is null ||
                              element.DesignOption.IsPrimary);
    }

    private static Document GetDocument(UIApplication application) =>
        application.ActiveUIDocument?.Document
        ?? throw new InvalidOperationException("Open a Revit project before synchronizing parameters.");

    private sealed record ParameterTarget(Element Element, Parameter Parameter);
    private sealed record PreparedWrite(
        ParameterWritePreviewRow Row,
        IReadOnlyList<ParameterTarget> Targets,
        object? Value);
}
