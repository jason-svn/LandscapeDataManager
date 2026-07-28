using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Revit.Infrastructure;

namespace WWP.LandscapeDataManager.Revit.Commands;

/// <summary>Legacy path: still used by tools not yet migrated to their own standalone exe.</summary>
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

/// <summary>Current path: one standalone tool executable, started/refocused via its own launcher.</summary>
public abstract class LaunchToolCommand : IExternalCommand
{
    protected abstract ToolProcessLauncher? Launcher { get; }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Launcher?.ShowOrStart();
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
public sealed class SetupParametersCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.ParametersLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class ImportPlantingDataCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.ImporterLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class DownloadSpeciesScheduleCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.ITreeDownloaderLauncher;
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
