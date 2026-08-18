using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Revit.Infrastructure;

namespace WWP.LandscapeDataManager.Revit.Commands;

/// <summary>One standalone tool executable, started/refocused via its own launcher.</summary>
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
public sealed class CalculateITreeCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.ITreeCalculatorLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class SyncLatestCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.SyncAuditLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class SearchTreesCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.TreeSearcherLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class CalculateFloorCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.FloorCalculatorLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class FindLocationCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.LocationFinderLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class RunHealthCheckCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.HealthCheckLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class OpenDashboardCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.DashboardLauncher;
}

[Transaction(TransactionMode.Manual)]
public sealed class OpenSettingsCommand : LaunchToolCommand
{
    protected override ToolProcessLauncher? Launcher => App.SettingsLauncher;
}
