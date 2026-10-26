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
    private readonly ExchangeRateService _exchangeRateService = new();

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

    /// <summary>Floor types pinned to the sheet row Floor Calculator matched them to (<see cref="FloorMatchAliases"/>).</summary>
    private IReadOnlyList<TypeAlias> _floorMatchAliases = [];
    private IReadOnlyList<ParameterMappingDefinition> _savedMappings = [];
    private AirtableApiSettings _airtableSettings = new(string.Empty, string.Empty, null);
    private InstanceMatchKeySettings? _instanceMatchKey;
    private bool _addingTreeTypes;

    // Money from the source goes into the project currency (see LoadMoneyContextAsync).
    private IReadOnlyDictionary<string, string> _fieldCurrencies = new Dictionary<string, string>();
    private CurrencyFactors? _currencyFactors;

    private const string SourceRecordIdParameter = "!_S_PLT_DataSync_SourceRecordId_Text";

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<MappingRow> MappingRows { get; } = [];
    public ObservableCollection<TypeSyncRow> TypeSyncRows { get; } = [];
    public ObservableCollection<InstanceSyncRow> InstanceSyncRows { get; } = [];
    public ObservableCollection<NewTreeTypeRow> NewTreeTypeRows { get; } = [];

    // Everything the last preview/apply reported, split into real changes and the unmatched/skipped
    // rows that would otherwise bury them. Only changes are shown unless "Show unmatched" is ticked.
    private readonly List<TypeSyncRow> _typeChangeRows = [];
    private readonly List<TypeSyncRow> _typeIssueRows = [];
    private readonly List<InstanceSyncRow> _instanceChangeRows = [];
    private readonly List<InstanceSyncRow> _instanceIssueRows = [];
    public ObservableCollection<ParameterOption> InstanceKeyParameterOptions { get; } = [];
    public ObservableCollection<string> SourceHeaderOptions { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        _pipeName = pipeName;
        Loaded += Page_Loaded;
    }

    private void SourcePanelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (WorkflowTabs is null || SourceTab is null)
        {
            return;
        }

        if (SourcePanelToggle.IsChecked == true)
        {
            WorkflowTabs.SelectedItem = SourceTab;
        }
        else if (ReferenceEquals(WorkflowTabs.SelectedItem, SourceTab))
        {
            WorkflowTabs.SelectedIndex = 0;
        }
    }

    private void WorkflowTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcePanelToggle is not null && SourceTab is not null)
        {
            SourcePanelToggle.IsChecked = ReferenceEquals(WorkflowTabs.SelectedItem, SourceTab);
        }
    }

    /// <summary>Header grips are tagged "Table:column", e.g. "Mapping:2".</summary>
    private void ColumnGrip_Resized(object? sender, double change)
    {
        if (sender is not FrameworkElement { Tag: string tag } || tag.Split(':') is not [var table, var column] ||
            !int.TryParse(column, out var index))
        {
            return;
        }

        var columns = table == "Mapping" ? ImporterColumns.Mapping : ImporterColumns.Results;
        columns.Resize(index, change);
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

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        var dataSourceSettings = snapshot?.DataSource ?? new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
        _airtableSettings = snapshot?.AirtableApi ?? new AirtableApiSettings(string.Empty, string.Empty, null);
        _typeAliases = snapshot?.TypeAliases ?? [];
        _savedMappings = snapshot?.ParameterMappings ?? [];
        _instanceMatchKey = snapshot?.InstanceMatchKey;
        SourceKindBox.SelectedIndex = dataSourceSettings.Kind == DataSourceKind.Excel ? 1 : 0;
        ExcelPathBox.Text = dataSourceSettings.ExcelPath;
        AirtableTokenStatusText.Text = string.IsNullOrWhiteSpace(_airtableCredentialStore.Load())
            ? "No Airtable personal access token saved — open Settings from the LIM ribbon first."
            : "Airtable personal access token: saved (managed in Settings).";
        AirtableSourceStatusText.Text = string.IsNullOrWhiteSpace(_airtableSettings.BaseId)
            ? "No planting data source saved yet — open Settings from the LIM ribbon first."
            : $"Source: base {_airtableSettings.BaseId} / table {_airtableSettings.TableIdOrName} (managed in Settings).";
        UpdateSourceVisibility();

        // Auto-connect on launch so the instance-pairing "matching box" (SourceHeaderOptions) is
        // already populated before the user does anything — previously it stayed empty until the
        // user manually clicked "Load source & Revit catalogs" first. Runs inside the same
        // RunBusyAsync as the settings pull above so a failure here (or above) always reaches
        // ConnectStatusText instead of leaving Page_Loaded's async void silently dying mid-load.
        await ConnectAsync();
    });

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

    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(ConnectAsync);

    private async Task ConnectAsync()
    {
        _sourceRecords = await LoadSourceRecordsAsync();

        // Every design option: a type's data is the type's, whichever scheme places it (a species only
        // in a secondary option would otherwise never be synced).
        var options = new ModelScanOptions(PrimaryDesignOptionsOnly: false);
        var typeScanTask = GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, options);
        var instanceScanTask = GetClient().SendAsync<PlantingInstanceScanResult>(PipeCommands.ScanPlantingInstances);
        var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, options);
        var floorsTask = GetFloorMatchesAsync();
        await Task.WhenAll(typeScanTask, instanceScanTask, catalogTask, floorsTask);

        _typeScan = await typeScanTask;
        _instanceScan = await instanceScanTask;
        var catalog = await catalogTask;
        _parameterCatalog = catalog.Parameters;
        _preferredUnitSystem = catalog.PreferredUnitSystem;
        _floorMatchAliases = FloorMatchAliases.Build(await floorsTask, _sourceRecords, TypeKeyFields);
        await LoadMoneyContextAsync(catalog.PreferredCurrency);

        var dataSourceSettings = SourceKindBox.SelectedIndex == 1
            ? new DataSourceSettings(DataSourceKind.Excel, string.Empty, ExcelPathBox.Text.Trim())
            : new DataSourceSettings(DataSourceKind.Airtable, string.Empty, string.Empty);
        await ProjectSettingsSync.PushAsync(
            GetClient(), dataSource: dataSourceSettings,
            preferredUnitSystem: _preferredUnitSystem);

        RestoreMappings();

        ConnectStatusText.Text =
            $"{_sourceRecords.Count:N0} source records · {_typeScan.Items.Count:N0} Revit types · " +
            $"{_instanceScan.Items.Count:N0} planting instances · {catalog.Parameters.Count(p => p.IsWritable):N0} writable parameters" +
            (_floorMatchAliases.Count > 0 ? $" · {_floorMatchAliases.Count:N0} floor type(s) matched to the row Floor Calculator gave them." : ".");
        await RefreshNewTreeTypesAsync();
    }

    /// <summary>
    /// What currency each money column is in (an Airtable currency field's symbol; none for Excel)
    /// and the rates into the project currency for every currency the source names. Best-effort:
    /// without a column's currency its bare numbers import as they stand, as before.
    /// </summary>
    private async Task LoadMoneyContextAsync(string projectCurrency)
    {
        _fieldCurrencies = new Dictionary<string, string>();
        if (SourceKindBox.SelectedIndex != 1)
        {
            try
            {
                _fieldCurrencies = await _airtableApiClient.GetFieldCurrenciesAsync(_airtableSettings, _airtableCredentialStore.Load().Trim());
            }
            catch (Exception exception) when (exception is AirtableInsufficientScopeException or HttpRequestException or InvalidOperationException)
            {
                // No schema scope on the token: money columns are read from their cells alone.
            }
        }

        var currencies = _fieldCurrencies.Values
            .Concat(_sourceRecords.SelectMany(record => record.Fields.Values)
                .Select(value => Money.CurrencyOf(ParameterSyncPlanBuilder.ReadSourceValue(value)))
                .OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _currencyFactors = await CurrencyFactors.LoadAsync(GetClient(), _exchangeRateService, projectCurrency, currencies);
    }

    /// <summary>Every floor with the sheet row Floor Calculator saved on it; none when the model has no floors.</summary>
    private async Task<IReadOnlyList<SelectedFloorItem>> GetFloorMatchesAsync()
    {
        try
        {
            return (await GetClient().SendAsync<SelectedFloorsResult>(PipeCommands.GetSelectedFloors, new SelectedFloorsRequest(AllFloors: true))).Items;
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    // ---- Revit tree types the Airtable source has no row for ----

    /// <summary>Lists the tree types placed in Revit with no row in the Airtable source, ticking every one that can be added.</summary>
    private async Task RefreshNewTreeTypesAsync()
    {
        NewTreeTypeRows.Clear();
        var airtable = SourceKindBox.SelectedIndex != 1 && !string.IsNullOrWhiteSpace(_airtableSettings.BaseId);
        NewTreeTypesCard.Visibility = airtable ? Visibility.Visible : Visibility.Collapsed;
        NewTreeTypesTable.Visibility = Visibility.Collapsed;
        UpdateAddNewTreeTypesButton();
        if (!airtable)
        {
            return;
        }

        // Its own status: a problem here shouldn't stop the sync the rest of the page is for.
        try
        {
            var (_, types) = await FindNewTreeTypesAsync();
            foreach (var type in types)
            {
                NewTreeTypeRows.Add(new NewTreeTypeRow(type));
            }

            foreach (var row in NewTreeTypeRows.Where(row => row.Type.Conflict is null))
            {
                NewTreeTypesList.SelectedItems.Add(row);
            }

            NewTreeTypesTable.Visibility = types.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NewTreeTypesStatusText.Text = types.Count == 0
                ? "Every tree type placed in Revit has a row in Airtable."
                : $"{types.Count:N0} tree type(s) placed in Revit have no row in Airtable.";
        }
        catch (Exception exception)
        {
            NewTreeTypesStatusText.Text = $"Couldn't check Airtable for new tree types: {exception.Message}";
        }

        UpdateAddNewTreeTypesButton();
    }

    /// <summary>
    /// The whole source table — not just the view the sync reads, so a row the view hides isn't added
    /// again — and the tree types it's missing from every design option.
    /// </summary>
    private async Task<(IReadOnlyList<AirtableRecord> Records, IReadOnlyList<NewTreeType> Types)> FindNewTreeTypesAsync()
    {
        var recordsTask = _airtableApiClient.GetRecordsAsync(_airtableSettings with { ViewName = null }, _airtableCredentialStore.Load().Trim());
        var scanTask = GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, new ModelScanOptions(PrimaryDesignOptionsOnly: false));
        await Task.WhenAll(recordsTask, scanTask);
        var records = await recordsTask;
        return (records, NewTreeTypes.Find((await scanTask).Items, records, TypeKeyFields, _typeAliases));
    }

    private void NewTreeTypesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAddNewTreeTypesButton();

    private List<NewTreeType> SelectedNewTreeTypes() =>
        NewTreeTypesList.SelectedItems.Cast<NewTreeTypeRow>().Select(row => row.Type).Where(type => type.Conflict is null).ToList();

    private void UpdateAddNewTreeTypesButton()
    {
        var count = SelectedNewTreeTypes().Count;
        AddNewTreeTypesButton.Content = count == 1 ? "Add 1 type to Airtable" : $"Add {count:N0} types to Airtable";
        AddNewTreeTypesButton.IsEnabled = count > 0 && !_addingTreeTypes;
    }

    private async void AddNewTreeTypes_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedNewTreeTypes();
        if (selected.Count == 0 || _addingTreeTypes)
        {
            return;
        }

        // A second click while rows are being added would add them twice.
        _addingTreeTypes = true;
        AddNewTreeTypesButton.IsEnabled = false;
        try
        {
            await AddNewTreeTypesAsync(selected);
        }
        finally
        {
            _addingTreeTypes = false;
            UpdateAddNewTreeTypesButton();
        }
    }

    private async Task AddNewTreeTypesAsync(List<NewTreeType> selected)
    {
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add tree types to Airtable?",
            Content = $"This adds {selected.Count:N0} row(s) to the Airtable table {_airtableSettings.TableIdOrName}, each with only the type name, \"Tree\" and its species code, for you to fill in. Nothing in Revit changes.",
            PrimaryButtonText = "Add to Airtable",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            string status;
            try
            {
                // Checked again against the table as it is now, so a row someone just added isn't added twice.
                var (records, missing) = await FindNewTreeTypesAsync();
                var wanted = selected.Select(type => (type.FamilyName, type.TypeName)).ToHashSet();
                var toAdd = missing.Where(type => type.Conflict is null && wanted.Contains((type.FamilyName, type.TypeName))).ToList();
                var added = toAdd.Count == 0
                    ? []
                    : await _airtableApiClient.CreateRecordsAsync(
                        _airtableSettings, _airtableCredentialStore.Load().Trim(), [.. toAdd.Select(type => NewTreeTypes.Fields(type, records))]);
                status = $"Added {added.Count:N0} row(s) to Airtable{(toAdd.Count > 0 ? $" ({string.Join(", ", toAdd.Select(type => type.TypeName))})" : string.Empty)}" +
                         $"{(selected.Count > toAdd.Count ? $"; {selected.Count - toAdd.Count:N0} already had one" : string.Empty)}. Fill them in there, then reload and preview.";
            }
            catch (Exception exception)
            {
                status = $"Couldn't add to Airtable: {exception.Message}";
            }

            // What was added stays on screen even if reloading fails.
            try
            {
                await ConnectAsync();
            }
            catch (Exception exception)
            {
                status += $" Reloading failed: {exception.Message}";
            }

            NewTreeTypesStatusText.Text = status;
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
        var typeOptions = _parameterCatalog.Where(p => p.IsWritable && p.Scope == "Type" && DefaultParameterMappingCatalog.IsImportTarget(p.Name)).Select(p => new ParameterOption(p)).ToList();
        var instanceOptions = _parameterCatalog.Where(p => p.IsWritable && p.Scope == "Instance" && DefaultParameterMappingCatalog.IsImportTarget(p.Name)).Select(p => new ParameterOption(p)).ToList();

        ApplyMappings(sourceHeaders, typeOptions, instanceOptions, _savedMappings);
        RestoreInstanceMatchKeyOptions(sourceHeaders);
    }

    /// <summary>
    /// Populates the pickers for bulk instance matching (any Instance-scope parameter, not just
    /// writable ones — the chosen parameter is only ever read, never written to) and restores the
    /// previously saved choice, if any.
    /// </summary>
    private void RestoreInstanceMatchKeyOptions(IReadOnlyList<string> sourceHeaders)
    {
        InstanceKeyParameterOptions.Clear();
        foreach (var option in _parameterCatalog.Where(p => p.Scope == "Instance").Select(p => new ParameterOption(p)))
        {
            InstanceKeyParameterOptions.Add(option);
        }

        SourceHeaderOptions.Clear();
        foreach (var header in sourceHeaders)
        {
            SourceHeaderOptions.Add(header);
        }

        if (!HasInstanceMatchKey)
        {
            return;
        }

        InstanceKeyParameterBox.SelectedItem = InstanceKeyParameterOptions.FirstOrDefault(option =>
            string.Equals(option.Descriptor.Name, _instanceMatchKey!.RevitParameter, StringComparison.OrdinalIgnoreCase));
        InstanceKeySourceFieldBox.SelectedItem = SourceHeaderOptions.FirstOrDefault(header =>
            string.Equals(header, _instanceMatchKey!.SourceField, StringComparison.OrdinalIgnoreCase));
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

            // A saved mapping to a renamed parameter follows the rename, keeping the user's choices.
            var savedParameter = saved is null ? null : DefaultParameterMappingCatalog.CurrentName(saved.RevitParameter);
            var savedTarget = saved is null
                ? null
                : (string.Equals(saved.Scope, "Instance", StringComparison.OrdinalIgnoreCase) ? instanceOptions : typeOptions)
                    .FirstOrDefault(option => string.Equals(option.Descriptor.Name, savedParameter, StringComparison.OrdinalIgnoreCase));

            if (saved is not null && savedTarget is not null)
            {
                target = savedTarget;
                scope = saved.Scope;
                enabled = saved.Enabled;
                conversion = saved.Conversion;
            }
            else
            {
                // Never mapped, or the saved parameter is gone (retired, or not bound in this
                // project): the default mapping, so the column isn't left blank.
                target = DefaultParameterMappingCatalog.FindMatch(header, allOptions, usedTargetKeys);
                scope = target?.Descriptor.Scope ?? saved?.Scope ?? "Type";
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
                Enabled = enabled,
                UnmappedReason = UnmappedReason(header, allOptions)
            });
        }
    }

    /// <summary>
    /// Why a column can't be mapped yet, when that's known: a sheet column deliberately not imported,
    /// or one whose LIM parameter isn't bound (writable) in this project — fixed by Import Shared Parameter.
    /// </summary>
    private static string? UnmappedReason(string header, IReadOnlyList<ParameterOption> options) =>
        DefaultParameterMappingCatalog.SkipReason(header) ??
        (DefaultParameterMappingCatalog.DefaultParameter(header) is { } parameter &&
         !options.Any(option => string.Equals(option.Descriptor.Name, parameter, StringComparison.OrdinalIgnoreCase))
            ? $"Run Import Shared Parameter: {parameter} isn't in this project yet"
            : null);

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
            _typeChangeRows.Clear();
            _typeIssueRows.Clear();
            _instanceChangeRows.Clear();
            _instanceIssueRows.Clear();
            _pendingTypeBatch = null;
            _pendingInstanceBatch = null;
            ApplyButton.IsEnabled = false;
            var hiddenUnchanged = 0;

            var typeMatches = StableTypeMatcher.Build(_typeScan.Items, _sourceRecords, TypeKeyFields, FloorMatchAliases.Merge(_typeAliases, _floorMatchAliases));
            var (typeWriteItems, skippedTypeFields) = BuildTypeWriteItems(typeMatches);
            foreach (var match in typeMatches.Where(m => m.Status != "Matched"))
            {
                _typeIssueRows.Add(TypeSyncRow.FromIssue(match));
            }

            _typeIssueRows.AddRange(skippedTypeFields);

            if (typeWriteItems.Count > 0)
            {
                var typePreview = await GetClient().SendAsync<ParameterWriteResult>(
                    PipeCommands.PreviewParameterWrites,
                    new ParameterWriteBatch(new ModelScanOptions(), typeWriteItems));
                foreach (var row in typePreview.Rows)
                {
                    if (IsUnchanged(row.Status))
                    {
                        hiddenUnchanged++;
                        continue;
                    }

                    (IsNotApplicable(row.Status) ? _typeIssueRows : _typeChangeRows).Add(TypeSyncRow.FromWrite(row));
                }

                var applicable = typePreview.Rows.Where(r => r.CanApply).Select(r => r.ItemIndex).ToHashSet();
                _pendingTypeBatch = new ParameterWriteBatch(
                    new ModelScanOptions(),
                    typeWriteItems.Where((_, index) => applicable.Contains(index)).ToList());
            }

            var instanceMatches = await BuildInstanceMatchesAsync();
            var (instanceWriteItems, skippedInstanceFields) = BuildInstanceWriteItems(instanceMatches);
            foreach (var match in instanceMatches.Where(m => m.Status != "Matched"))
            {
                _instanceIssueRows.Add(InstanceSyncRow.FromIssue(match));
            }

            _instanceIssueRows.AddRange(skippedInstanceFields);

            if (instanceWriteItems.Count > 0)
            {
                var instancePreview = await GetClient().SendAsync<InstanceParameterWriteResult>(
                    PipeCommands.PreviewInstanceParameterWrites,
                    new InstanceParameterWriteBatch(instanceWriteItems));
                var familyTypeByUniqueId = _instanceScan.Items.ToDictionary(i => i.UniqueId, i => $"{i.FamilyName} : {i.TypeName}");
                foreach (var row in instancePreview.Rows)
                {
                    if (IsUnchanged(row.Status))
                    {
                        hiddenUnchanged++;
                        continue;
                    }

                    (IsNotApplicable(row.Status) ? _instanceIssueRows : _instanceChangeRows)
                        .Add(InstanceSyncRow.FromWrite(row, familyTypeByUniqueId.GetValueOrDefault(row.UniqueId, row.UniqueId)));
                }

                var applicable = instancePreview.Rows.Where(r => r.CanApply).Select(r => r.ItemIndex).ToHashSet();
                _pendingInstanceBatch = new InstanceParameterWriteBatch(
                    instanceWriteItems.Where((_, index) => applicable.Contains(index)).ToList());
            }

            var readyCount = (_pendingTypeBatch?.Items.Count ?? 0) + (_pendingInstanceBatch?.Items.Count ?? 0);
            ApplyButton.IsEnabled = readyCount > 0;
            RefreshResultRows();
            var hiddenNote = hiddenUnchanged > 0 ? $" {hiddenUnchanged:N0} values already match the source and are hidden." : string.Empty;
            PreviewStatusText.Text = (readyCount > 0
                ? $"{readyCount:N0} values ready to apply. Review the rows below, then select Apply to Revit."
                : "No Revit values need to change, or nothing could be matched — see the rows below.") + hiddenNote;
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

                var normalized = NormalizeSourceValue(
                    ParameterSyncPlanBuilder.ReadSourceValue(sourceValue), mapping.SelectedAirtableField!, target, mapping.SelectedConversion, match.Record);
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

    /// <summary>
    /// A source value as its target takes it: money put into the project currency
    /// (<see cref="ImportMoney"/>), then units normalized (<see cref="ImportUnitNormalizer"/>).
    /// </summary>
    private ImportUnitNormalizationResult NormalizeSourceValue(
        string raw, string sourceField, RevitParameterDescriptor target, string conversion, AirtableRecord record)
    {
        var money = ImportMoney.Convert(raw, sourceField, target, _fieldCurrencies, _currencyFactors);
        if (!money.Success)
        {
            return new ImportUnitNormalizationResult(false, raw, money.Message);
        }

        var normalized = ImportUnitNormalizer.Normalize(
            money.Value, sourceField, target, conversion, _preferredUnitSystem, ParameterSyncPlanBuilder.ReadRecordUnitSystem(record));
        return money.Message is null || !normalized.Success
            ? normalized
            : normalized with { Message = normalized.Message is null ? money.Message : $"{money.Message} {normalized.Message}" };
    }

    /// <summary>
    /// Re-scans planting instances (reading the configured match parameter's live value, if any),
    /// then matches each to a source record: an instance already carrying a stable pairing from a
    /// previous sync keeps that pairing; only instances with none yet fall back to matching by the
    /// configured Revit-parameter/source-column pair, so a bulk match never overrides an existing
    /// explicit "Pair selected" pairing.
    /// </summary>
    /// <summary>True only when both halves of the bulk-match key are actually set — an empty <see cref="InstanceMatchKeySettings"/> (as written by "Clear") means "not configured," not null, since <see cref="ProjectSettingsSync.PushAsync"/> only ever merges supplied fields in, never clears one back to null.</summary>
    private bool HasInstanceMatchKey =>
        !string.IsNullOrWhiteSpace(_instanceMatchKey?.RevitParameter) && !string.IsNullOrWhiteSpace(_instanceMatchKey?.SourceField);

    private async Task<IReadOnlyList<InstanceMatch>> BuildInstanceMatchesAsync()
    {
        var scan = await GetClient().SendAsync<PlantingInstanceScanResult>(
            PipeCommands.ScanPlantingInstances,
            new PlantingInstanceScanOptions(HasInstanceMatchKey ? _instanceMatchKey!.RevitParameter : null));
        _instanceScan = scan;

        var idMatches = InstanceMatchPlanBuilder.Build(scan.Items, _sourceRecords).Instances;
        if (!HasInstanceMatchKey)
        {
            return idMatches;
        }

        var keyMatchesByUniqueId = InstanceKeyMatchPlanBuilder
            .Build(scan.Items, _sourceRecords, _instanceMatchKey!.SourceField)
            .ToDictionary(match => match.Instance.UniqueId);

        return idMatches
            .Select(match => match.Status == "NotPaired" && keyMatchesByUniqueId.TryGetValue(match.Instance.UniqueId, out var keyMatch)
                ? keyMatch
                : match)
            .ToList();
    }

    private (List<InstanceParameterWriteItem> Items, List<InstanceSyncRow> Skipped) BuildInstanceWriteItems(IReadOnlyList<InstanceMatch> matches)
    {
        var enabledMappings = MappingRows.Where(row => row.Enabled && row.Scope == "Instance" && row.SelectedTarget is not null).ToList();
        var items = new List<InstanceParameterWriteItem>();
        var skipped = new List<InstanceSyncRow>();
        foreach (var match in matches.Where(m => m.Status == "Matched"))
        {
            if (string.IsNullOrEmpty(match.Instance.SourceRecordId))
            {
                items.Add(new InstanceParameterWriteItem(
                    match.Instance.UniqueId, SourceRecordIdParameter, "Text", match.Record!.Id,
                    "Paired automatically — matched by parameter value."));
            }

            foreach (var mapping in enabledMappings)
            {
                var target = mapping.SelectedTarget!.Descriptor;
                if (!match.Record!.Fields.TryGetValue(mapping.SelectedAirtableField!, out var sourceValue))
                {
                    skipped.Add(InstanceSyncRow.FromSkippedField(match, mapping.SelectedAirtableField!, target.Name));
                    continue;
                }

                var normalized = NormalizeSourceValue(
                    ParameterSyncPlanBuilder.ReadSourceValue(sourceValue), mapping.SelectedAirtableField!, target, mapping.SelectedConversion, match.Record);
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
                _typeChangeRows.Clear();
                var history = new List<SyncedValueRecord>();
                foreach (var row in result.Rows)
                {
                    if (!IsUnchanged(row.Status))
                    {
                        _typeChangeRows.Add(TypeSyncRow.FromWrite(row));
                    }

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
                _instanceChangeRows.Clear();
                var history = new List<SyncedValueRecord>();
                foreach (var row in result.Rows)
                {
                    if (!IsUnchanged(row.Status))
                    {
                        _instanceChangeRows.Add(InstanceSyncRow.FromWrite(row, familyTypeByUniqueId.GetValueOrDefault(row.UniqueId, row.UniqueId)));
                    }

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
            RefreshResultRows();
            PreviewStatusText.Text = $"Applied {appliedParameters:N0} parameter values across {appliedElements:N0} Revit elements/types.";
        });
    }

    /// <summary>Rows whose Revit value already equals the source value — hidden from the report so only real changes and problems show.</summary>
    private static bool IsUnchanged(string status) => string.Equals(status, "No change", StringComparison.Ordinal);

    /// <summary>The parameter doesn't exist on that element's category (e.g. a Planting-only parameter on a Floor type) — not a change, so it's filed with the unmatched/skipped rows.</summary>
    private static bool IsNotApplicable(string status) => string.Equals(status, "Not applicable", StringComparison.Ordinal);

    private static readonly HashSet<string> FailedInstanceStatuses = new(StringComparer.Ordinal)
    {
        "NotPaired", "Duplicate", "Orphaned", "Invalid"
    };

    private void ShowIssueRows_Changed(object sender, RoutedEventArgs e) => RefreshResultRows();

    /// <summary>Rebuilds the two result lists: changes always, unmatched/skipped rows only when asked for.</summary>
    private void RefreshResultRows()
    {
        var showIssues = ShowIssueRowsCheckBox.IsChecked == true;
        TypeSyncRows.Clear();
        foreach (var row in showIssues ? _typeChangeRows.Concat(_typeIssueRows) : _typeChangeRows)
        {
            TypeSyncRows.Add(row);
        }

        InstanceSyncRows.Clear();
        foreach (var row in showIssues ? _instanceChangeRows.Concat(_instanceIssueRows) : _instanceChangeRows)
        {
            InstanceSyncRows.Add(row);
        }

        var issueCount = _typeIssueRows.Count + _instanceIssueRows.Count;
        ShowIssueRowsCheckBox.Content = $"Show unmatched & skipped rows ({issueCount:N0})";
    }

    private IEnumerable<InstanceSyncRow> AllInstanceRows => _instanceChangeRows.Concat(_instanceIssueRows);

    private IReadOnlyList<string> GetFailedInstanceUniqueIds() =>
        AllInstanceRows
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
        var allIds = AllInstanceRows
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

    private async void SaveInstanceMatchKey_Click(object sender, RoutedEventArgs e)
    {
        var parameterName = (InstanceKeyParameterBox.SelectedItem as ParameterOption)?.Descriptor.Name;
        var sourceField = InstanceKeySourceFieldBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(sourceField))
        {
            InstanceMatchKeyStatusText.Text = "Select both a Revit parameter and a source column first.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            _instanceMatchKey = new InstanceMatchKeySettings(parameterName, sourceField);
            await ProjectSettingsSync.PushAsync(GetClient(), instanceMatchKey: _instanceMatchKey);
            InstanceMatchKeyStatusText.Text =
                $"Matching instances where '{parameterName}' equals source column '{sourceField}'. Select Preview changes to apply it.";
        });
    }

    private async void ClearInstanceMatchKey_Click(object sender, RoutedEventArgs e)
    {
        InstanceKeyParameterBox.SelectedItem = null;
        InstanceKeySourceFieldBox.SelectedItem = null;

        await RunBusyAsync(async () =>
        {
            _instanceMatchKey = new InstanceMatchKeySettings(string.Empty, string.Empty);
            await ProjectSettingsSync.PushAsync(GetClient(), instanceMatchKey: _instanceMatchKey);
            InstanceMatchKeyStatusText.Text = "Bulk instance matching cleared — unpaired instances need pairing one at a time again.";
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
