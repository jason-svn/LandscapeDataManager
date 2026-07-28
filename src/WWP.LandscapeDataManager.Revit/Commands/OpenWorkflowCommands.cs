using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace WWP.LandscapeDataManager.Revit.Commands;

public abstract class OpenWorkflowCommand : IExternalCommand
{
    protected abstract string Workflow { get; }

    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        try
        {
            App.CompanionLauncher?.ShowOrStart(Workflow);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            message = exception.Message;
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class SetupParametersCommand : OpenWorkflowCommand
{
    protected override string Workflow => "parameters";
}

[Transaction(TransactionMode.Manual)]
public sealed class ImportPlantingDataCommand : OpenWorkflowCommand
{
    protected override string Workflow => "import";
}

[Transaction(TransactionMode.Manual)]
public sealed class DownloadSpeciesScheduleCommand : OpenWorkflowCommand
{
    protected override string Workflow => "species";
}

[Transaction(TransactionMode.Manual)]
public sealed class CalculateITreeCommand : OpenWorkflowCommand
{
    protected override string Workflow => "calculate";
}

[Transaction(TransactionMode.Manual)]
public sealed class SyncLatestCommand : OpenWorkflowCommand
{
    protected override string Workflow => "sync";
}
