using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.App.Services;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App;

public sealed partial class MainPage : Page
{
    public void NavigateTo(string workflow)
    {
        WorkflowTabs.SelectedIndex = workflow.ToLowerInvariant() switch
        {
            "parameters" => 1,
            "import" => 0,
            "species" => 4,
            "calculate" => 3,
            "sync" => 2,
            _ => 0
        };
    }

    private readonly AirtableClient _airtableClient;
    private readonly ExcelClient _excelClient = new();
    private readonly ITreeApiClient _iTreeApiClient = new();
    private readonly ITreeExcelMergeService _iTreeExcelMergeService = new();
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private IReadOnlyList<string> _airtableHeaders = [];
    private IReadOnlyList<ParameterOption> _parameterOptions = [];
    private IReadOnlyList<ParameterMappingDefinition> _savedMappings = [];
    private ParameterWriteBatch? _pendingWriteBatch;
    private string? _mapperSourceIdentity;

    public MainPage()
    {
        InitializeComponent();
        _airtableClient = new AirtableClient(AirtableWebView);
    }

    public ObservableCollection<ReviewRow> ReviewItems { get; } = [];
    public ObservableCollection<MappingRow> MappingRows { get; } = [];
    public ObservableCollection<CalculationRow> CalculationItems { get; } = [];
    public ObservableCollection<SyncPreviewRow> SyncPreviewItems { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        _pipeName = pipeName;
        Loaded += Page_Loaded;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SiblingToolLauncher.ShowOrStart(Path.Combine("Settings", "WWP.LandscapeDataManager.Settings.exe"), _pipeName);
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, "Failed to open Settings", exception.Message);
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
            var settings = snapshot?.DataSource ?? new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
            _savedMappings = snapshot?.ParameterMappings ?? [];
            SharedLinkBox.Text = settings.SharedLink;
            ExcelPathBox.Text = settings.ExcelPath;
            DataSourceBox.SelectedIndex = settings.Kind == DataSourceKind.Excel ? 1 : 0;
            UpdateDataSourceVisibility();
            ITreeApiKeyStatusText.Text = string.IsNullOrWhiteSpace(_iTreeCredentialStore.Load())
                ? "No i-Tree API key saved — open Settings from the LIM ribbon first."
                : "i-Tree API key: saved (managed in Settings).";
            await RefreshRevitStatusAsync();
        });
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
            await LoadMapperCatalogsAndPromptAsync(records);
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
            var records = await recordsTask;
            var nameIndex = NameIndex.Create(records);
            RenderScan(await scanTask, nameIndex);

            if (!string.Equals(_mapperSourceIdentity, GetSourceIdentity(), StringComparison.Ordinal))
            {
                await LoadMapperCatalogsAndPromptAsync(records);
            }

            ShowStatus(
                InfoBarSeverity.Success,
                "Review ready",
                "The model scan and source-data name comparison completed. No Revit values were changed.");
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
                "Model types were collected without loading source data.");
        });
    }

    private async void Calculate_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var recordsTask = LoadAirtableRecordsAsync();
            var scanTask = GetClient().SendAsync<ModelScanResult>(
                PipeCommands.ScanModel,
                new ModelScanOptions(PrimaryOptionsToggle.IsOn));

            await Task.WhenAll(recordsTask, scanTask);
            var report = LandscapeCalculationEngine.Calculate(
                await recordsTask,
                await scanTask);
            RenderCalculations(report);

            var unresolved = report.MissingCount + report.AmbiguousCount + report.InvalidCount;
            ShowStatus(
                unresolved == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                "Calculation preview ready",
                $"Calculated {report.CalculatedCount:N0} Revit types; {unresolved:N0} require review. No Revit values were changed.");
        });
    }

    private async void PreviewParameterWrites_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            _pendingWriteBatch = null;
            ApplyParameterWritesButton.IsEnabled = false;
            SyncPreviewItems.Clear();

            var mappings = _savedMappings;
            var recordsTask = LoadAirtableRecordsAsync();
            var options = new ModelScanOptions(PrimaryOptionsToggle.IsOn);
            var scanTask = GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, options);
            var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(
                PipeCommands.GetParameterCatalog,
                options);
            await Task.WhenAll(recordsTask, scanTask, catalogTask);
            var catalog = await catalogTask;

            var plan = ParameterSyncPlanBuilder.Build(
                await recordsTask,
                await scanTask,
                mappings,
                options,
                catalog.Parameters,
                catalog.PreferredUnitSystem);
            foreach (var issue in plan.Issues)
            {
                SyncPreviewItems.Add(SyncPreviewRow.FromIssue(issue));
            }

            if (plan.Batch.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "No parameter writes could be prepared. Resolve the skipped type matches and source columns first.");
            }

            var preview = await GetClient().SendAsync<ParameterWriteResult>(
                PipeCommands.PreviewParameterWrites,
                plan.Batch);
            foreach (var row in preview.Rows)
            {
                SyncPreviewItems.Add(SyncPreviewRow.FromRevit(row));
            }

            var applicableIndices = preview.Rows
                .Where(row => row.CanApply)
                .Select(row => row.ItemIndex)
                .ToHashSet();
            var applicableItems = plan.Batch.Items
                .Where((item, index) => applicableIndices.Contains(index))
                .ToList();
            _pendingWriteBatch = new ParameterWriteBatch(plan.Batch.Options, applicableItems);
            ApplyParameterWritesButton.IsEnabled = applicableItems.Count > 0;

            var invalid = preview.Rows.Count(row => row.Status == "Invalid");
            var unchanged = preview.Rows.Count(row => row.Status == "No change");
            SyncStatusText.Text =
                $"{applicableItems.Count:N0} ready · {unchanged:N0} unchanged · " +
                $"{invalid:N0} invalid · {plan.Issues.Count:N0} skipped · " +
                $"{plan.UnitAdjustments.Count:N0} unit-aligned · {catalog.PreferredUnitSystem}";
            var unitNote = catalog.UnitSystemWarning is null
                ? $"Import values are aligned to {catalog.PreferredUnitSystem}, read from {catalog.PreferredUnitSystemSource}."
                : catalog.UnitSystemWarning;
            ShowStatus(
                catalog.UnitSystemWarning is not null || applicableItems.Count > 0
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Informational,
                "Parameter write preview ready",
                applicableItems.Count > 0
                    ? $"Review the proposed values, then explicitly select Apply to Revit. {unitNote}"
                    : $"No Revit values need to be changed. {unitNote}");
        });
    }

    private async void ApplyParameterWrites_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingWriteBatch is null || _pendingWriteBatch.Items.Count == 0)
        {
            ShowStatus(
                InfoBarSeverity.Warning,
                "Build a preview first",
                "There are no validated parameter changes ready to apply.");
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Apply landscape data to Revit?",
            Content = $"This will write {_pendingWriteBatch.Items.Count:N0} validated type/mapping rows to the active Revit model. Revit will apply them in one transaction.",
            PrimaryButtonText = "Apply changes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<ParameterWriteResult>(
                PipeCommands.ApplyParameterWrites,
                _pendingWriteBatch);
            SyncPreviewItems.Clear();
            foreach (var row in result.Rows)
            {
                SyncPreviewItems.Add(SyncPreviewRow.FromRevit(row));
            }

            SyncStatusText.Text =
                $"Applied {result.ChangedParameterCount:N0} parameter values across {result.ChangedElementCount:N0} Revit elements/types.";
            _pendingWriteBatch = null;
            ApplyParameterWritesButton.IsEnabled = false;
            ShowStatus(
                InfoBarSeverity.Success,
                "Landscape data applied",
                SyncStatusText.Text);
        });
    }

    private async Task<IReadOnlyList<AirtableRecord>> LoadAirtableRecordsAsync()
    {
        var settings = new DataSourceSettings(
            DataSourceBox.SelectedIndex == 1 ? DataSourceKind.Excel : DataSourceKind.Airtable,
            SharedLinkBox.Text.Trim(),
            ExcelPathBox.Text.Trim());
        await ProjectSettingsSync.PushAsync(GetClient(), dataSource: settings);

        return settings.Kind == DataSourceKind.Excel
            ? await _excelClient.GetRecordsAsync(settings.ExcelPath)
            : await _airtableClient.GetRecordsAsync(settings.SharedLink);
    }

    private void DataSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDataSourceVisibility();

    private void UpdateDataSourceVisibility()
    {
        if (SharedLinkBox is null || ExcelSourcePanel is null)
        {
            return;
        }

        var useExcel = DataSourceBox.SelectedIndex == 1;
        SharedLinkBox.Visibility = useExcel ? Visibility.Collapsed : Visibility.Visible;
        ExcelSourcePanel.Visibility = useExcel ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowseExcel_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xlsm");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ExcelPathBox.Text = file.Path;
            DataSourceBox.SelectedIndex = 1;
        }
    }

    private async void BrowseExistingITreeWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xlsm");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ITreeExcelPathBox.Text = file.Path;
        }
    }

    private async void ChooseNewITreeWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"iTree Data {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("Excel workbook", [".xlsx"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            ITreeExcelPathBox.Text = file.Path;
        }
    }

    private async void ExportITree_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var apiKey = _iTreeCredentialStore.Load().Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("No i-Tree API key saved — open Settings from the LIM ribbon first.");
            }

            var catalogMode = ITreeScopeBox.SelectedIndex == 2;
            var profile = new ITreeExportProfile(
                ITreeMonetaryCheckBox.IsChecked == true,
                ITreeCarbonCheckBox.IsChecked == true,
                ITreeHydrologyCheckBox.IsChecked == true,
                ITreeAirCheckBox.IsChecked == true,
                ITreeMetadataCheckBox.IsChecked == true,
                ITreeAnnualTimelineCheckBox.IsChecked == true,
                ITreeCumulativeTimelineCheckBox.IsChecked == true,
                ITreeFullResponseCheckBox.IsChecked == true);
            if (!catalogMode && !profile.Monetary && !profile.Carbon && !profile.Hydrology &&
                !profile.AirQuality && !profile.Metadata && !profile.FullResponse)
            {
                throw new InvalidOperationException("Select at least one i-Tree data group or the full response.");
            }

            ITreeInputScanResult? scan = null;
            ITreeDownloadResult download;
            if (catalogMode)
            {
                download = await _iTreeApiClient.DownloadSpeciesCatalogAsync(apiKey);
            }
            else
            {
                scan = await GetClient().SendAsync<ITreeInputScanResult>(
                    PipeCommands.GetITreeInputs,
                    new ITreeInputOptions(ITreeScopeBox.SelectedIndex == 1));
                download = await _iTreeApiClient.DownloadAsync(scan.Items, apiKey, profile);
            }
            var headerRow = double.IsNaN(ITreeHeaderRowBox.Value)
                ? 1
                : (int)Math.Round(ITreeHeaderRowBox.Value);
            var merge = await _iTreeExcelMergeService.MergeAsync(
                download.Records,
                new ITreeExcelMergeOptions(
                    ITreeExcelPathBox.Text,
                    ITreeWorksheetBox.Text,
                    headerRow,
                    ITreeAppendMissingCheckBox.IsChecked == true,
                    ITreeBackupCheckBox.IsChecked == true));

            var notes = new List<string>
            {
                $"Downloaded {download.Records.Count:N0} Species_Code records and {download.FieldCount:N0} selected fields.",
                $"Updated {merge.UpdatedRows:N0} matching rows, appended {merge.AppendedRows:N0}, " +
                $"preserved {merge.PreservedUnmatchedRows:N0} unmatched rows, and added {merge.AddedColumns:N0} columns."
            };
            if (scan is not null && scan.SkippedWithoutSpeciesCode > 0)
            {
                notes.Add($"Skipped {scan.SkippedWithoutSpeciesCode:N0} Revit planting types without Species_Code.");
            }
            var nonTwentyYearTypes = scan?.Items.Count(item => item.Years != 20) ?? 0;
            if (nonTwentyYearTypes > 0)
            {
                notes.Add(
                    $"Warning: {nonTwentyYearTypes:N0} types use Years other than 20; the legacy *_20yr columns contain their configured-period totals.");
            }
            if (download.DuplicateSpeciesCodes.Count > 0)
            {
                notes.Add(
                    $"Used the first Revit type for {download.DuplicateSpeciesCodes.Count:N0} duplicate Species_Code values: " +
                    string.Join(", ", download.DuplicateSpeciesCodes.Take(8)) +
                    (download.DuplicateSpeciesCodes.Count > 8 ? "..." : string.Empty));
            }
            if (download.Errors.Count > 0)
            {
                notes.Add($"{download.Errors.Count:N0} API requests failed: {string.Join("; ", download.Errors.Take(3))}");
            }
            if (merge.BackupPath is not null)
            {
                notes.Add($"Backup: {merge.BackupPath}");
            }

            ITreeExportStatusText.Text = string.Join(Environment.NewLine, notes);
            ShowStatus(
                download.Errors.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                "i-Tree Excel export complete",
                $"Saved {Path.GetFileName(merge.FilePath)}. {merge.UpdatedRows:N0} rows updated; " +
                $"{merge.AppendedRows:N0} rows appended.");
        });
    }

    private string GetSourceDescription() => DataSourceBox.SelectedIndex == 1
        ? Path.GetFileName(ExcelPathBox.Text.Trim())
        : "the Airtable shared view";

    private string GetSourceIdentity() => DataSourceBox.SelectedIndex == 1
        ? $"excel|{ExcelPathBox.Text.Trim()}"
        : $"airtable|{SharedLinkBox.Text.Trim()}";

    private async void LoadMapper_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () => await LoadMapperCatalogsAndPromptAsync());
    }

    private async Task LoadMapperCatalogsAndPromptAsync(IReadOnlyList<AirtableRecord>? loadedRecords = null)
    {
        var recordsTask = loadedRecords is null
            ? LoadAirtableRecordsAsync()
            : Task.FromResult(loadedRecords);
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
        _mapperSourceIdentity = GetSourceIdentity();
        await PromptForMappingModeAsync(catalog.DocumentTitle);
    }

    private void AutoMap_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMapperLoaded())
        {
            return;
        }

        var mapped = AutoMapUnmappedColumns();
        UpdateMappingSummary();
        ShowStatus(
            mapped > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational,
            "Auto-map complete",
            mapped > 0
                ? $"Mapped {mapped:N0} additional source columns. Review the remaining Needs mapping rows."
                : "No additional safe name or WWP alias matches were found. Map the remaining columns manually.");
    }

    private void MapperColumnWidth_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        ApplyMapperColumnWidths();

    private void ResetMapperColumnWidths_Click(object sender, RoutedEventArgs e)
    {
        MapperSourceWidthSlider.Value = 420;
        MapperTargetWidthSlider.Value = 350;
        MapperConversionWidthSlider.Value = 230;
        ApplyMapperColumnWidths();
    }

    private void MappingRowGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            ApplyMapperColumnWidths(grid);
        }
    }

    private void ApplyMapperColumnWidths()
    {
        if (MapperSourceWidthSlider is null ||
            MapperTargetWidthSlider is null ||
            MapperConversionWidthSlider is null ||
            MapperSourceHeaderColumn is null ||
            MapperTargetHeaderColumn is null ||
            MapperConversionHeaderColumn is null)
        {
            return;
        }

        MapperSourceHeaderColumn.Width = new GridLength(MapperSourceWidthSlider.Value);
        MapperTargetHeaderColumn.Width = new GridLength(MapperTargetWidthSlider.Value);
        MapperConversionHeaderColumn.Width = new GridLength(MapperConversionWidthSlider.Value);

        if (MapperRowsListView is null)
        {
            return;
        }

        foreach (var grid in FindVisualChildren<Grid>(MapperRowsListView)
                     .Where(grid => string.Equals(grid.Tag as string, "MapperRowGrid", StringComparison.Ordinal)))
        {
            ApplyMapperColumnWidths(grid);
        }
    }

    private void ApplyMapperColumnWidths(Grid grid)
    {
        if (grid.ColumnDefinitions.Count < 5 ||
            MapperSourceWidthSlider is null ||
            MapperTargetWidthSlider is null ||
            MapperConversionWidthSlider is null)
        {
            return;
        }

        grid.ColumnDefinitions[1].Width = new GridLength(MapperSourceWidthSlider.Value);
        grid.ColumnDefinitions[2].Width = new GridLength(MapperTargetWidthSlider.Value);
        grid.ColumnDefinitions[3].Width = new GridLength(MapperConversionWidthSlider.Value);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private async void SaveMappings_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var enabledRows = MappingRows
                .Where(row => row.Enabled)
                .ToList();

            if (enabledRows.Any(row =>
                    string.IsNullOrWhiteSpace(row.SelectedAirtableField) ||
                    row.SelectedTarget is null))
            {
                throw new InvalidOperationException(
                    "Every enabled mapping must have a source column and a Revit parameter.");
            }

            var duplicateTarget = enabledRows
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

            var definitions = MappingRows
                .Where(row => row.SelectedTarget is not null)
                .Select(row => new ParameterMappingDefinition(
                    row.SelectedAirtableField!,
                    row.SelectedTarget!.Descriptor.Name,
                    row.SelectedTarget.Descriptor.Scope,
                    row.SelectedConversion,
                    row.Enabled))
                .ToList();

            _savedMappings = definitions;
            await ProjectSettingsSync.PushAsync(GetClient(), parameterMappings: definitions);
            _pendingWriteBatch = null;
            ApplyParameterWritesButton.IsEnabled = false;
            MapperStatusText.Text = $"Saved {definitions.Count:N0} mappings to this project's settings.";
            ShowStatus(
                InfoBarSeverity.Success,
                "Mappings saved",
                "Enabled mappings are ready on the Apply to Revit tab; disabled columns will be ignored.");
        });
    }

    private async Task RestoreMappingsAsync()
    {
        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        _savedMappings = snapshot?.ParameterMappings ?? [];
        ApplyMappings(_savedMappings);
    }

    private void ApplyMappings(IReadOnlyList<ParameterMappingDefinition> savedMappings)
    {
        MappingRows.Clear();
        foreach (var source in _airtableHeaders)
        {
            var mapping = savedMappings.FirstOrDefault(saved =>
                string.Equals(saved.AirtableField, source, StringComparison.OrdinalIgnoreCase));
            var target = _parameterOptions.FirstOrDefault(option =>
                mapping is not null &&
                string.Equals(
                    option.Descriptor.Name,
                    mapping!.RevitParameter,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    option.Descriptor.Scope,
                    mapping.Scope,
                    StringComparison.OrdinalIgnoreCase));
            MappingRows.Add(new MappingRow(_airtableHeaders, _parameterOptions, _parameterOptions)
            {
                SelectedAirtableField = source,
                SelectedTarget = target,
                SelectedConversion = mapping?.Conversion ?? "Auto (Revit spec)",
                Enabled = target is not null && mapping!.Enabled
            });
        }
    }

    private List<ParameterMappingDefinition> BuildMappingDefinitions() =>
        MappingRows
            .Where(row => row.SelectedTarget is not null)
            .Select(row => new ParameterMappingDefinition(
                row.SelectedAirtableField!, row.SelectedTarget!.Descriptor.Name, row.SelectedTarget.Descriptor.Scope, row.SelectedConversion, row.Enabled))
            .ToList();

    private async Task PromptForMappingModeAsync(string documentTitle)
    {
        if (_airtableHeaders.Count == 0)
        {
            throw new InvalidOperationException("The selected Airtable view or Excel worksheet has no column headers.");
        }

        var restored = MappingRows.Count(row => row.SelectedTarget is not null);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Map imported columns",
            Content = $"Found {_airtableHeaders.Count:N0} source columns and {_parameterOptions.Count:N0} writable Revit parameter choices in {documentTitle}. " +
                      $"{restored:N0} saved mappings were restored. Auto-map uses exact names and known WWP aliases; uncertain columns remain for manual mapping.",
            PrimaryButtonText = "Auto-map all",
            CloseButtonText = "Map manually",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        var mapped = result == ContentDialogResult.Primary
            ? AutoMapUnmappedColumns()
            : 0;

        UpdateMappingSummary();
        var needsMapping = MappingRows.Count(row => row.SelectedTarget is null);
        ShowStatus(
            needsMapping == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            result == ContentDialogResult.Primary ? "Auto-map complete" : "Manual mapping ready",
            result == ContentDialogResult.Primary
                ? $"Mapped {mapped:N0} additional columns; {needsMapping:N0} still need a Revit target or can remain disabled."
                : $"All {_airtableHeaders.Count:N0} source columns are listed. Select targets for the {needsMapping:N0} unmapped columns you want to use.");
    }

    private int AutoMapUnmappedColumns()
    {
        var usedTargets = MappingRows
            .Where(row => row.SelectedTarget is not null)
            .Select(row => GetTargetKey(row.SelectedTarget!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapped = 0;

        foreach (var row in MappingRows.Where(row => row.SelectedTarget is null))
        {
            var target = FindSuggestedTarget(row.SelectedAirtableField!, usedTargets);
            if (target is null)
            {
                row.Enabled = false;
                continue;
            }

            row.SelectedTarget = target;
            row.SelectedConversion = "Auto (Revit spec)";
            row.Enabled = true;
            usedTargets.Add(GetTargetKey(target));
            mapped++;
        }

        return mapped;
    }

    private ParameterOption? FindSuggestedTarget(string source, IReadOnlySet<string> usedTargets)
    {
        foreach (var candidate in GetTargetCandidates(source))
        {
            var normalizedCandidate = NormalizeMappingName(candidate);
            var match = _parameterOptions
                .Where(option => !usedTargets.Contains(GetTargetKey(option)))
                .Where(option =>
                    string.Equals(option.Descriptor.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeMappingName(option.Descriptor.Name) == normalizedCandidate)
                .OrderByDescending(option =>
                    string.Equals(option.Descriptor.Scope, "Type", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetTargetCandidates(string source) => source.Trim() switch
    {
        "Avoided runoff m3/yr (16/18 girth)" => ["!_S_PLT_LDS_AvoidedWaterRunoffAnnual_Number", source],
        "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)" => ["!_S_PLT_LDS_CarbonDioxideSequestrationAnnual_Number", source],
        "Oxygen levels O2 kg/yr (16/18 girth)" => ["!_S_PLT_LDS_OxygenLevelsAnnual_Number", source],
        // MaintenanceCost/PollutantsRemoved are Floor Calculator's own coefficient-times-area outputs
        // (see FloorLdsCalculationService) — but the raw WWP sheet columns are still useful direct
        // mapping targets for Types, matching the same "map raw WWP_* column onto its !_S_PLT_
        // parameter" intent as COST_SAVED below.
        "Maintenance Costs" => ["!_S_PLT_LDS_MaintenanceCostAnnual_Currency", source],
        "COST_SAVED" => ["!_S_PLT_iTreeResult_CostSavedAnnual_Currency", source],
        "WWP_Pollutants_Removed" => ["!_S_PLT_LDS_PollutantsRemovedAnnual_Mass", source],
        "Origin" => ["!_S_PLT_LDS_Origin_Text", source],
        "WWP_LDS_Category" => ["!_S_PLT_LDS_Category_Text", source],
        "WWP_LDS_SubCategory" => ["!_S_PLT_LDS_SubCategory_Text", source],
        "Product GWP" => ["!_S_PLT_LDS_ProductGWP_Number", source],
        "Transport GWP" => ["!_S_PLT_LDS_TransportGWP_Number", source],
        "Pollen" => ["!_S_PLT_LDS_PollenAnnual_Number", source],
        "Surface_Temperature_Reduction_Min" => ["!_S_PLT_LDS_SurfaceTempReduction_Number", source],
        "Air temperature reduction (1.5m height)" => ["!_S_PLT_LDS_AirTempReduction_Number", source],
        "Irrigation_Demand_Plant Factor" => ["!_S_PLT_LDS_IrrigationDemandFactor_Number", source],
        "Max_Height" => ["Max_Height", "Maxi_Height"],
        "Max_Width" => ["Max_Width", "Maxi_Width"],
        _ => [source]
    };

    private static string GetTargetKey(ParameterOption option) =>
        $"{option.Descriptor.Scope}|{option.Descriptor.Name}";

    private static string NormalizeMappingName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private void UpdateMappingSummary()
    {
        var ready = MappingRows.Count(row => row.Enabled && row.SelectedTarget is not null);
        var ignored = MappingRows.Count(row => !row.Enabled && row.SelectedTarget is not null);
        var needsMapping = MappingRows.Count(row => row.SelectedTarget is null);
        MapperStatusText.Text =
            $"{MappingRows.Count:N0} source columns · {ready:N0} ready · " +
            $"{needsMapping:N0} need mapping · {ignored:N0} ignored";
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
            "The mapper needs both source headers and active-model Revit parameters.");
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

    private void RenderCalculations(CalculationReport report)
    {
        CalculationItems.Clear();
        foreach (var row in report.Rows)
        {
            CalculationItems.Add(row);
        }

        CarbonTotalText.Text = $"{report.Totals.Carbon:N2} kg CO₂e/yr";
        OxygenTotalText.Text = $"{report.Totals.Oxygen:N2} kg O₂/yr";
        RunoffTotalText.Text = $"{report.Totals.Runoff:N2} m³/yr";
        PollutantsTotalText.Text = $"{report.Totals.Pollutants:N2} kg/yr";
        MaintenanceTotalText.Text = $"{report.Totals.MaintenanceCost:N2}";
        SavingsTotalText.Text = $"{report.Totals.CostSavings:N2}";
        CalculationStatusText.Text =
            $"{report.CalculatedCount:N0} calculated · {report.MissingCount:N0} missing · " +
            $"{report.AmbiguousCount:N0} duplicate · {report.InvalidCount:N0} invalid · " +
            $"net savings {report.Totals.NetSavings:N2}";
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
