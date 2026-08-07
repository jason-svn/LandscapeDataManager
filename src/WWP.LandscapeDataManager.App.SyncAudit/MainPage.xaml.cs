using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.SyncAudit;

public sealed partial class MainPage : Page
{
    private static readonly string[] TypeKeyFields = ["Types", "Planting type", "Species names", "species", "type"];
    private const string SignatureEngineVersion = "LIM-iTreeCalculator-v1"; // must match App.ITreeCalculator exactly
    private static readonly TimeSpan CatalogueStaleAfter = TimeSpan.FromDays(30);

    private readonly ExcelClient _excelClient = new();
    private readonly AirtableApiClient _airtableApiClient = new();
    private readonly DataSourceSettingsStore _dataSourceSettingsStore = new();
    private readonly AirtableApiSettingsStore _airtableApiSettingsStore = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();
    private readonly ParameterMappingStore _mappingStore = new();
    private readonly TypeAliasStore _typeAliasStore = new();
    private readonly ITreeApiClient _iTreeApiClient = new();
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly SpeciesCatalogueDatabase _catalogueDatabase = new();
    private readonly SyncedValueHistoryStore _historyStore = new();
    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";
    private List<ParameterWriteItem> _typeWriteItems = [];
    private List<InstanceParameterWriteItem> _instanceWriteItems = [];

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<AuditRow> ReportRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var syncedFromProject = await ProjectSettingsSync.PullAndApplyAsync(
            GetClient(), _dataSourceSettingsStore, _airtableApiSettingsStore, _mappingStore, _typeAliasStore);

        var dataSourceSettings = await _dataSourceSettingsStore.LoadAsync();
        var airtableSettings = await _airtableApiSettingsStore.LoadAsync();
        SourceKindBox.SelectedIndex = dataSourceSettings.Kind == DataSourceKind.Excel ? 1 : 0;
        ExcelPathBox.Text = dataSourceSettings.ExcelPath;
        AirtableBaseIdBox.Text = airtableSettings.BaseId;
        AirtableTableBox.Text = airtableSettings.TableIdOrName;
        AirtableTokenStatusText.Text = string.IsNullOrWhiteSpace(_airtableCredentialStore.Load())
            ? "No Airtable personal access token saved — open Settings from the LIM ribbon first."
            : "Airtable personal access token: saved (managed in Settings).";
        UpdateSourceVisibility();

