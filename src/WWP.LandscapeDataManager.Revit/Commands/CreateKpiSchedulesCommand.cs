using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Revit.Services;

namespace WWP.LandscapeDataManager.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CreateKpiSchedulesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("Open a Revit project before creating KPI schedules.");

        using var transaction = new Transaction(document, "Create KPI Schedules");
        transaction.Start();

        KpiScheduleBuilderService.KpiScheduleResult result;
        try
        {
            result = KpiScheduleBuilderService.CreateSchedules(document);
        }
        catch (Exception exception)
        {
            transaction.RollBack();
            message = exception.Message;
            return Result.Failed;
        }

        if (!result.DesignOptionLabelBound)
        {
            transaction.RollBack();
            TaskDialog.Show(
                "KPI Schedules",
                "Run \"Import Shared Parameter\" first — the shared parameters these schedules need aren't bound to this project yet.");
            return Result.Succeeded;
        }

        transaction.Commit();

        var summary = new StringBuilder();
        if (result.Created.Count > 0)
        {
            summary.AppendLine($"Created {result.Created.Count} schedule(s):");
            foreach (var outcome in result.Created)
            {
                summary.AppendLine($"  • {outcome.ScheduleName}");
            }
        }

        if (result.AlreadyExisting.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"Already existed, left unchanged ({result.AlreadyExisting.Count}):");
            foreach (var name in result.AlreadyExisting)
            {
                summary.AppendLine($"  • {name}");
            }
        }

        if (result.SkippedEmpty.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"Skipped — no elements in that Design Option yet ({result.SkippedEmpty.Count}):");
            foreach (var name in result.SkippedEmpty)
            {
                summary.AppendLine($"  • {name}");
            }
        }

        if (result.MissingFields.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Some parameters aren't bound yet, so those columns were left out — run \"Import Shared Parameter\" to add them:");
            foreach (var field in result.MissingFields)
            {
                summary.AppendLine($"  • {field}");
            }
        }

        if (summary.Length == 0)
        {
            summary.Append("No Planting or Floor elements were found in the model.");
        }

        TaskDialog.Show("KPI Schedules", summary.ToString().TrimEnd());
        return Result.Succeeded;
    }
}
