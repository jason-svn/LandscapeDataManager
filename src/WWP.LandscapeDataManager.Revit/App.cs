using System.Reflection;
using System.Windows.Media.Imaging;
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
            "LIMShowLandscapeData",
            "LIM\nDATA",
            Assembly.GetExecutingAssembly().Location,
            typeof(ShowAppCommand).FullName);

        if (panel.AddItem(buttonData) is PushButton button)
        {
            button.ToolTip = "Open LIM- LANDSCAPE DATA.";
            button.LongDescription = "Scan Revit planting and floor types, compare Airtable or Excel records, and preview synchronization results.";
            button.Image = LoadEmbeddedImage("LIM.LandscapeData.Logo16.png");
            button.LargeImage = LoadEmbeddedImage("LIM.LandscapeData.Logo32.png");
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
        const string panelName = "LIM- LANDSCAPE DATA";

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

    private static BitmapImage LoadEmbeddedImage(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"The embedded ribbon image '{resourceName}' is missing.");
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
