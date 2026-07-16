using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.App.Models;
using WWP.LandscapeDataManager.App.Services;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App;

public sealed partial class MainPage : Page
{
    private readonly AirtableClient _airtableClient = new();
    private readonly ParameterMappingStore _mappingStore = new();
    private RevitPipeClient? _revitClient;
    private IReadOnlyList<string> _airtableHeaders = [];
    private IReadOnlyList<ParameterOption> _parameterOptions = [];

    public MainPage()
    {
        InitializeComponent();
        BaseIdBox.Text = Environment.GetEnvironmentVariable("WWP_AIRTABLE_BASE_ID") ?? string.Empty;
    }

    public ObservableCollection<ReviewRow> ReviewItems { get; } = [];
    public ObservableCollection<MappingRow> MappingRows { get; } = [];

    public void Initialize(string pipeName)
    {
        _revitClient = new RevitPipeClient(pipeName);
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(RefreshRevitStatusAsync);
    }

    private async Task RefreshRevitStatusAsync()
    {
        var status = await GetClient().SendAsync<RevitStatus>(PipeCommands.GetStatus);
        RevitConnectionText.Text = $"Connected to Revit {status.RevitVersion}";
        DocumentText.Text = status.DocumentTitle ?? "No active document";
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var records = await LoadAirtableRecordsAsync();
            ShowStatus(
                InfoBarSeverity.Success,
                "Airtable connected",
                $"Downloaded {records.Count:N0} records from {TableNameBox.Text.Trim()}.");
        });
    }

    private async void SyncAndScan_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var recordsTask = LoadAirtableRecordsAsync();
            var scanTask = GetClient().SendAsync<ModelScanResult>(
                PipeCommands.ScanModel,
                new ModelScanOptions(PrimaryOptionsToggle.IsOn));

            await Task.WhenAll(recordsTask, scanTask);
            var nameIndex = NameIndex.Create(await recordsTask);
            RenderScan(await scanTask, nameIndex);

            ShowStatus(
                InfoBarSeverity.Success,
                "Review ready",
                "The model scan and Airtable name comparison completed. No Revit values were changed.");
        });
    }

    private async void ScanRevit_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var scan = await GetClient().SendAsync<ModelScanResult>(
                PipeCommands.ScanModel,
                new ModelScanOptions(PrimaryOptionsToggle.IsOn));
            RenderScan(scan, null);
            ShowStatus(
                InfoBarSeverity.Informational,
                "Revit scan complete",
                "Model types were collected without connecting to Airtable.");
        });
    }

    private Task<IReadOnlyList<AirtableRecord>> LoadAirtableRecordsAsync()
    {
        var token = Environment.GetEnvironmentVariable("WWP_AIRTABLE_TOKEN") ?? string.Empty;
        return _airtableClient.GetRecordsAsync(
            BaseIdBox.Text.Trim(),
            TableNameBox.Text.Trim(),
            token);
    }

    private async void LoadMapper_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var recordsTask = LoadAirtableRecordsAsync();
            var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(
                PipeCommands.GetParameterCatalog,
                new ModelScanOptions(PrimaryOptionsToggle.IsOn));

            await Task.WhenAll(recordsTask, catalogTask);
            var records = await recordsTask;
            var catalog = await catalogTask;

            _airtableHeaders = records
                .SelectMany(record => record.Fields.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _parameterOptions = catalog.Parameters
                .Where(parameter => parameter.IsWritable)
                .Select(parameter => new ParameterOption(parameter))
                .ToList();

            await RestoreMappingsAsync();
            MapperStatusText.Text =
                $"{_airtableHeaders.Count:N0} Airtable columns · {_parameterOptions.Count:N0} writable Revit parameter choices · {catalog.DocumentTitle}";
            ShowStatus(
                InfoBarSeverity.Success,
                "Parameter catalogs loaded",
                "Choose mappings or use Auto-map WWP to create suggestions.");
        });
    }

    private void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMapperLoaded())
        {
            return;
        }

        MappingRows.Add(CreateMappingRow());
    }

    private void RemoveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MappingRow row })
        {
            MappingRows.Remove(row);
        }
    }

    private void AutoMap_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMapperLoaded())
        {
            return;
        }

        MappingRows.Clear();

        AddSuggestedMapping(
            ["Avoided runoff m3/yr (16/18 girth)"],
            ["WWP_Avoided_Water_Runoff"]);
        AddSuggestedMapping(
            ["Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)"],
            ["WWP_Carbon_Dioxide_Sequestration"]);
        AddSuggestedMapping(
            ["Oxygen levels O2 kg/yr (16/18 girth)"],
            ["WWP_Oxygen_Levels"]);
        AddSuggestedMapping(["WWP_Pollutants_Removed"], ["WWP_Pollutants_Removed"]);
        AddSuggestedMapping(["Maintenance Costs"], ["WWP_Maintenance_Cost"]);
        AddSuggestedMapping(["COST_SAVED"], ["WWP_Cost_Saved"]);
        AddSuggestedMapping(["WWP_Total_GWP"], ["WWP_Total_GWP"]);
        AddSuggestedMapping(["WWP_LDS_Category"], ["WWP_LDS_Category"]);
        AddSuggestedMapping(["WWP_LDS_SubCategory"], ["WWP_LDS_SubCategory"]);
        AddSuggestedMapping(
            ["Max_Height"],
            ["Max_Height", "Maxi_Height"]);
        AddSuggestedMapping(
            ["Max_Width"],
            ["Max_Width", "Maxi_Width"]);

        if (MappingRows.Count == 0)
        {
            MappingRows.Add(CreateMappingRow());
        }

        MapperStatusText.Text = $"Generated {MappingRows.Count:N0} suggestions from fields and parameters present in this project.";
    }

    private async void SaveMappings_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var completeRows = MappingRows
                .Where(row => row.Enabled)
                .ToList();

            if (completeRows.Any(row =>
                    string.IsNullOrWhiteSpace(row.SelectedAirtableField) ||
                    row.SelectedTarget is null))
            {
                throw new InvalidOperationException(
                    "Every enabled mapping must have an Airtable column and a Revit parameter.");
            }

            var duplicateTarget = completeRows
                .GroupBy(row => new
                {
                    row.SelectedTarget!.Descriptor.Name,
                    row.SelectedTarget.Descriptor.Scope
                })
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateTarget is not null)
            {
                throw new InvalidOperationException(
                    $"The Revit target {duplicateTarget.Key.Name} ({duplicateTarget.Key.Scope}) is mapped more than once.");
            }

            var definitions = completeRows
                .Select(row => new ParameterMappingDefinition(
                    row.SelectedAirtableField!,
                    row.SelectedTarget!.Descriptor.Name,
                    row.SelectedTarget.Descriptor.Scope,
                    row.SelectedConversion,
                    row.Enabled))
                .ToList();

            await _mappingStore.SaveAsync(definitions);
            MapperStatusText.Text = $"Saved {definitions.Count:N0} mappings to {_mappingStore.FilePath}";
            ShowStatus(
                InfoBarSeverity.Success,
                "Mappings saved",
                "The mapping configuration is ready for the future preview/apply engine.");
        });
    }

    private async Task RestoreMappingsAsync()
    {
        MappingRows.Clear();
        var savedMappings = await _mappingStore.LoadAsync();

        foreach (var mapping in savedMappings)
        {
            var target = _parameterOptions.FirstOrDefault(option =>
                string.Equals(
                    option.Descriptor.Name,
                    mapping.RevitParameter,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    option.Descriptor.Scope,
                    mapping.Scope,
                    StringComparison.OrdinalIgnoreCase));
            var source = _airtableHeaders.FirstOrDefault(header =>
                string.Equals(header, mapping.AirtableField, StringComparison.OrdinalIgnoreCase));

            if (target is null || source is null)
            {
                continue;
            }

            MappingRows.Add(new MappingRow(_airtableHeaders, _parameterOptions)
            {
                Enabled = mapping.Enabled,
                SelectedAirtableField = source,
                SelectedTarget = target,
                SelectedConversion = mapping.Conversion
            });
        }

        if (MappingRows.Count == 0)
        {
            MappingRows.Add(CreateMappingRow());
        }
    }

    private MappingRow CreateMappingRow() => new(_airtableHeaders, _parameterOptions);

    private void AddSuggestedMapping(
        IReadOnlyList<string> sourceCandidates,
        IReadOnlyList<string> targetCandidates,
        string conversion = "Auto (Revit spec)")
    {
        var source = sourceCandidates
            .Select(candidate => _airtableHeaders.FirstOrDefault(header =>
                string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(header => header is not null);
        var target = targetCandidates
            .Select(candidate => _parameterOptions.FirstOrDefault(option =>
                string.Equals(option.Descriptor.Name, candidate, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(option.Descriptor.Scope, "Type", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(option => option is not null)
            ?? targetCandidates
                .Select(candidate => _parameterOptions.FirstOrDefault(option =>
                    string.Equals(option.Descriptor.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(option => option is not null);

        if (source is null || target is null)
        {
            return;
        }

        MappingRows.Add(new MappingRow(_airtableHeaders, _parameterOptions)
        {
            SelectedAirtableField = source,
            SelectedTarget = target,
            SelectedConversion = conversion
        });
    }

    private bool EnsureMapperLoaded()
    {
        if (_airtableHeaders.Count > 0 && _parameterOptions.Count > 0)
        {
            return true;
        }

        ShowStatus(
            InfoBarSeverity.Warning,
            "Load the catalogs first",
            "The mapper needs both Airtable headers and active-model Revit parameters.");
        return false;
    }

    private void RenderScan(ModelScanResult result, NameIndex? nameIndex)
    {
        ReviewItems.Clear();
        var needsReview = 0;

        foreach (var item in result.Items)
        {
            var status = nameIndex is null
                ? "Not checked"
                : nameIndex.Contains(item.TypeName) ? "Matched" : "Missing";

            if (status == "Missing")
            {
                needsReview++;
            }

            ReviewItems.Add(new ReviewRow(
                status,
                item.Category,
                item.TypeName,
                item.ElementCount.ToString("N0"),
                item.AreaSquareMetres == 0 ? "—" : item.AreaSquareMetres.ToString("N2"),
                item.CalculationType ?? "—"));
        }

        DocumentText.Text = result.DocumentTitle;
        TypeCountText.Text = result.Items.Count.ToString("N0");
        ElementCountText.Text = result.Items.Sum(item => item.ElementCount).ToString("N0");
        FloorAreaText.Text = $"{result.Items.Sum(item => item.AreaSquareMetres):N2} m²";
        ReviewCountText.Text = nameIndex is null ? "—" : needsReview.ToString("N0");
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;

        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, "Operation failed", exception.Message);
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private RevitPipeClient GetClient() =>
        _revitClient ?? throw new InvalidOperationException("The Revit connection has not been initialized.");

    public async ValueTask DisposeAsync()
    {
        if (_revitClient is not null)
        {
            await _revitClient.DisposeAsync();
        }
    }
}
