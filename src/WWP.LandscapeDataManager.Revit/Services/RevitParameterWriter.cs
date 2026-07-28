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
                    ParameterValueConverter.SetValue(target.Parameter, change.Value);
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

                var value = ParameterValueConverter.ConvertValue(targets[0].Parameter, item.SourceValue, item.Conversion);
                var unchanged = targets.All(target => ParameterValueConverter.ValuesEqual(target.Parameter, value));
                var currentValues = targets
                    .Select(target => ParameterValueConverter.FormatCurrentValue(target.Parameter))
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
                    ParameterValueConverter.FormatProposedValue(targets[0].Parameter, value),
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
