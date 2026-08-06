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
    internal static ToolProcessLauncher? ITreeCalculatorLauncher { get; private set; }
    internal static ToolProcessLauncher? SyncAuditLauncher { get; private set; }
    internal static ToolProcessLauncher? TreeSearcherLauncher { get; private set; }
    internal static ToolProcessLauncher? LocationFinderLauncher { get; private set; }
    internal static ToolProcessLauncher? FloorCalculatorLauncher { get; private set; }
    internal static ToolProcessLauncher? HealthCheckLauncher { get; private set; }

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
        ITreeCalculatorLauncher = new ToolProcessLauncher(
            Path.Combine("ITreeCalculator", "WWP.LandscapeDataManager.ITreeCalculator.exe"),
            PipeServer.PipeName);
        SyncAuditLauncher = new ToolProcessLauncher(
            Path.Combine("SyncAudit", "WWP.LandscapeDataManager.SyncAudit.exe"),
            PipeServer.PipeName);
        TreeSearcherLauncher = new ToolProcessLauncher(
            Path.Combine("TreeSearcher", "WWP.LandscapeDataManager.TreeSearcher.exe"),
            PipeServer.PipeName);
        LocationFinderLauncher = new ToolProcessLauncher(
            Path.Combine("LocationFinder", "WWP.LandscapeDataManager.LocationFinder.exe"),
            PipeServer.PipeName);
        FloorCalculatorLauncher = new ToolProcessLauncher(
            Path.Combine("FloorCalculator", "WWP.LandscapeDataManager.FloorCalculator.exe"),
            PipeServer.PipeName);
        HealthCheckLauncher = new ToolProcessLauncher(
            Path.Combine("HealthCheck", "WWP.LandscapeDataManager.HealthCheck.exe"),
            PipeServer.PipeName);
        PipeServer.Start();

        var projectSetupPanel = GetOrCreatePanel(application, "Project Setup");
        var dataProcessingPanel = GetOrCreatePanel(application, "Data Processing");
        var dataCalculationPanel = GetOrCreatePanel(application, "Data Calculation");
        var diagnosisPanel = GetOrCreatePanel(application, "Diagnosis");

        AddWorkflowButton<SetupParametersCommand>(
            projectSetupPanel,
            "LIMSetupPlantingParameters",
            "Project\nSetup",
            "PS",
            "Shared Parameter Setup",
            "Assign the shared parameter file, import the required planting and i-Tree parameters, and bind them to Project Information and Planting.");
        AddWorkflowButton<DownloadSpeciesScheduleCommand>(
            projectSetupPanel,
            "LIMDownloadITreeSpecies",
            "i-Tree\nDownloader",
            "iDL",
            "i-Tree Downloader",
            "Download the i-Tree species catalog, including Species_Code, common name, scientific name, and species type, and cache it locally for Tree Searcher.");
        AddWorkflowButton<FindLocationCommand>(
            projectSetupPanel,
            "LIMFindLocation",
            "Location\nFinder",
            "LF",
            "Location Finder",
            "Pick a location on a map (or search an address, or read the project's existing Site Location) and publish it to the i-Tree latitude/longitude parameters.");

        AddWorkflowButton<SearchTreesCommand>(
            dataProcessingPanel,
            "LIMSearchTrees",
            "Tree\nSearcher",
            "TS",
            "Tree Searcher",
            "Search the cached i-Tree species catalogue by name or code and assign a species directly to whatever Planting instances or types are currently selected.");
        AddWorkflowButton<ImportPlantingDataCommand>(
            dataProcessingPanel,
            "LIMImportPlantingData",
            "Excel\nImporter",
            "EX",
            "Planting Data Import",
            "Import Excel or Airtable records, detect source units, preview changes, and write mapped Planting type and instance values.");

        AddWorkflowButton<CalculateITreeCommand>(
            dataCalculationPanel,
            "LIMCalculateITree",
            "i-Tree\nCalculator",
            "iCAL",
            "i-Tree Calculator",
            "Validate tree inputs, calculate i-Tree benefits, write the WWP output parameters, and report elements with missing inputs.");
        AddWorkflowButton<CalculateFloorCommand>(
            dataCalculationPanel,
            "LIMCalculateFloor",
            "Floor\nCalculator",
            "FC",
            "Floor Calculator",
            "Match selected Floor instances to the WWP landscape data sheet and calculate cost, carbon, air, water, and temperature benefits from the sheet's per-square-metre coefficients.");

        AddWorkflowButton<SyncLatestCommand>(
            diagnosisPanel,
            "LIMSyncLatestPlantingData",
            "Refresh\nand Audit",
            "RA",
            "Refresh and Audit",
            "Compare with the last pull, reapply changed data, report updates, and identify stale or incomplete planting elements.");
        AddWorkflowButton<RunHealthCheckCommand>(
            diagnosisPanel,
            "LIMRunHealthCheck",
            "Health\nCheck",
            "HC",
            "Health Check",
            "Scan every Planting and Floor element for missing i-Tree or landscape data sheet results, list what still needs attention, and colour the active view red (needs attention) or green (calculated).");

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        CompanionLauncher?.Dispose();
        ParametersLauncher?.Dispose();
        ImporterLauncher?.Dispose();
        ITreeDownloaderLauncher?.Dispose();
        ITreeCalculatorLauncher?.Dispose();
        SyncAuditLauncher?.Dispose();
        TreeSearcherLauncher?.Dispose();
        LocationFinderLauncher?.Dispose();
        FloorCalculatorLauncher?.Dispose();
        HealthCheckLauncher?.Dispose();
        PipeServer?.Dispose();
        Dispatcher?.Dispose();

        CompanionLauncher = null;
        ParametersLauncher = null;
        ImporterLauncher = null;
        ITreeDownloaderLauncher = null;
        ITreeCalculatorLauncher = null;
        SyncAuditLauncher = null;
        TreeSearcherLauncher = null;
        LocationFinderLauncher = null;
        FloorCalculatorLauncher = null;
        HealthCheckLauncher = null;
        PipeServer = null;
        Dispatcher = null;
        return Result.Succeeded;
    }

    private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string panelName)
    {
        const string tabName = "LIM";

        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Another LIM add-in already created the shared tab.
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
