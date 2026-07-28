using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Writes per-instance parameter values targeted by Revit <see cref="Element.UniqueId"/> —
/// the instance-scoped counterpart to <see cref="RevitParameterWriter"/>'s type-scoped writes,
/// used where every instance can legitimately carry a different source value (tree year,
/// condition, crown exposure, project-specific overrides).
/// </summary>
internal static class InstanceParameterWriter
{
    public static InstanceParameterWriteResult Preview(UIApplication application, InstanceParameterWriteBatch batch)
    {
        var document = GetDocument(application);
        var prepared = Prepare(document, batch);
        return new InstanceParameterWriteResult(
            document.Title,
            prepared.Select(item => item.Row).ToList(),
            0,
            0,
            false);
    }

    public static InstanceParameterWriteResult Apply(UIApplication application, InstanceParameterWriteBatch batch)
    {
        var document = GetDocument(application);
        var prepared = Prepare(document, batch);
        var failures = prepared
            .Where(item => item.Row.Status == "Invalid")
            .Select(item => $"{item.Row.UniqueId} / {item.Row.RevitParameter}: {item.Row.Message}")
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
            return new InstanceParameterWriteResult(document.Title, prepared.Select(item => item.Row).ToList(), 0, 0, true);
        }

        using var transaction = new Transaction(document, "LIM Apply Planting Instance Data");
        transaction.Start();
        try
        {
            foreach (var change in changes)
            {
                ParameterValueConverter.SetValue(change.Target!.Parameter, change.Value);
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
        return new InstanceParameterWriteResult(
            document.Title,
            appliedRows,
            changes.Count,
            changes.Select(item => item.Target!.Element.Id.Value).Distinct().Count(),
            true);
    }

    private static IReadOnlyList<PreparedWrite> Prepare(Document document, InstanceParameterWriteBatch batch)
    {
        var prepared = new List<PreparedWrite>(batch.Items.Count);

        for (var index = 0; index < batch.Items.Count; index++)
        {
            var item = batch.Items[index];
            try
            {
                var element = document.GetElement(item.UniqueId)
                              ?? throw new InvalidOperationException("The Revit element no longer exists.");
                var parameter = element.LookupParameter(item.RevitParameter)
                                 ?? throw new InvalidOperationException("Parameter is missing.");
                if (parameter.IsReadOnly)
                {
                    throw new InvalidOperationException("Parameter is read-only.");
                }

                var value = ParameterValueConverter.ConvertValue(parameter, item.SourceValue, item.Conversion);
                var unchanged = ParameterValueConverter.ValuesEqual(parameter, value);
                var row = new InstanceParameterWritePreviewRow(
                    index,
                    item.UniqueId,
                    item.RevitParameter,
                    ParameterValueConverter.FormatCurrentValue(parameter),
                    ParameterValueConverter.FormatProposedValue(parameter, value),
                    unchanged ? "No change" : "Ready",
                    item.UnitMessage,
                    !unchanged);
                prepared.Add(new PreparedWrite(row, new ParameterTarget(element, parameter), value));
            }
            catch (Exception exception)
            {
                prepared.Add(new PreparedWrite(
                    new InstanceParameterWritePreviewRow(
                        index,
                        item.UniqueId,
                        item.RevitParameter,
                        "—",
                        item.SourceValue,
                        "Invalid",
                        exception.Message,
                        false),
                    null,
                    null));
            }
        }

        return prepared;
    }

    private static Document GetDocument(UIApplication application) =>
        application.ActiveUIDocument?.Document
        ?? throw new InvalidOperationException("Open a Revit project before synchronizing planting instances.");

    private sealed record ParameterTarget(Element Element, Parameter Parameter);
    private sealed record PreparedWrite(
        InstanceParameterWritePreviewRow Row,
        ParameterTarget? Target,
        object? Value);
}
