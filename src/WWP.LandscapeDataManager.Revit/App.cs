using System.IO;
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
    internal static ToolProcessLauncher? ParametersLauncher { get; private set; }
    internal static ToolProcessLauncher? ImporterLauncher { get; private set; }
    internal static ToolProcessLauncher? ITreeDownloaderLauncher { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        Dispatcher = new RevitExternalEventDispatcher();
        PipeServer = new RevitPipeServer(Dispatcher);
        CompanionLauncher = new CompanionLauncher(PipeServer.PipeName);
        ParametersLauncher = new ToolProcessLauncher(
            Path.Combine("Parameters", "WWP.LandscapeDataManager.Parameters.exe"),
            PipeServer.PipeName);
        ImporterLauncher = new ToolProcessLauncher(
            Path.Combine("Importer", "WWP.LandscapeDataManager.Importer.exe"),
            PipeServer.PipeName);
        ITreeDownloaderLauncher = new ToolProcessLauncher(
            Path.Combine("ITreeDownloader", "WWP.LandscapeDataManager.ITreeDownloader.exe"),
            PipeServer.PipeName);
        PipeServer.Start();

        var panel = GetOrCreatePanel(application);
        AddWorkflowButton<SetupParametersCommand>(
            panel,
            "LIMSetupPlantingParameters",
            "Project\nSetup",
            "PS",
            "Shared Parameter Setup",
            "Assign the shared parameter file, import the required planting and i-Tree parameters, and bind them to Project Information and Planting.");
        AddWorkflowButton<ImportPlantingDataCommand>(
            panel,
            "LIMImportPlantingData",
            "Excel\nImporter",
            "EX",
            "Planting Data Import",
            "Import Excel or Airtable records, detect source units, preview changes, and write mapped Planting type and instance values.");
        AddWorkflowButton<DownloadSpeciesScheduleCommand>(
            panel,
            "LIMDownloadITreeSpecies",
            "i-Tree\nDownloader",
            "IT",
            "i-Tree Downloader",
            "Download the i-Tree species catalog, including Species_Code, common name, scientific name, and species type, and update the planting key schedule.");
        AddWorkflowButton<CalculateITreeCommand>(
            panel,
            "LIMCalculateITree",
            "i-Tree\nCalculator",
            "CAL",
            "i-Tree Calculator",
            "Validate tree inputs, calculate i-Tree benefits, write the WWP output parameters, and report elements with missing inputs.");
        AddWorkflowButton<SyncLatestCommand>(
            panel,
            "LIMSyncLatestPlantingData",
            "Refresh\nand Audit",
            "RA",
            "Refresh and Audit",
            "Compare with the last pull, reapply changed data, report updates, and identify stale or incomplete planting elements.");

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        CompanionLauncher?.Dispose();
        ParametersLauncher?.Dispose();
        ImporterLauncher?.Dispose();
        ITreeDownloaderLauncher?.Dispose();
        PipeServer?.Dispose();
        Dispatcher?.Dispose();

        CompanionLauncher = null;
        ParametersLauncher = null;
        ImporterLauncher = null;
        ITreeDownloaderLauncher = null;
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

    private static void AddWorkflowButton<TCommand>(
        RibbonPanel panel,
        string internalName,
        string displayText,
        string iconCode,
        string toolTip,
        string longDescription)
        where TCommand : IExternalCommand
    {
        var buttonData = new PushButtonData(
            internalName,
            displayText,
            Assembly.GetExecutingAssembly().Location,
            typeof(TCommand).FullName);

        if (panel.AddItem(buttonData) is not PushButton button)
        {
            return;
        }

        button.ToolTip = toolTip;
        button.LongDescription = longDescription;
        button.Image = LoadEmbeddedImage($"LIM.LandscapeData.{iconCode}.16.png");
        button.LargeImage = LoadEmbeddedImage($"LIM.LandscapeData.{iconCode}.32.png");
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
