using System.Collections.ObjectModel;
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
    private readonly AirtableCredentialStore _airtableCredentialStore = new();
    private readonly SyncedValueHistoryStore _historyStore = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;

    private IReadOnlyList<AirtableRecord> _sourceRecords = [];
    private ModelScanResult? _typeScan;
    private PlantingInstanceScanResult? _instanceScan;
    private IReadOnlyList<RevitParameterDescriptor> _parameterCatalog = [];
    private string _preferredUnitSystem = "Metric";
    private ParameterWriteBatch? _pendingTypeBatch;
    private InstanceParameterWriteBatch? _pendingInstanceBatch;
    private IReadOnlyList<TypeAlias> _typeAliases = [];
    private IReadOnlyList<ParameterMappingDefinition> _savedMappings = [];
    private AirtableApiSettings _airtableSettings = new(string.Empty, string.Empty, null);

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<MappingRow> MappingRows { get; } = [];
    public ObservableCollection<TypeSyncRow> TypeSyncRows { get; } = [];
    public ObservableCollection<InstanceSyncRow> InstanceSyncRows { get; } = [];

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
            ConnectStatusText.Text = $"Failed to open Settings: {exception.Message}";
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        var dataSourceSettings = snapshot?.DataSource ?? new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
        _airtableSettings = snapshot?.AirtableApi ?? new AirtableApiSettings(string.Empty, string.Empty, null);
        _typeAliases = snapshot?.TypeAliases ?? [];
        _savedMappings = snapshot?.ParameterMappings ?? [];
        SourceKindBox.SelectedIndex = dataSourceSettings.Kind == DataSourceKind.Excel ? 1 : 0;
        ExcelPathBox.Text = dataSourceSettings.ExcelPath;
        AirtableTokenStatusText.Text = string.IsNullOrWhiteSpace(_airtableCredentialStore.Load())
            ? "No Airtable personal access token saved — open Settings from the LIM ribbon first."
            : "Airtable personal access token: saved (managed in Settings).";
        AirtableSourceStatusText.Text = string.IsNullOrWhiteSpace(_airtableSettings.BaseId)
            ? "No planting data source saved yet — open Settings from the LIM ribbon first."
            : $"Source: base {_airtableSettings.BaseId} / table {_airtableSettings.TableIdOrName} (managed in Settings).";
        UpdateSourceVisibility();

        if (snapshot is not null)
        {
            ConnectStatusText.Text = "Data source, Airtable, mapping, and type-alias settings loaded from this project's saved settings.";
        }
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

            var dataSourceSettings = SourceKindBox.SelectedIndex == 1
                ? new DataSourceSettings(DataSourceKind.Excel, string.Empty, ExcelPathBox.Text.Trim())
                : new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
            await ProjectSettingsSync.PushAsync(
                GetClient(), dataSource: dataSourceSettings,
                preferredUnitSystem: _preferredUnitSystem);

            RestoreMappings();

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
            return await _excelClient.GetRecordsAsync(excelPath);
        }

        var token = _airtableCredentialStore.Load().Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("No Airtable personal access token saved — open Settings from the LIM ribbon first.");
        }

        if (string.IsNullOrWhiteSpace(_airtableSettings.BaseId))
        {
            throw new InvalidOperationException("No planting data source saved — open Settings from the LIM ribbon first.");
        }

        return await _airtableApiClient.GetRecordsAsync(_airtableSettings, token);
    }

    private void RestoreMappings()
    {
        var sourceHeaders = _sourceRecords
            .SelectMany(record => record.Fields.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var typeOptions = _parameterCatalog.Where(p => p.IsWritable && p.Scope == "Type").Select(p => new ParameterOption(p)).ToList();
        var instanceOptions = _parameterCatalog.Where(p => p.IsWritable && p.Scope == "Instance").Select(p => new ParameterOption(p)).ToList();

        ApplyMappings(sourceHeaders, typeOptions, instanceOptions, _savedMappings);
    }

    /// <summary>
    /// A saved (per-project) mapping always wins when one exists for a header. Otherwise, falls back
    /// to <see cref="DefaultParameterMappingCatalog"/> so a project that has never been mapped before
    /// still starts with every standard WWP column pre-wired instead of showing up entirely blank.
    /// </summary>
    private void ApplyMappings(
        IReadOnlyList<string> sourceHeaders,
        IReadOnlyList<ParameterOption> typeOptions,
        IReadOnlyList<ParameterOption> instanceOptions,
        IReadOnlyList<ParameterMappingDefinition> savedMappings)
    {
        MappingRows.Clear();
        var allOptions = typeOptions.Concat(instanceOptions).ToList();
        var usedTargetKeys = new HashSet<string>();

        foreach (var header in sourceHeaders)
        {
            var saved = savedMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.AirtableField, header, StringComparison.OrdinalIgnoreCase));

            string scope;
            ParameterOption? target;
            bool enabled;
            string conversion;

            if (saved is not null)
            {
                scope = saved.Scope;
                var savedTargetOptions = string.Equals(scope, "Instance", StringComparison.OrdinalIgnoreCase) ? instanceOptions : typeOptions;
                target = savedTargetOptions.FirstOrDefault(option =>
                    string.Equals(option.Descriptor.Name, saved.RevitParameter, StringComparison.OrdinalIgnoreCase));
                enabled = target is not null && saved.Enabled;
                conversion = saved.Conversion;
            }
            else
            {
                target = DefaultParameterMappingCatalog.FindMatch(header, allOptions, usedTargetKeys);
                scope = target?.Descriptor.Scope ?? "Type";
                enabled = target is not null;
                conversion = "Auto (Revit spec)";
            }

            if (target is not null)
            {
                usedTargetKeys.Add(DefaultParameterMappingCatalog.TargetKey(target));
            }

            MappingRows.Add(new MappingRow(sourceHeaders, typeOptions, instanceOptions, scope)
            {
                SelectedAirtableField = header,
                SelectedTarget = target,
                SelectedConversion = conversion,
                Enabled = enabled
            });
        }
    }

    private void AutoMap_Click(object sender, RoutedEventArgs e)
    {
        var allOptions = MappingRows.Count > 0
            ? MappingRows[0].TypeTargetOptions.Concat(MappingRows[0].InstanceTargetOptions).ToList()
            : [];
        var usedTargetKeys = MappingRows
            .Where(row => row.SelectedTarget is not null)
            .Select(row => DefaultParameterMappingCatalog.TargetKey(row.SelectedTarget!))
            .ToHashSet();

        var mapped = 0;
        foreach (var row in MappingRows.Where(row => row.SelectedTarget is null))
        {
            var target = DefaultParameterMappingCatalog.FindMatch(row.SelectedAirtableField!, allOptions, usedTargetKeys);
            if (target is null)
            {
                continue;
            }

            row.Scope = target.Descriptor.Scope;
            row.SelectedTarget = target;
            row.SelectedConversion = "Auto (Revit spec)";
            row.Enabled = true;
            usedTargetKeys.Add(DefaultParameterMappingCatalog.TargetKey(target));
            mapped++;
        }

        ConnectStatusText.Text = $"Auto-mapped {mapped:N0} additional column(s) using the default mapping table.";
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

            var typeMatches = StableTypeMatcher.Build(_typeScan.Items, _sourceRecords, TypeKeyFields, _typeAliases);
            var (typeWriteItems, skippedTypeFields) = BuildTypeWriteItems(typeMatches);
            foreach (var match in typeMatches.Where(m => m.Status != "Matched"))
            {
                TypeSyncRows.Add(TypeSyncRow.FromIssue(match));
            }

            foreach (var skipped in skippedTypeFields)
            {
                TypeSyncRows.Add(skipped);
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
            var (instanceWriteItems, skippedInstanceFields) = BuildInstanceWriteItems(instanceMatchPlan);
            foreach (var match in instanceMatchPlan.Instances.Where(m => m.Status != "Matched"))
            {
                InstanceSyncRows.Add(InstanceSyncRow.FromIssue(match));
            }

            foreach (var skipped in skippedInstanceFields)
            {
                InstanceSyncRows.Add(skipped);
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

    private (List<ParameterWriteItem> Items, List<TypeSyncRow> Skipped) BuildTypeWriteItems(IReadOnlyList<TypeMatch> matches)
    {
        var enabledMappings = MappingRows.Where(row => row.Enabled && row.Scope == "Type" && row.SelectedTarget is not null).ToList();
        var items = new List<ParameterWriteItem>();
        var skipped = new List<TypeSyncRow>();
        foreach (var match in matches.Where(m => m.Status == "Matched"))
        {
            foreach (var mapping in enabledMappings)
            {
                var target = mapping.SelectedTarget!.Descriptor;
                if (!match.Record!.Fields.TryGetValue(mapping.SelectedAirtableField!, out var sourceValue))
                {
                    skipped.Add(TypeSyncRow.FromSkippedField(match, mapping.SelectedAirtableField!, target.Name));
                    continue;
                }

                var raw = ParameterSyncPlanBuilder.ReadSourceValue(sourceValue);
                var normalized = ImportUnitNormalizer.Normalize(
                    raw, mapping.SelectedAirtableField!, target, mapping.SelectedConversion,
                    _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(match.Record));
                if (!normalized.Success)
                {
                    skipped.Add(TypeSyncRow.FromNormalizationFailure(
                        match, target.Name, normalized.Message ?? "The source unit could not be normalized."));
                    continue;
                }

                items.Add(new ParameterWriteItem(
                    match.RevitType.TypeId, match.RevitType.TypeName, mapping.SelectedAirtableField!,
                    target.Name, target.Scope, mapping.SelectedConversion, normalized.Value, normalized.Message));
            }
        }

        return (items, skipped);
    }

    private (List<InstanceParameterWriteItem> Items, List<InstanceSyncRow> Skipped) BuildInstanceWriteItems(InstanceMatchPlan plan)
    {
        var enabledMappings = MappingRows.Where(row => row.Enabled && row.Scope == "Instance" && row.SelectedTarget is not null).ToList();
        var items = new List<InstanceParameterWriteItem>();
        var skipped = new List<InstanceSyncRow>();
        foreach (var match in plan.Instances.Where(m => m.Status == "Matched"))
        {
            foreach (var mapping in enabledMappings)
            {
                var target = mapping.SelectedTarget!.Descriptor;
                if (!match.Record!.Fields.TryGetValue(mapping.SelectedAirtableField!, out var sourceValue))
                {
                    skipped.Add(InstanceSyncRow.FromSkippedField(match, mapping.SelectedAirtableField!, target.Name));
                    continue;
                }

                var raw = ParameterSyncPlanBuilder.ReadSourceValue(sourceValue);
                var normalized = ImportUnitNormalizer.Normalize(
                    raw, mapping.SelectedAirtableField!, target, mapping.SelectedConversion,
                    _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(match.Record));
                if (!normalized.Success)
                {
                    skipped.Add(InstanceSyncRow.FromNormalizationFailure(
                        match, target.Name, normalized.Message ?? "The source unit could not be normalized."));
                    continue;
                }

                items.Add(new InstanceParameterWriteItem(
                    match.Instance.UniqueId, target.Name, mapping.SelectedConversion, normalized.Value, normalized.Message));
            }
        }

        return (items, skipped);
    }

    private async Task SaveMappingsAsync()
    {
        _savedMappings = BuildMappingDefinitions();
        await ProjectSettingsSync.PushAsync(GetClient(), parameterMappings: _savedMappings);
    }

    private List<ParameterMappingDefinition> BuildMappingDefinitions() =>
        MappingRows
            .Where(row => row.SelectedTarget is not null)
            .Select(row => new ParameterMappingDefinition(
                row.SelectedAirtableField!, row.SelectedTarget!.Descriptor.Name, row.Scope, row.SelectedConversion, row.Enabled))
            .ToList();

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

    private static readonly HashSet<string> FailedInstanceStatuses = new(StringComparer.Ordinal)
    {
        "NotPaired", "Duplicate", "Orphaned", "Invalid"
    };

    private IReadOnlyList<string> GetFailedInstanceUniqueIds() =>
        InstanceSyncRows
            .Where(row => FailedInstanceStatuses.Contains(row.Status) && !string.IsNullOrEmpty(row.UniqueId))
            .Select(row => row.UniqueId)
            .Distinct()
            .ToList();

    private async void SelectFailedInstances_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetFailedInstanceUniqueIds();
        if (ids.Count == 0)
        {
            InstanceActionStatusText.Text = "No failed instance rows to select.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<OperationResult>(PipeCommands.SelectElements, new ElementSelectionRequest(ids));
            InstanceActionStatusText.Text = result.Message ?? "Selected failed elements.";
        });
    }

    private async void ZoomFailedInstances_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetFailedInstanceUniqueIds();
        if (ids.Count == 0)
        {
            InstanceActionStatusText.Text = "No failed instance rows to zoom to.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ZoomToElements, new ElementSelectionRequest(ids));
            InstanceActionStatusText.Text = result.Message ?? "Zoomed to failed elements.";
        });
    }

    private async void ColourFailedInstances_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetFailedInstanceUniqueIds();
        if (ids.Count == 0)
        {
            InstanceActionStatusText.Text = "No failed instance rows to colour.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var statusByUniqueId = ids.ToDictionary(id => id, _ => "Failed");
            var result = await GetClient().SendAsync<OperationResult>(
                PipeCommands.ApplyStatusColourOverrides, new StatusColourOverrideRequest(statusByUniqueId));
            InstanceActionStatusText.Text = result.Message ?? $"Applied red colour overrides to {ids.Count:N0} element(s).";
        });
    }

    private async void ResetFailedColours_Click(object sender, RoutedEventArgs e)
    {
        var allIds = InstanceSyncRows
            .Where(row => !string.IsNullOrEmpty(row.UniqueId))
            .Select(row => row.UniqueId)
            .Distinct()
            .ToList();
        if (allIds.Count == 0)
        {
            InstanceActionStatusText.Text = "No instance rows to reset.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ResetColourOverrides, new ElementSelectionRequest(allIds));
            InstanceActionStatusText.Text = result.Message ?? "Colour overrides reset.";
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
