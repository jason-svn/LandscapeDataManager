using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Importer;

public sealed partial class MainPage : Page
{
    private static readonly string[] TypeKeyFields = ["Types", "Planting type", "Species names", "species", "type"];

    private readonly ExcelClient _excelClient = new();
    private readonly AirtableApiClient _airtableApiClient = new();
    private readonly DataSourceSettingsStore _dataSourceSettingsStore = new();
    private readonly AirtableApiSettingsStore _airtableApiSettingsStore = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();
    private readonly ParameterMappingStore _mappingStore = new();
    private readonly TypeAliasStore _typeAliasStore = new();
    private readonly SyncedValueHistoryStore _historyStore = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;

    private IReadOnlyList<AirtableRecord> _sourceRecords = [];
    private ModelScanResult? _typeScan;
    private PlantingInstanceScanResult? _instanceScan;
    private IReadOnlyList<RevitParameterDescriptor> _parameterCatalog = [];
    private string _preferredUnitSystem = "Metric";
    private ParameterWriteBatch? _pendingTypeBatch;
    private InstanceParameterWriteBatch? _pendingInstanceBatch;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<MappingRow> TypeMappingRows { get; } = [];
    public ObservableCollection<MappingRow> InstanceMappingRows { get; } = [];
    public ObservableCollection<TypeSyncRow> TypeSyncRows { get; } = [];
    public ObservableCollection<InstanceSyncRow> InstanceSyncRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var dataSourceSettings = await _dataSourceSettingsStore.LoadAsync();
        var airtableSettings = await _airtableApiSettingsStore.LoadAsync();
        SourceKindBox.SelectedIndex = dataSourceSettings.Kind == DataSourceKind.Excel ? 1 : 0;
        ExcelPathBox.Text = dataSourceSettings.ExcelPath;
        AirtableBaseIdBox.Text = airtableSettings.BaseId;
        AirtableTableBox.Text = airtableSettings.TableIdOrName;
        AirtableViewBox.Text = airtableSettings.ViewName ?? string.Empty;
        AirtableTokenBox.Password = _airtableCredentialStore.Load();
        UpdateSourceVisibility();
    }

    private void SourceKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSourceVisibility();

    private void UpdateSourceVisibility()
    {
        if (AirtablePanel is null || ExcelPanel is null)
        {
            return;
        }

        var useExcel = SourceKindBox.SelectedIndex == 1;
        AirtablePanel.Visibility = useExcel ? Visibility.Collapsed : Visibility.Visible;
        ExcelPanel.Visibility = useExcel ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowseExcel_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xlsm");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ExcelPathBox.Text = file.Path;
            SourceKindBox.SelectedIndex = 1;
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            _sourceRecords = await LoadSourceRecordsAsync();

            var options = new ModelScanOptions();
            var typeScanTask = GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, options);
            var instanceScanTask = GetClient().SendAsync<PlantingInstanceScanResult>(PipeCommands.ScanPlantingInstances);
            var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, options);
            await Task.WhenAll(typeScanTask, instanceScanTask, catalogTask);

            _typeScan = await typeScanTask;
            _instanceScan = await instanceScanTask;
            var catalog = await catalogTask;
            _parameterCatalog = catalog.Parameters;
            _preferredUnitSystem = catalog.PreferredUnitSystem;

            await RestoreMappingsAsync();

            ConnectStatusText.Text =
                $"{_sourceRecords.Count:N0} source records · {_typeScan.Items.Count:N0} Revit types · " +
                $"{_instanceScan.Items.Count:N0} planting instances · {catalog.Parameters.Count(p => p.IsWritable):N0} writable parameters.";
        });
    }

    private async Task<IReadOnlyList<AirtableRecord>> LoadSourceRecordsAsync()
    {
        if (SourceKindBox.SelectedIndex == 1)
        {
            var excelPath = ExcelPathBox.Text.Trim();
            await _dataSourceSettingsStore.SaveAsync(new DataSourceSettings(DataSourceKind.Excel, string.Empty, excelPath));
            return await _excelClient.GetRecordsAsync(excelPath);
        }

        var token = AirtableTokenBox.Password.Trim();
        var settings = new AirtableApiSettings(
            AirtableBaseIdBox.Text.Trim(),
            AirtableTableBox.Text.Trim(),
            string.IsNullOrWhiteSpace(AirtableViewBox.Text) ? null : AirtableViewBox.Text.Trim());
        await _dataSourceSettingsStore.SaveAsync(new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty));
        await _airtableApiSettingsStore.SaveAsync(settings);
        if (RememberAirtableTokenCheckBox.IsChecked == true)
        {
            _airtableCredentialStore.Save(token);
        }
        else
        {
            _airtableCredentialStore.Delete();
        }

        return await _airtableApiClient.GetRecordsAsync(settings, token);
    }

    private async Task RestoreMappingsAsync()
    {
        var sourceHeaders = _sourceRecords
            .SelectMany(record => record.Fields.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var typeOptions = _parameterCatalog.Where(p => p.IsWritable && p.Scope == "Type").Select(p => new ParameterOption(p)).ToList();
        var instanceOptions = _parameterCatalog.Where(p => p.IsWritable && p.Scope == "Instance").Select(p => new ParameterOption(p)).ToList();
        var savedMappings = await _mappingStore.LoadAsync();

        PopulateRows(TypeMappingRows, sourceHeaders, typeOptions, savedMappings, "Type");
        PopulateRows(InstanceMappingRows, sourceHeaders, instanceOptions, savedMappings, "Instance");
    }

    private static void PopulateRows(
        ObservableCollection<MappingRow> rows,
        IReadOnlyList<string> sourceHeaders,
        IReadOnlyList<ParameterOption> targetOptions,
        IReadOnlyList<ParameterMappingDefinition> savedMappings,
        string scope)
    {
        rows.Clear();
        foreach (var header in sourceHeaders)
        {
            var mapping = savedMappings.FirstOrDefault(saved =>
                string.Equals(saved.Scope, scope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(saved.AirtableField, header, StringComparison.OrdinalIgnoreCase));
            var target = targetOptions.FirstOrDefault(option =>
                mapping is not null && string.Equals(option.Descriptor.Name, mapping.RevitParameter, StringComparison.OrdinalIgnoreCase));
            rows.Add(new MappingRow(sourceHeaders, targetOptions)
            {
                SelectedAirtableField = header,
                SelectedTarget = target,
                SelectedConversion = mapping?.Conversion ?? "Auto (Revit spec)",
                Enabled = target is not null && mapping!.Enabled
            });
        }
    }

    private void AutoMap_Click(object sender, RoutedEventArgs e)
    {
        var mapped = AutoMapUnmapped(TypeMappingRows) + AutoMapUnmapped(InstanceMappingRows);
        ConnectStatusText.Text = $"Auto-mapped {mapped:N0} additional columns by exact name match.";
    }

    private static int AutoMapUnmapped(ObservableCollection<MappingRow> rows)
    {
        var used = rows.Where(r => r.SelectedTarget is not null)
            .Select(r => r.SelectedTarget!.Descriptor.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapped = 0;
        foreach (var row in rows.Where(r => r.SelectedTarget is null))
        {
            var target = row.TargetOptions.FirstOrDefault(option =>
                !used.Contains(option.Descriptor.Name) &&
                string.Equals(option.Descriptor.Name, row.SelectedAirtableField, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                continue;
            }

            row.SelectedTarget = target;
            row.Enabled = true;
            used.Add(target.Descriptor.Name);
            mapped++;
        }

        return mapped;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (_typeScan is null || _instanceScan is null)
            {
                throw new InvalidOperationException("Load the source and Revit catalogs first.");
            }

            await SaveMappingsAsync();
            TypeSyncRows.Clear();
            InstanceSyncRows.Clear();
            _pendingTypeBatch = null;
            _pendingInstanceBatch = null;
            ApplyButton.IsEnabled = false;

            var aliases = await _typeAliasStore.LoadAsync();
            var typeMatches = StableTypeMatcher.Build(_typeScan.Items, _sourceRecords, TypeKeyFields, aliases);
            var typeWriteItems = BuildTypeWriteItems(typeMatches);
            foreach (var match in typeMatches.Where(m => m.Status != "Matched"))
            {
                TypeSyncRows.Add(TypeSyncRow.FromIssue(match));
            }

            if (typeWriteItems.Count > 0)
            {
                var typePreview = await GetClient().SendAsync<ParameterWriteResult>(
                    PipeCommands.PreviewParameterWrites,
                    new ParameterWriteBatch(new ModelScanOptions(), typeWriteItems));
                foreach (var row in typePreview.Rows)
                {
                    TypeSyncRows.Add(TypeSyncRow.FromWrite(row));
                }

                var applicable = typePreview.Rows.Where(r => r.CanApply).Select(r => r.ItemIndex).ToHashSet();
                _pendingTypeBatch = new ParameterWriteBatch(
                    new ModelScanOptions(),
                    typeWriteItems.Where((_, index) => applicable.Contains(index)).ToList());
            }

            var instanceMatchPlan = InstanceMatchPlanBuilder.Build(_instanceScan.Items, _sourceRecords);
            var instanceWriteItems = BuildInstanceWriteItems(instanceMatchPlan);
            foreach (var match in instanceMatchPlan.Instances.Where(m => m.Status != "Matched"))
            {
                InstanceSyncRows.Add(InstanceSyncRow.FromIssue(match));
            }

            if (instanceWriteItems.Count > 0)
            {
                var instancePreview = await GetClient().SendAsync<InstanceParameterWriteResult>(
                    PipeCommands.PreviewInstanceParameterWrites,
                    new InstanceParameterWriteBatch(instanceWriteItems));
                var familyTypeByUniqueId = _instanceScan.Items.ToDictionary(i => i.UniqueId, i => $"{i.FamilyName} : {i.TypeName}");
                foreach (var row in instancePreview.Rows)
                {
                    InstanceSyncRows.Add(InstanceSyncRow.FromWrite(row, familyTypeByUniqueId.GetValueOrDefault(row.UniqueId, row.UniqueId)));
                }

                var applicable = instancePreview.Rows.Where(r => r.CanApply).Select(r => r.ItemIndex).ToHashSet();
                _pendingInstanceBatch = new InstanceParameterWriteBatch(
                    instanceWriteItems.Where((_, index) => applicable.Contains(index)).ToList());
            }

            var readyCount = (_pendingTypeBatch?.Items.Count ?? 0) + (_pendingInstanceBatch?.Items.Count ?? 0);
            ApplyButton.IsEnabled = readyCount > 0;
            PreviewStatusText.Text = readyCount > 0
                ? $"{readyCount:N0} values ready to apply. Review the rows below, then select Apply to Revit."
                : "No Revit values need to change, or nothing could be matched — see the rows below.";
        });
    }

    private List<ParameterWriteItem> BuildTypeWriteItems(IReadOnlyList<TypeMatch> matches)
    {
        var enabledMappings = TypeMappingRows.Where(row => row.Enabled && row.SelectedTarget is not null).ToList();
        var items = new List<ParameterWriteItem>();
        foreach (var match in matches.Where(m => m.Status == "Matched"))
        {
            foreach (var mapping in enabledMappings)
            {
                if (!match.Record!.Fields.TryGetValue(mapping.SelectedAirtableField!, out var sourceValue))
                {
                    continue;
                }

                var target = mapping.SelectedTarget!.Descriptor;
                var raw = ParameterSyncPlanBuilder.ReadSourceValue(sourceValue);
                var normalized = ImportUnitNormalizer.Normalize(
                    raw, mapping.SelectedAirtableField!, target, mapping.SelectedConversion,
                    _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(match.Record));
                if (!normalized.Success)
                {
                    continue;
                }

                items.Add(new ParameterWriteItem(
                    match.RevitType.TypeId, match.RevitType.TypeName, mapping.SelectedAirtableField!,
                    target.Name, target.Scope, mapping.SelectedConversion, normalized.Value, normalized.Message));
            }
        }

        return items;
    }

    private List<InstanceParameterWriteItem> BuildInstanceWriteItems(InstanceMatchPlan plan)
    {
        var enabledMappings = InstanceMappingRows.Where(row => row.Enabled && row.SelectedTarget is not null).ToList();
        var items = new List<InstanceParameterWriteItem>();
        foreach (var match in plan.Instances.Where(m => m.Status == "Matched"))
        {
            foreach (var mapping in enabledMappings)
            {
                if (!match.Record!.Fields.TryGetValue(mapping.SelectedAirtableField!, out var sourceValue))
                {
                    continue;
                }

                var target = mapping.SelectedTarget!.Descriptor;
                var raw = ParameterSyncPlanBuilder.ReadSourceValue(sourceValue);
                var normalized = ImportUnitNormalizer.Normalize(
                    raw, mapping.SelectedAirtableField!, target, mapping.SelectedConversion,
                    _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(match.Record));
                if (!normalized.Success)
                {
                    continue;
                }

                items.Add(new InstanceParameterWriteItem(
                    match.Instance.UniqueId, target.Name, mapping.SelectedConversion, normalized.Value, normalized.Message));
            }
        }

        return items;
    }

    private async Task SaveMappingsAsync()
    {
        var definitions = TypeMappingRows
            .Where(row => row.SelectedTarget is not null)
            .Select(row => new ParameterMappingDefinition(
                row.SelectedAirtableField!, row.SelectedTarget!.Descriptor.Name, "Type", row.SelectedConversion, row.Enabled))
            .Concat(InstanceMappingRows
                .Where(row => row.SelectedTarget is not null)
                .Select(row => new ParameterMappingDefinition(
                    row.SelectedAirtableField!, row.SelectedTarget!.Descriptor.Name, "Instance", row.SelectedConversion, row.Enabled)))
            .ToList();
        await _mappingStore.SaveAsync(definitions);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var totalItems = (_pendingTypeBatch?.Items.Count ?? 0) + (_pendingInstanceBatch?.Items.Count ?? 0);
        if (totalItems == 0)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Apply planting data to Revit?",
            Content = $"This will write {totalItems:N0} validated values to the active Revit model in one undoable transaction per scope.",
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
            var appliedParameters = 0;
            var appliedElements = 0;

            if (_pendingTypeBatch is { Items.Count: > 0 })
            {
                var result = await GetClient().SendAsync<ParameterWriteResult>(PipeCommands.ApplyParameterWrites, _pendingTypeBatch);
                TypeSyncRows.Clear();
                var history = new List<SyncedValueRecord>();
                foreach (var row in result.Rows)
                {
                    TypeSyncRows.Add(TypeSyncRow.FromWrite(row));
                    if (row.Status == "Applied")
                    {
                        var typeId = _pendingTypeBatch.Items[row.ItemIndex].TypeId;
                        history.Add(new SyncedValueRecord(SyncedValueHistoryStore.TypeKey(typeId), row.RevitParameter, row.ProposedValue));
                    }
                }

                await _historyStore.RecordAsync(history);
                appliedParameters += result.ChangedParameterCount;
                appliedElements += result.ChangedElementCount;
            }

            if (_pendingInstanceBatch is { Items.Count: > 0 })
            {
                var result = await GetClient().SendAsync<InstanceParameterWriteResult>(PipeCommands.ApplyInstanceParameterWrites, _pendingInstanceBatch);
                var familyTypeByUniqueId = (_instanceScan?.Items ?? []).ToDictionary(i => i.UniqueId, i => $"{i.FamilyName} : {i.TypeName}");
                InstanceSyncRows.Clear();
                var history = new List<SyncedValueRecord>();
                foreach (var row in result.Rows)
                {
                    InstanceSyncRows.Add(InstanceSyncRow.FromWrite(row, familyTypeByUniqueId.GetValueOrDefault(row.UniqueId, row.UniqueId)));
                    if (row.Status == "Applied")
                    {
                        history.Add(new SyncedValueRecord(SyncedValueHistoryStore.InstanceKey(row.UniqueId), row.RevitParameter, row.ProposedValue));
                    }
                }

                await _historyStore.RecordAsync(history);
                appliedParameters += result.ChangedParameterCount;
                appliedElements += result.ChangedElementCount;
            }

            _pendingTypeBatch = null;
            _pendingInstanceBatch = null;
            ApplyButton.IsEnabled = false;
            PreviewStatusText.Text = $"Applied {appliedParameters:N0} parameter values across {appliedElements:N0} Revit elements/types.";
        });
    }

    private async void PairSelected_Click(object sender, RoutedEventArgs e)
    {
        var sourceRecordId = PairSourceRecordIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sourceRecordId))
        {
            PairingStatusText.Text = "Enter the source record ID to pair with the current Revit selection first.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<PairSelectedInstanceResult>(
                PipeCommands.PairSelectedInstance,
                new PairSelectedInstanceRequest(sourceRecordId));
            PairingStatusText.Text = result.Paired
                ? $"Paired Revit element {result.UniqueId} with source record '{sourceRecordId}'. Reconnect or preview again to pick it up."
                : result.Message ?? "Pairing failed.";
        });
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
            ConnectStatusText.Text = $"Failed: {exception.Message}";
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
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
