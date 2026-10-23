using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Revit.Commands;
using WWP.LandscapeDataManager.Revit.Infrastructure;
using WWP.LandscapeDataManager.Revit.Services;

namespace WWP.LandscapeDataManager.Revit;

public sealed class App : IExternalApplication
{
    internal static RevitExternalEventDispatcher? Dispatcher { get; private set; }
    internal static RevitPipeServer? PipeServer { get; private set; }
    internal static ToolProcessLauncher? ParametersLauncher { get; private set; }
    internal static ToolProcessLauncher? ImporterLauncher { get; private set; }
    internal static ToolProcessLauncher? ITreeDownloaderLauncher { get; private set; }
    internal static ToolProcessLauncher? ITreeCalculatorLauncher { get; private set; }
    internal static ToolProcessLauncher? TreeSearcherLauncher { get; private set; }
    internal static ToolProcessLauncher? LocationFinderLauncher { get; private set; }
    internal static ToolProcessLauncher? FloorCalculatorLauncher { get; private set; }
    internal static ToolProcessLauncher? DashboardLauncher { get; private set; }
    internal static ToolProcessLauncher? ConnectivityLauncher { get; private set; }
    internal static ToolProcessLauncher? GrowthProjectionsLauncher { get; private set; }
    internal static ToolProcessLauncher? SettingsLauncher { get; private set; }

    private static UIControlledApplication? _application;
    private static string _revitVersion = string.Empty;
    private static volatile LimRelease? _pendingUpdate;