        if (syncedFromProject)
        {
            StatusText.Text = "Data source, Airtable, mapping, and type-alias settings loaded from this project's saved settings.";
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

    private async void ExportConnectionSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "LIM data source settings"
        };
        picker.FileTypeChoices.Add("Connection settings", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var dataSourceSettings = new DataSourceSettings(
            SourceKindBox.SelectedIndex == 1 ? DataSourceKind.Excel : DataSourceKind.Airtable,
            string.Empty,
            ExcelPathBox.Text.Trim());
        var airtableSettings = new AirtableApiSettings(AirtableBaseIdBox.Text.Trim(), AirtableTableBox.Text.Trim(), null);
        var bundle = new ConnectionSettingsBundle(dataSourceSettings, airtableSettings);

        await using var stream = File.Create(file.Path);
        await JsonSerializer.SerializeAsync(stream, bundle, new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = true });
        StatusText.Text = $"Exported connection settings to {file.Path}.";
    }

    private async void ImportConnectionSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        ConnectionSettingsBundle? bundle;
        await using (var stream = File.OpenRead(file.Path))
        {
            bundle = await JsonSerializer.DeserializeAsync<ConnectionSettingsBundle>(stream, JsonDefaults.Options);
        }

        if (bundle is null)
        {
            StatusText.Text = $"Could not read connection settings from {file.Path}.";
            return;
        }

        await _dataSourceSettingsStore.SaveAsync(bundle.DataSource);
        await _airtableApiSettingsStore.SaveAsync(bundle.Airtable);
        await ProjectSettingsSync.PushAsync(GetClient(), _dataSourceSettingsStore, _airtableApiSettingsStore);

        SourceKindBox.SelectedIndex = bundle.DataSource.Kind == DataSourceKind.Excel ? 1 : 0;
        ExcelPathBox.Text = bundle.DataSource.ExcelPath;
        AirtableBaseIdBox.Text = bundle.Airtable.BaseId;
        AirtableTableBox.Text = bundle.Airtable.TableIdOrName;
        UpdateSourceVisibility();

        StatusText.Text = $"Imported connection settings from {file.Path}.";
    }

    private async void ExportTypeAliases_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "LIM type aliases"
        };
        picker.FileTypeChoices.Add("Type aliases", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var aliases = await _typeAliasStore.LoadAsync();
        await _typeAliasStore.ExportToAsync(file.Path, aliases);
        StatusText.Text = $"Exported {aliases.Count:N0} type aliases to {file.Path}.";
    }

    private async void ImportTypeAliases_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var imported = await _typeAliasStore.ImportFromAsync(file.Path);
        await _typeAliasStore.SaveAsync(imported);
        await ProjectSettingsSync.PushAsync(GetClient(), typeAliasStore: _typeAliasStore);
        StatusText.Text = $"Imported {imported.Count:N0} type aliases from {file.Path}.";
    }

    /// <summary>Bundles the non-secret connection settings a teammate needs to reconnect to the same source. The Airtable API token itself is never included.</summary>
    private sealed record ConnectionSettingsBundle(DataSourceSettings DataSource, AirtableApiSettings Airtable);

    private async Task<IReadOnlyList<AirtableRecord>> LoadSourceRecordsAsync()
    {
        if (SourceKindBox.SelectedIndex == 1)
        {
            return await _excelClient.GetRecordsAsync(ExcelPathBox.Text.Trim());
        }

        var token = _airtableCredentialStore.Load().Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("No Airtable personal access token saved — open Settings from the LIM ribbon first.");
        }

        var settings = new AirtableApiSettings(AirtableBaseIdBox.Text.Trim(), AirtableTableBox.Text.Trim(), null);
        return await _airtableApiClient.GetRecordsAsync(settings, token);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var sourceRecords = await LoadSourceRecordsAsync();
        var options = new ModelScanOptions();

        var typeScanTask = GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, options);
        var instanceScanTask = GetClient().SendAsync<PlantingInstanceScanResult>(PipeCommands.ScanPlantingInstances);
        var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, options);
        var validationTask = GetClient().SendAsync<ValidatePlantingInstancesResult>(PipeCommands.ValidatePlantingInstances, null);
        await Task.WhenAll(typeScanTask, instanceScanTask, catalogTask, validationTask);

        var typeScan = await typeScanTask;
        var instanceScan = await instanceScanTask;
        var catalog = await catalogTask;
        var validation = await validationTask;
        _preferredUnitSystem = catalog.PreferredUnitSystem;
        _preferredCurrency = catalog.PreferredCurrency;

        var mappings = await _mappingStore.LoadAsync();
        var aliases = await _typeAliasStore.LoadAsync();
        var typeMappings = mappings.Where(m => string.Equals(m.Scope, "Type", StringComparison.OrdinalIgnoreCase) && m.Enabled).ToList();
        var instanceMappings = mappings.Where(m => string.Equals(m.Scope, "Instance", StringComparison.OrdinalIgnoreCase) && m.Enabled).ToList();

        ReportRows.Clear();
        _typeWriteItems = [];
        _instanceWriteItems = [];

        await CompareTypesAsync(typeScan, sourceRecords, typeMappings, aliases, catalog);
        await CompareInstancesAsync(instanceScan, sourceRecords, instanceMappings, catalog);
        CompareCalculationStaleness(validation);
        await CompareCatalogueStalenessAsync();

        UpdateCounts();
        StatusText.Text = $"{ReportRows.Count:N0} audit rows. Source records disappearing from a previous sync are reported, never deleted from Revit.";
    });

    private async Task CompareTypesAsync(
        ModelScanResult typeScan,
        IReadOnlyList<AirtableRecord> sourceRecords,
        IReadOnlyList<ParameterMappingDefinition> typeMappings,
        IReadOnlyList<TypeAlias> aliases,
        ParameterCatalogResult catalog)
    {
        if (typeMappings.Count == 0)
        {
            return;
        }

        var matches = StableTypeMatcher.Build(typeScan.Items, sourceRecords, TypeKeyFields, aliases);
        foreach (var match in matches.Where(m => m.Status == "Matched"))
        {
            foreach (var mapping in typeMappings)
            {
                if (!match.Record!.Fields.TryGetValue(mapping.AirtableField, out var sourceValue))
                {
                    continue;
                }

                var target = catalog.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, mapping.RevitParameter, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Scope, "Type", StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    continue;
                }

                var raw = ParameterSyncPlanBuilder.ReadSourceValue(sourceValue);
                var normalized = ImportUnitNormalizer.Normalize(
                    raw, mapping.AirtableField, target, mapping.Conversion,
                    _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(match.Record));
                if (!normalized.Success)
                {
                    continue;
                }

                _typeWriteItems.Add(new ParameterWriteItem(
                    match.RevitType.TypeId, match.RevitType.TypeName, mapping.AirtableField,
                    target.Name, target.Scope, mapping.Conversion, normalized.Value, normalized.Message));
            }
        }

        if (_typeWriteItems.Count == 0)
        {
            return;
        }

        var preview = await GetClient().SendAsync<ParameterWriteResult>(
            PipeCommands.PreviewParameterWrites, new ParameterWriteBatch(new ModelScanOptions(), _typeWriteItems));
        var targetKeys = _typeWriteItems.Select(item => SyncedValueHistoryStore.TypeKey(item.TypeId)).Distinct().ToList();
        var history = await _historyStore.GetForTargetsAsync(targetKeys);

        for (var index = 0; index < preview.Rows.Count; index++)
        {
            var row = preview.Rows[index];
            var targetKey = SyncedValueHistoryStore.TypeKey(_typeWriteItems[row.ItemIndex].TypeId);
            var previous = history.GetValueOrDefault((targetKey, row.RevitParameter));
            var diff = ThreeWayDiffClassifier.Classify(previous, row.CurrentValue, row.ProposedValue);
            if (diff.Category == "Unchanged" && row.Status == "Invalid")
            {
                continue; // an invalid write candidate with nothing actually different isn't worth reporting
            }

            ReportRows.Add(new AuditRow
            {
                Category = diff.Category,
                Scope = "Type",
                Target = row.TypeName,
                Parameter = row.RevitParameter,
                PreviousValue = previous ?? "(never synced)",
                CurrentValue = row.CurrentValue,
                LatestValue = row.ProposedValue,
                ProposedAction = ProposedActionFor(diff),
                Message = row.Message ?? string.Empty,
                TypeWriteIndex = diff.Category is "Changed" or "Conflict" ? index : null,
                TargetKey = targetKey
            });
        }
    }

    private static string ProposedActionFor(ThreeWayDiffResult diff) => diff.Category switch
    {
        "Changed" => "Auto-apply the latest source value",
        "Conflict" => "Needs a decision: Keep Revit value or Accept source value",
        _ => "None"
    };

    private async Task CompareInstancesAsync(
        PlantingInstanceScanResult instanceScan,
        IReadOnlyList<AirtableRecord> sourceRecords,
        IReadOnlyList<ParameterMappingDefinition> instanceMappings,
        ParameterCatalogResult catalog)
    {
        var plan = InstanceMatchPlanBuilder.Build(instanceScan.Items, sourceRecords);
        var familyTypeByUniqueId = instanceScan.Items.ToDictionary(i => i.UniqueId, i => $"{i.FamilyName} : {i.TypeName}");

        foreach (var match in plan.Instances.Where(m => m.Status is "Orphaned" or "NotPaired" or "Duplicate"))
        {
            var category = match.Status == "Orphaned" ? "Removed from source" : "Not yet paired";
            var message = match.Status switch
            {
                "Orphaned" => $"Paired source record '{match.Instance.SourceRecordId}' was not found in the latest pull. Not deleted — resolve manually.",
                "Duplicate" => "More than one Revit instance is paired to the same source record.",
                _ => "Select this instance in Revit and pair it (Planting Data Sync tool)."
            };
            ReportRows.Add(new AuditRow
            {
                Category = category,
                Scope = "Instance",
                Target = familyTypeByUniqueId.GetValueOrDefault(match.Instance.UniqueId, match.Instance.UniqueId),
                Message = message,
                UniqueId = match.Status == "Orphaned" ? match.Instance.UniqueId : null
            });

            if (match.Status == "Orphaned")
            {
                _instanceWriteItems.Add(new InstanceParameterWriteItem(
                    match.Instance.UniqueId, "!_S_PLT_DataSync_Status_Text", "Text", "Orphaned"));
            }
        }

        foreach (var record in plan.UnpairedSourceRecords)
        {
            ReportRows.Add(new AuditRow
            {
                Category = "Added",
                Scope = "Instance",
                Target = record.Id,
                Message = "New source record with no paired Revit instance yet. Pair it in the Planting Data Sync tool."
            });
        }

        if (instanceMappings.Count > 0)
        {
            var matchedStartIndex = _instanceWriteItems.Count;
            foreach (var match in plan.Instances.Where(m => m.Status == "Matched"))
            {
                foreach (var mapping in instanceMappings)
                {
                    if (!match.Record!.Fields.TryGetValue(mapping.AirtableField, out var sourceValue))
                    {
                        continue;
                    }

                    var target = catalog.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, mapping.RevitParameter, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Scope, "Instance", StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        continue;
                    }

                    var raw = ParameterSyncPlanBuilder.ReadSourceValue(sourceValue);
                    var normalized = ImportUnitNormalizer.Normalize(
                        raw, mapping.AirtableField, target, mapping.Conversion,
                        _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(match.Record));
                    if (!normalized.Success)
                    {
                        continue;
                    }

                    _instanceWriteItems.Add(new InstanceParameterWriteItem(
                        match.Instance.UniqueId, target.Name, mapping.Conversion, normalized.Value, normalized.Message));
                }
            }

            if (_instanceWriteItems.Count > matchedStartIndex)
            {
                var mappedItems = _instanceWriteItems.Skip(matchedStartIndex).ToList();
                var preview = await GetClient().SendAsync<InstanceParameterWriteResult>(
                    PipeCommands.PreviewInstanceParameterWrites, new InstanceParameterWriteBatch(mappedItems));
                var targetKeys = mappedItems.Select(item => SyncedValueHistoryStore.InstanceKey(item.UniqueId)).Distinct().ToList();
                var history = await _historyStore.GetForTargetsAsync(targetKeys);

                for (var index = 0; index < preview.Rows.Count; index++)
                {
                    var row = preview.Rows[index];
                    var previous = history.GetValueOrDefault((row.UniqueId, row.RevitParameter));
                    var diff = ThreeWayDiffClassifier.Classify(previous, row.CurrentValue, row.ProposedValue);
                    if (diff.Category == "Unchanged" && row.Status == "Invalid")
                    {
                        continue;
                    }

                    ReportRows.Add(new AuditRow
                    {
                        Category = diff.Category,
                        Scope = "Instance",
                        Target = familyTypeByUniqueId.GetValueOrDefault(row.UniqueId, row.UniqueId),
                        Parameter = row.RevitParameter,
                        PreviousValue = previous ?? "(never synced)",
                        CurrentValue = row.CurrentValue,
                        LatestValue = row.ProposedValue,
                        ProposedAction = ProposedActionFor(diff),
                        Message = row.Message ?? string.Empty,
                        InstanceWriteIndex = diff.Category is "Changed" or "Conflict" ? matchedStartIndex + index : null,
                        TargetKey = row.UniqueId
                    });
                }
            }
        }
    }

    private void CompareCalculationStaleness(ValidatePlantingInstancesResult validation)
    {
        foreach (var item in validation.Items)
        {
            var status = PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion);
            if (status.Status == "Stale")
            {
                ReportRows.Add(new AuditRow
                {
                    Category = "Calculation stale",
                    Scope = "Instance",
                    Target = $"{item.FamilyName} : {item.TypeName}",
                    Message = status.Details ?? "Inputs changed since the last i-Tree calculation.",
                    UniqueId = item.UniqueId
                });
            }
        }
    }

    private async Task CompareCatalogueStalenessAsync()
    {
        var metadata = await _catalogueDatabase.GetMetadataAsync();
        var isStale = metadata.DownloadedAtUtc is null ||
                      !DateTimeOffset.TryParse(metadata.DownloadedAtUtc, out var downloaded) ||
                      DateTimeOffset.UtcNow - downloaded > CatalogueStaleAfter;
        if (isStale)
        {
            ReportRows.Add(new AuditRow
            {
                Category = "Catalogue stale",
                Scope = "Species",
                Target = "i-Tree species catalogue",
                Message = metadata.DownloadedAtUtc is null
                    ? "No local catalogue has been downloaded yet."
                    : $"Last downloaded {metadata.DownloadedAtUtc}, more than {CatalogueStaleAfter.Days} days ago."
            });
        }
    }

    private void UpdateCounts()
    {
        int Count(string category) => ReportRows.Count(row => row.Category == category);
        AddedCountText.Text = Count("Added").ToString("N0");
        ChangedCountText.Text = Count("Changed").ToString("N0");
        RemovedCountText.Text = Count("Removed from source").ToString("N0");
        CalcStaleCountText.Text = Count("Calculation stale").ToString("N0");
        CatalogueStaleCountText.Text = Count("Catalogue stale").ToString("N0");
        UnchangedCountText.Text = Count("Unchanged").ToString("N0");
        ConflictCountText.Text = Count("Conflict").ToString("N0");
    }

    private async void SyncLatest_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        // Only "Changed" rows (Revit still matches what was last synced; only the source moved)
        // are safe to auto-apply. "Conflict" rows need an explicit Reapply/Accept or Keep decision.
        var changedRows = ReportRows.Where(row => row.Category == "Changed").ToList();
        var typeItems = changedRows.Where(r => r.TypeWriteIndex is not null).Select(r => _typeWriteItems[r.TypeWriteIndex!.Value]).ToList();
        var instanceItems = changedRows.Where(r => r.InstanceWriteIndex is not null).Select(r => _instanceWriteItems[r.InstanceWriteIndex!.Value]).ToList();
        var applied = await ApplyWritesAsync(typeItems, instanceItems);
        ActionStatusText.Text = $"Applied {applied:N0} Changed value(s) plus any newly-orphaned markers. Conflicts were left for you to decide.";
    });

    private async void ReapplySelected_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var selected = ReportList.SelectedItems.Cast<AuditRow>().ToList();
        var typeIndices = selected.Where(r => r.TypeWriteIndex is not null).Select(r => r.TypeWriteIndex!.Value).ToHashSet();
        var instanceIndices = selected.Where(r => r.InstanceWriteIndex is not null).Select(r => r.InstanceWriteIndex!.Value).ToHashSet();

        var typeItems = _typeWriteItems.Where((_, index) => typeIndices.Contains(index)).ToList();
        var instanceItems = _instanceWriteItems.Where((_, index) => instanceIndices.Contains(index)).ToList();
        var applied = await ApplyWritesAsync(typeItems, instanceItems);
        ActionStatusText.Text = $"Reapplied {applied:N0} selected value(s) — this accepts the latest source value for any selected Conflict rows.";
    });

    private async void KeepRevitValue_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var selected = ReportList.SelectedItems.Cast<AuditRow>()
            .Where(row => row.Category == "Conflict" && row.TargetKey is not null)
            .ToList();
        if (selected.Count == 0)
        {
            ActionStatusText.Text = "Select one or more Conflict rows first.";
            return;
        }

        // No Revit write at all — just accept the current Revit value as the new synced baseline
        // so this field stops being reported as a conflict next time.
        var history = selected.Select(row => new SyncedValueRecord(row.TargetKey!, row.Parameter, row.CurrentValue)).ToList();
        await _historyStore.RecordAsync(history);
        ActionStatusText.Text = $"Kept the current Revit value for {selected.Count:N0} field(s). Select Refresh &amp; compare to confirm.";
    });

    private async Task<int> ApplyWritesAsync(List<ParameterWriteItem> typeItems, List<InstanceParameterWriteItem> instanceItems)
    {
        var applied = 0;
        if (typeItems.Count > 0)
        {
            var result = await GetClient().SendAsync<ParameterWriteResult>(
                PipeCommands.ApplyParameterWrites, new ParameterWriteBatch(new ModelScanOptions(), typeItems));
            var history = result.Rows
                .Where(row => row.Status == "Applied")
                .Select(row => new SyncedValueRecord(SyncedValueHistoryStore.TypeKey(typeItems[row.ItemIndex].TypeId), row.RevitParameter, row.ProposedValue))
                .ToList();
            await _historyStore.RecordAsync(history);
            applied += result.ChangedParameterCount;
        }

        if (instanceItems.Count > 0)
        {
            var result = await GetClient().SendAsync<InstanceParameterWriteResult>(
                PipeCommands.ApplyInstanceParameterWrites, new InstanceParameterWriteBatch(instanceItems));
            var history = result.Rows
                .Where(row => row.Status == "Applied")
                .Select(row => new SyncedValueRecord(SyncedValueHistoryStore.InstanceKey(row.UniqueId), row.RevitParameter, row.ProposedValue))
                .ToList();
            await _historyStore.RecordAsync(history);
            applied += result.ChangedParameterCount;
        }

        return applied;
    }

    private async void RecalculateStale_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var apiKey = _iTreeCredentialStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ActionStatusText.Text = "No i-Tree API key saved — open Settings from the LIM ribbon first.";
            return;
        }

        var staleUniqueIds = ReportRows
            .Where(row => row.Category == "Calculation stale" && row.UniqueId is not null)
            .Select(row => row.UniqueId!)
            .ToList();
        if (staleUniqueIds.Count == 0)
        {
            ActionStatusText.Text = "No stale calculations to recalculate.";
            return;
        }

        var validation = await GetClient().SendAsync<ValidatePlantingInstancesResult>(PipeCommands.ValidatePlantingInstances, null);
        var staleItems = validation.Items.Where(item => staleUniqueIds.Contains(item.UniqueId)).ToList();

        var signedInputs = staleItems.Select(item => (
            Signature: PlantingInstanceStatusEvaluator.Evaluate(item, SignatureEngineVersion).InputSignature,
            Input: new ITreeRevitInput(
                item.SpeciesCode ?? string.Empty, string.Empty, string.Empty, item.FamilyName, item.TypeName, 0,
                item.Condition ?? string.Empty, item.DbhInches ?? 0, item.Latitude ?? 0, item.Longitude ?? 0,
                item.Years ?? 1, item.CrownExposure ?? 0))).ToList();

        var outcomes = await _iTreeApiClient.CalculateForInstancesAsync(signedInputs, apiKey);
        var exchangeRate = await _exchangeRateService.GetUsdRateAsync(_preferredCurrency);
        var writeItems = new List<InstanceParameterWriteItem>();
        foreach (var (item, (signature, _)) in staleItems.Zip(signedInputs))
        {
            var outcome = outcomes.GetValueOrDefault(signature);
            var status = outcome?.Error is null ? "Calculated" : "APIError";
            writeItems.AddRange(ITreeInstanceResultMapper.BuildWriteItems(
                item.UniqueId, status, outcome?.Error, signature, outcome,
                _preferredUnitSystem, exchangeRate.CurrencyCode, exchangeRate.UsdRate));
        }

        await GetClient().SendAsync<InstanceParameterWriteResult>(
            PipeCommands.ApplyInstanceParameterWrites, new InstanceParameterWriteBatch(writeItems));
        var rateNote = exchangeRate.Success ? string.Empty : $" ({exchangeRate.Error})";
        ActionStatusText.Text = $"Recalculated {staleItems.Count:N0} stale tree(s) in {exchangeRate.CurrencyCode}. Select Refresh &amp; compare to see updated status.{rateNote}";
    });

    private async void RefreshCatalogue_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var apiKey = _iTreeCredentialStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ActionStatusText.Text = "No i-Tree API key saved — open Settings from the LIM ribbon first.";
            return;
        }

        var download = await _iTreeApiClient.DownloadSpeciesCatalogAsync(apiKey);
        var records = download.Records.Select(record =>
        {
            string Get(string key) => record.Fields.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
            var replaceBy = Get("ReplaceBy");
            return new SpeciesCatalogueRecord(record.SpeciesCode, Get("Common_Name"), Get("Scientific_Name"), Get("SpeciesType"),
                string.IsNullOrWhiteSpace(replaceBy) ? null : replaceBy);
        }).ToList();

        var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', records.OrderBy(r => r.SpeciesCode).Select(r => r.SpeciesCode)))));
        await _catalogueDatabase.SaveAsync(records, version);
        ActionStatusText.Text = $"Refreshed the local species catalogue: {records.Count:N0} species cached.";
    });

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = $"Audit Report {DateTime.Now:yyyy-MM-dd}" };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Category,Scope,Target,Parameter,PreviousValue,CurrentValue,LatestValue,ProposedAction,Message");
        foreach (var row in ReportRows)
        {
            builder.AppendLine(string.Join(',', row.Category, row.Scope, Csv(row.Target), Csv(row.Parameter),
                Csv(row.PreviousValue), Csv(row.CurrentValue), Csv(row.LatestValue), Csv(row.ProposedAction), Csv(row.Message)));
        }

        await Windows.Storage.FileIO.WriteTextAsync(file, builder.ToString());
        StatusText.Text = $"Exported {ReportRows.Count:N0} rows to {file.Name}.";
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

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
            StatusText.Text = $"Failed: {exception.Message}";
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
