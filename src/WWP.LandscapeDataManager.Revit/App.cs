using System.Reflection;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Revit.Commands;
using WWP.LandscapeDataManager.Revit.Infrastructure;

namespace WWP.LandscapeDataManager.Revit;

public sealed class App : IExternalApplication
{
    internal static RevitExternalEventDispatcher? Dispatcher { get; private set; }
    internal static RevitPipeServer? PipeServer { get; private set; }
    internal static CompanionLauncher? CompanionLauncher { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        Dispatcher = new RevitExternalEventDispatcher();
        PipeServer = new RevitPipeServer(Dispatcher);
        CompanionLauncher = new CompanionLauncher(PipeServer.PipeName);
        PipeServer.Start();

        var panel = GetOrCreatePanel(application);
        var buttonData = new PushButtonData(
            "WWP.ShowLandscapeData",
            "Landscape\nData",
            Assembly.GetExecutingAssembly().Location,
            typeof(ShowAppCommand).FullName);

        if (panel.AddItem(buttonData) is PushButton button)
        {
            button.ToolTip = "Open the WinUI 3 landscape data manager.";
            button.LongDescription = "Scan Revit planting and floor types, compare Airtable records, and preview synchronization results.";
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        CompanionLauncher?.Dispose();
        PipeServer?.Dispose();
        Dispatcher?.Dispose();

        CompanionLauncher = null;
        PipeServer = null;
        Dispatcher = null;
        return Result.Succeeded;
    }

    private static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
    {
        const string tabName = "EGIS";
        const string panelName = "Landscape Data";

        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Another EGIS add-in already created the shared tab.
        }

        return application.GetRibbonPanels(tabName)
                   .FirstOrDefault(panel => panel.Name == panelName)
               ?? application.CreateRibbonPanel(tabName, panelName);
    }
}