    public Result OnStartup(UIControlledApplication application)
    {
        Dispatcher = new RevitExternalEventDispatcher();
        PipeServer = new RevitPipeServer(Dispatcher);
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
        TreeSearcherLauncher = new ToolProcessLauncher(
            Path.Combine("TreeSearcher", "WWP.LandscapeDataManager.TreeSearcher.exe"),
            PipeServer.PipeName);
        LocationFinderLauncher = new ToolProcessLauncher(
            Path.Combine("LocationFinder", "WWP.LandscapeDataManager.LocationFinder.exe"),
            PipeServer.PipeName);
        FloorCalculatorLauncher = new ToolProcessLauncher(
            Path.Combine("FloorCalculator", "WWP.LandscapeDataManager.FloorCalculator.exe"),
            PipeServer.PipeName);
        DashboardLauncher = new ToolProcessLauncher(
            Path.Combine("Dashboard", "WWP.LandscapeDataManager.Dashboard.exe"),
            PipeServer.PipeName);
        ConnectivityLauncher = new ToolProcessLauncher(
            Path.Combine("Connectivity", "WWP.LandscapeDataManager.Connectivity.exe"),
            PipeServer.PipeName);
        GrowthProjectionsLauncher = new ToolProcessLauncher(
            Path.Combine("GrowthProjections", "WWP.LandscapeDataManager.GrowthProjections.exe"),
            PipeServer.PipeName);
        SettingsLauncher = new ToolProcessLauncher(
            Path.Combine("Settings", "WWP.LandscapeDataManager.Settings.exe"),
            PipeServer.PipeName);
        PipeServer.Start();

        var projectSetupPanel = GetOrCreatePanel(application, "Project Setup");
        var dataProcessingPanel = GetOrCreatePanel(application, "Data Processing");
        var dataCalculationPanel = GetOrCreatePanel(application, "Data Calculation");
        var reportingPanel = GetOrCreatePanel(application, "Reporting");
        var updatesPanel = GetOrCreatePanel(application, "Updates");

        AddWorkflowButton<OpenSettingsCommand>(
            projectSetupPanel,
            "LIMOpenSettings",
            "Settings",
            "ST",
            "Settings",
            "Save the i-Tree API key, Airtable personal access token, and the WWP landscape data sheet source in one place — every other LIM tool reads them from here instead of asking again.");
        AddWorkflowButton<SetupParametersCommand>(
            projectSetupPanel,
            "LIMSetupPlantingParameters",
            "Import Shared\nParameter",
            "PS",
            "Import Shared Parameter",
            "Assign the shared parameter file, import the required planting and i-Tree parameters, and bind them to Project Information and Planting.");
        AddWorkflowButton<DownloadSpeciesScheduleCommand>(
            projectSetupPanel,
            "LIMDownloadITreeSpecies",
            "iTree Database\nCacher",
            "iDL",
            "iTree Database Cacher",
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
        AddWorkflowButton<GrowthProjectionsCommand>(
            dataProcessingPanel,
            "LIMGrowthProjections",
            "Growth\nProjections",
            "GP",
            "Growth Projections",
            "Model the planting at each growth year: the original trees become year 0 (the project start) and each tree is copied onto a growth-year workset with its age set to its age at planting plus that year. Re-run to refresh the linked copies.");

        AddWorkflowButton<CalculateITreeCommand>(
            dataCalculationPanel,
            "LIMCalculateITree",
            "Tree\nCalculator",
            "iCAL",
            "Tree Calculator",
            "Validate tree inputs, calculate i-Tree benefits, write the WWP output parameters, and report elements with missing inputs.");
        AddWorkflowButton<CalculateFloorCommand>(
            dataCalculationPanel,
            "LIMCalculateFloor",
            "Floor\nCalculator",
            "FC",
            "Floor Calculator",
            "Match selected Floor instances to the WWP landscape data sheet and calculate cost, carbon, air, water, and temperature benefits from the sheet's per-square-metre coefficients.");
        AddWorkflowButton<CalculateConnectivityCommand>(
            dataCalculationPanel,
            "LIMCalculateConnectivity",
            "Connectivity\nCalculator",
            "CN",
            "Connectivity Calculator",
            "Score every tree and planting area 1-4 by how much it holds the green network together (Probability of Connectivity, dPC), write the band back to Revit, and colour the view by band.");

        AddWorkflowButton<OpenDashboardCommand>(
            reportingPanel,
            "LIMOpenDashboard",
            "Benefits\nDashboard",
            "DB",
            "Landscape Benefits Dashboard",
            "Roll up the i-Tree and landscape data sheet results already stored on Planting and Floor elements into per-species and planting-area subtotals and a grand total, filterable by Design Option and level, exportable as an image or Excel workbook.");
        // Last of all the tools: the schedules collect what every tool before it has written.
        AddWorkflowButton<CreateKpiSchedulesCommand>(
            reportingPanel,
            "LIMCreateKpiSchedules",
            "KPI\nSchedules",
            "KS",
            "Create KPI Schedules",
            "Create a Planting and a Planting Area schedule per Design Option, covering every KPI parameter the Dashboard reports (i-Tree, oxygen, maintenance, forecast outlooks, BNG and connectivity), grouped by species/landscape type with counts and totals. Run it again to add newer parameters to schedules it made before.");

        // Not part of the workflow, so after it, in a panel of its own.
        AddWorkflowButton<CheckForUpdatesCommand>(
            updatesPanel,
            "LIMCheckForUpdates",
            $"Check for Updates\n{UpdateService.InstalledVersionText}",
            "UP",
            $"Check for Updates (installed: {UpdateService.InstalledVersionText})",
            "Check GitHub for a newer LIM- Landscape Data release. The update downloads in its own window while you keep working, then installs once Revit closes.");

        StartBackgroundUpdateCheck(application);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= OnIdlingShowUpdate;
        _application = null;
        ParametersLauncher?.Dispose();
        ImporterLauncher?.Dispose();
        ITreeDownloaderLauncher?.Dispose();
        ITreeCalculatorLauncher?.Dispose();
        TreeSearcherLauncher?.Dispose();
        LocationFinderLauncher?.Dispose();
        FloorCalculatorLauncher?.Dispose();
        DashboardLauncher?.Dispose();
        ConnectivityLauncher?.Dispose();
        GrowthProjectionsLauncher?.Dispose();
        SettingsLauncher?.Dispose();
        PipeServer?.Dispose();
        Dispatcher?.Dispose();

        ParametersLauncher = null;
        ImporterLauncher = null;
        ITreeDownloaderLauncher = null;
        ITreeCalculatorLauncher = null;
        TreeSearcherLauncher = null;
        LocationFinderLauncher = null;
        FloorCalculatorLauncher = null;
        DashboardLauncher = null;
        ConnectivityLauncher = null;
        GrowthProjectionsLauncher = null;
        SettingsLauncher = null;
        PipeServer = null;
        Dispatcher = null;
        return Result.Succeeded;
    }

    /// <summary>
    /// Asks GitHub off the UI thread; Revit only allows dialogs on its own thread, so a newer
    /// release is shown from the next Idling event instead.
    /// </summary>
    private static void StartBackgroundUpdateCheck(UIControlledApplication application)
    {
        _application = application;
        _revitVersion = application.ControlledApplication.VersionNumber;
        _ = Task.Run(async () =>
        {
            var release = await UpdateService.CheckInBackgroundAsync();
            if (release is not null)
            {
                _pendingUpdate = release;
            }
        });
        application.Idling += OnIdlingShowUpdate;
    }

    private static void OnIdlingShowUpdate(object? sender, IdlingEventArgs e)
    {
        var release = _pendingUpdate;
        if (release is null)
        {
            return;
        }

        _pendingUpdate = null;
        if (_application is not null)
        {
            _application.Idling -= OnIdlingShowUpdate;
        }

        UpdatePrompt.Show(release, _revitVersion, offerSkip: true);
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

        // A missing icon used to throw out of OnStartup, silently dropping every button after it.
        try
        {
            button.Image = LoadEmbeddedImage($"LIM.LandscapeData.{iconCode}.16.png");
            button.LargeImage = LoadEmbeddedImage($"LIM.LandscapeData.{iconCode}.32.png");
        }
        catch (InvalidOperationException exception)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
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
