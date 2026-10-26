using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using WWP.LandscapeDataManager.SharedUI;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

public sealed partial class MainPage : Page
{
    private const int MaxSearchResults = 25;

    private readonly WwpLdsCoefficientDatabase _coefficientDatabase = new();
    private readonly AirtableCredentialStore _credentialStore = new();
    private readonly AirtableApiClient _airtableClient = new();
    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private IReadOnlyList<WwpLdsCoefficientRecord> _allCoefficients = [];
    private WwpLdsAirtableSettings _wwpLdsSettings = WwpLdsAirtableSettings.CompanyDefault;
    private string _preferredCurrency = "USD";
    private double _preferredCurrencyFactor = 1d;
    private string _preferredUnitSystem = "Metric";
    // Rates into the project currency for the sheet's money (pounds, or the currency a cell names);
    // null until loaded, when costs are taken as they stand.
    private CurrencyFactors? _sheetCurrencies;

    // Set when the live rate lookup failed at load: costs can't be written in the project's
    // currency, so Calculate is refused rather than storing US-dollar figures labelled as it.
    private string? _rateUnavailableMessage;

    public MainPage()
    {
        InitializeComponent();
        ThemeResources.Observe(this, () =>
        {
            foreach (var row in Rows)
            {
                row.RefreshTheme();
            }
        });
    }

    public ObservableCollection<FloorAssignmentRow> Rows { get; } = [];

    /// <summary>The Results step: each loaded floor's benefits from its match.</summary>
    public ObservableCollection<FloorResultRow> ResultRows { get; } = [];

    /// <summary>Step 1 matches floors and calculates; step 2 shows what each floor gets.</summary>
    private void CoefficientSteps_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (MatchPanel is null || ResultsPanel is null)
        {
            return;
        }

        var results = sender.SelectedItem == ResultsStepItem;
        MatchToolbar.Visibility = results ? Visibility.Collapsed : Visibility.Visible;
        MatchPanel.Visibility = results ? Visibility.Collapsed : Visibility.Visible;
        ResultsSummaryText.Visibility = results ? Visibility.Visible : Visibility.Collapsed;
        ResultsPanel.Visibility = results ? Visibility.Visible : Visibility.Collapsed;
        if (results)
        {
            RebuildResults();
        }
    }

    /// <summary>The loaded floors' benefits from their matches, with the totals over the matched ones.</summary>
    private void RebuildResults()
    {
        ResultRows.Clear();
        var totals = new List<FloorBenefits>();
        foreach (var row in Rows)
        {
            var benefits = row.SelectedRecord is null ? null : BenefitsOf(row);
            if (benefits is not null)
            {
                totals.Add(benefits);
            }

            ResultRows.Add(new FloorResultRow(row, row.SelectedRecord is { } record ? new CoefficientRow(record).DisplayName : "—", benefits));
        }

        CostHeaderText.Text = $"Cost saved{Environment.NewLine}{_preferredCurrency}";
        TotalAreaText.Text = Rows.Where(row => row.SelectedRecord is not null).Sum(row => row.AreaSquareMeters).ToString("N0");
        TotalCo2Text.Text = FloorResultRow.Format(totals.Sum(total => total.Co2Kg));
        TotalRunoffText.Text = FloorResultRow.Format(totals.Sum(total => total.RunoffM3));
        TotalPollutantsText.Text = FloorResultRow.Format(totals.Sum(total => total.PollutantsKg));
        TotalOxygenText.Text = FloorResultRow.Format(totals.Sum(total => total.OxygenKg));
        TotalCostText.Text = FloorResultRow.Format(totals.Sum(total => total.CostSaved));
        TotalGwpText.Text = FloorResultRow.Format(totals.Sum(total => total.GwpKg));
        ResultsSummaryText.Text = Rows.Count == 0
            ? "Load floors in step 1 first."
            : $"{totals.Count:N0} of {Rows.Count:N0} floor(s) matched. Per year, except embodied GWP: the sheet's coefficient × area (or a value reviewed by hand), which Calculate writes to Revit. Status says whether it's written.";
    }

    private static FloorBenefits BenefitsOf(FloorAssignmentRow row) => new(
        row.Metrics[FloorAssignmentRow.Co2Index].FinalValue,
        row.Metrics[FloorAssignmentRow.RunoffIndex].FinalValue,
        row.Metrics[FloorAssignmentRow.PollutionIndex].FinalValue,
        row.Metrics[FloorAssignmentRow.OxygenIndex].FinalValue,
        row.Metrics[FloorAssignmentRow.CostSavedIndex].FinalValue,
        row.Metrics[FloorAssignmentRow.GwpIndex].FinalValue);

    /// <summary>Reused across "Review values" dialog openings — cleared and repopulated with the target row's own Metrics objects (not copies) so edits apply directly.</summary>
    public ObservableCollection<FloorMetricEntry> EditingMetrics { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _windowHandle = windowHandle;
        BngTabContent.Initialize(GetClient, pipeName);
        Loaded += Page_Loaded;
    }

    /// <summary>The BNG tab loads the current Revit selection the first time it's opened.</summary>
    private async void CalculatorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CalculatorTabs.SelectedIndex == 1)
        {
            await BngTabContent.LoadOnFirstShowAsync();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SiblingToolLauncher.ShowOrStart(Path.Combine("Settings", "WWP.LandscapeDataManager.Settings.exe"), _pipeName);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed to open Settings: {exception.Message}";
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        _wwpLdsSettings = snapshot?.WwpLdsSource ?? WwpLdsAirtableSettings.CompanyDefault;
        SourceStatusText.Text = string.IsNullOrWhiteSpace(_credentialStore.Load())
            ? "No Airtable token saved, and no landscape data sheet source configured — open Settings from the LIM ribbon first."
            : $"From {_wwpLdsSettings.BaseId} / {_wwpLdsSettings.TableIdOrName} (set in Settings)";

        await RefreshCatalogueStatusAsync();
        if (WwpLdsSheet.NeedsResync(_allCoefficients) && !string.IsNullOrWhiteSpace(_credentialStore.Load()))
        {
            // A cache synced before LIM read the sheet's "£" money (every cost saved was 0) or its tree columns.
            try
            {
                await WwpLdsSheet.SyncAsync(_wwpLdsSettings, _credentialStore.Load().Trim(), _airtableClient, _coefficientDatabase);
                await RefreshCatalogueStatusAsync();
            }
            catch (Exception exception)
            {
                StatusText.Text = $"Couldn't refresh the landscape data sheet: {exception.Message}";
            }
        }

        try
        {
            var catalog = await GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
            var rate = await ProjectExchangeRate.GetUsdRateAsync(GetClient(), _exchangeRateService, catalog.PreferredCurrency);
            _preferredCurrency = rate.CurrencyCode;
            _preferredCurrencyFactor = rate.UsdRate;
            _preferredUnitSystem = catalog.PreferredUnitSystem;
            _rateUnavailableMessage = rate.Success ? null : ProjectExchangeRate.UnavailableMessage(rate);
            if (_rateUnavailableMessage is null)
            {
                await LoadSheetCurrenciesAsync();
            }

            if (_rateUnavailableMessage is not null)
            {
                StatusText.Text = _rateUnavailableMessage;
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not resolve the preferred currency, defaulting to USD: {exception.Message}";
        }

        try
        {
            await LoadSelectedFloorsAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private async Task RefreshCatalogueStatusAsync()
    {
        _allCoefficients = await _coefficientDatabase.GetAllAsync();
        var downloadedAtUtc = await _coefficientDatabase.GetDownloadedAtUtcAsync();
        var lastUpdated = DateTimeOffset.TryParse(downloadedAtUtc, out var parsed)
            ? parsed.ToLocalTime().ToString("MMM d, yyyy h:mm tt")
            : null;

        CatalogueStatusText.Text = _allCoefficients.Count == 0
            ? "No landscape data sheet yet: Sync sheet (needs the Airtable token in Settings)"
            : lastUpdated is null
                ? $"Landscape data sheet: {_allCoefficients.Count:N0} rows"
                : $"Landscape data sheet: {_allCoefficients.Count:N0} rows, synced {lastUpdated}";
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        var token = _credentialStore.Load().Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusText.Text = "No Airtable personal access token saved — open Settings from the LIM ribbon first.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var coefficients = await WwpLdsSheet.SyncAsync(_wwpLdsSettings, token, _airtableClient, _coefficientDatabase);

            await RefreshCatalogueStatusAsync();
            if (_sheetCurrencies is not null)
            {
                // The new rows may name a currency the last load had no rate for.
                await LoadSheetCurrenciesAsync();
            }

            StatusText.Text = $"Synced {coefficients.Count:N0} landscape data sheet rows from Airtable." +
                (_rateUnavailableMessage is null ? string.Empty : " " + _rateUnavailableMessage);
        });
    }

    /// <summary>
    /// Rates for the sheet's money into the project currency; Calculate is refused (see
    /// <see cref="_rateUnavailableMessage"/>) while any currency the sheet uses has none.
    /// </summary>
    private async Task LoadSheetCurrenciesAsync()
    {
        var (factors, missing) = await WwpLdsSheet.CurrencyFactorsAsync(GetClient(), _exchangeRateService, _preferredCurrency, _allCoefficients);
        _sheetCurrencies = factors;
        _rateUnavailableMessage = missing is null ? null : $"No {missing} → {_preferredCurrency} exchange rate available for the sheet's {missing} values.";
    }

    private async void LoadSelectedFloors_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(() => LoadFloorsAsync(allFloors: false));

    private async void LoadAllFloors_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(() => LoadFloorsAsync(allFloors: true));

    private Task LoadSelectedFloorsAsync() => LoadFloorsAsync(allFloors: false);

    private async Task LoadFloorsAsync(bool allFloors)
    {
        var result = await GetClient().SendAsync<SelectedFloorsResult>(PipeCommands.GetSelectedFloors, new SelectedFloorsRequest(allFloors));

        Rows.Clear();
        foreach (var item in result.Items)
        {
            Rows.Add(new FloorAssignmentRow(item));
        }

        // The landscape type belongs to the floor type, so a match saved on any floor of a type
        // (written to Revit by Calculate) applies to every floor of that type.
        var savedByType = Rows
            .Select(row => (row, Record: FindSavedRecord(row)))
            .Where(entry => entry.Record is not null)
            .GroupBy(entry => entry.row.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Record!, StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            if ((FindSavedRecord(row) ?? savedByType.GetValueOrDefault(row.DisplayName)) is { } saved)
            {
                row.SelectedRecord = saved;
                row.SearchQuery = new CoefficientRow(saved).Display;
                row.MatchStatus = "Saved match";
                UpdateComputedValues(row);
            }
            else
            {
                ApplyAutoMatch(row);
            }
        }

        RowsCountText.Text = allFloors
            ? $"{Rows.Count:N0} floor instance(s) loaded — every floor in the project."
            : $"{Rows.Count:N0} floor instance(s) loaded from the current Revit selection.";
        UpdateCalculateAllEnabled();

        var autoMatched = Rows.Count(row => row.MatchStatus == "Auto-matched");
        var restored = Rows.Count(row => row.MatchStatus == "Saved match");
        StatusText.Text = Rows.Count == 0
            ? "Nothing to load."
            : $"{restored:N0} row(s) restored from their saved match, {autoMatched:N0} auto-matched, of {Rows.Count:N0}. Search and pick manually for the rest (a pick applies to every floor of that type), then calculate — calculating saves the match in Revit.";
    }

    /// <summary>The sheet row this floor was last calculated with: by its stored match key, else by its stored landscape type name.</summary>
    private WwpLdsCoefficientRecord? FindSavedRecord(FloorAssignmentRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.CurrentLdsMatchKey) &&
            _allCoefficients.FirstOrDefault(record => string.Equals(record.MatchKey, row.CurrentLdsMatchKey, StringComparison.OrdinalIgnoreCase)) is { } byKey)
        {
            return byKey;
        }

        return string.IsNullOrWhiteSpace(row.CurrentLdsType)
            ? null
            : _allCoefficients.FirstOrDefault(record => string.Equals(new CoefficientRow(record).DisplayName, row.CurrentLdsType, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyAutoMatch(FloorAssignmentRow row)
    {
        var match = WwpLdsFuzzyMatcher.FindBestMatch(row.FamilyName, row.TypeName, _allCoefficients);
        if (match is null)
        {
            row.MatchStatus = "No match found";
            return;
        }

        row.SelectedRecord = match;
        row.SearchQuery = new CoefficientRow(match).Display;
        row.MatchStatus = "Auto-matched";
        UpdateComputedValues(row);
    }

    /// <summary>
    /// Recomputes each metric's coefficient-derived default (coefficient × area, or as-is for the
    /// two intrinsic temperature metrics) without touching any metric already flagged Manual. The
    /// cost-saved coefficient is the WWP landscape data sheet's figure in pounds ("£15.80") or the
    /// currency its cell names, so it's converted to the project's preferred currency here — the same point i-Tree Calculator applies
    /// its rate — so a manual override afterward starts from (and edits) the already-converted number.
    /// </summary>
    private void UpdateComputedValues(FloorAssignmentRow row)
    {
        var record = row.SelectedRecord;
        var area = row.AreaSquareMeters;
        var metrics = row.Metrics;

        metrics[FloorAssignmentRow.Co2Index].ComputedValue = (record?.Co2SequesteredAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.RunoffIndex].ComputedValue = (record?.RunoffAvoidedAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.PollutionIndex].ComputedValue = (record?.PollutionMassRemovedAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.CostSavedIndex].ComputedValue = CostSaved(record) * area;
        metrics[FloorAssignmentRow.OxygenIndex].ComputedValue = (record?.OxygenProducedAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.GwpIndex].ComputedValue = (record?.TotalGwp ?? 0) * area;
        metrics[FloorAssignmentRow.SurfaceTempIndex].ComputedValue = record?.SurfaceTempReduction ?? 0;
        metrics[FloorAssignmentRow.AirTempIndex].ComputedValue = record?.AirTempReduction ?? 0;
    }

    /// <summary>A sheet row's cost saved per m² in the project currency (as it stands until the rates load).</summary>
    private double CostSaved(WwpLdsCoefficientRecord? record) =>
        record is null ? 0
        : _sheetCurrencies is null ? record.CostSavedAnnual ?? 0
        : WwpLdsSheet.CostSavedIn(_sheetCurrencies, record) ?? 0;

    private FloorLdsValues BuildValues(FloorAssignmentRow row)
    {
        var record = row.SelectedRecord ?? throw new InvalidOperationException("No landscape type selected.");
        var metrics = row.Metrics;
        var manualCount = metrics.Count(m => m.IsManual);
        var resultSource = manualCount switch
        {
            0 => "Coefficient table",
            var count when count == metrics.Count => "Manual entry",
            _ => "Coefficient table + manual entry"
        };

        return new FloorLdsValues(
            new CoefficientRow(record).DisplayName,
            record.MatchKey,
            resultSource,
            metrics[FloorAssignmentRow.Co2Index].FinalValue,
            metrics[FloorAssignmentRow.RunoffIndex].FinalValue,
            metrics[FloorAssignmentRow.PollutionIndex].FinalValue,
            metrics[FloorAssignmentRow.CostSavedIndex].FinalValue,
            metrics[FloorAssignmentRow.OxygenIndex].FinalValue,
            metrics[FloorAssignmentRow.GwpIndex].FinalValue,
            metrics[FloorAssignmentRow.SurfaceTempIndex].FinalValue,
            metrics[FloorAssignmentRow.AirTempIndex].FinalValue,
            _preferredCurrency,
            _preferredCurrencyFactor,
            // Pollen isn't reviewable per floor: the sheet's per-m² rate × area.
            (record.PollenRate ?? 0) * row.AreaSquareMeters);
    }

    private async void ReviewValues_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FloorAssignmentRow row)
        {
            return;
        }

        EditingMetrics.Clear();
        foreach (var metric in row.Metrics)
        {
            EditingMetrics.Add(metric);
        }

        MetricsDialog.Title = $"Review values — {row.DisplayName}";
        MetricsDialog.XamlRoot = XamlRoot;
        await MetricsDialog.ShowAsync();
    }

    private void RowSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        if (sender.DataContext is not FloorAssignmentRow row)
        {
            return;
        }

        var query = sender.Text.Trim();
        row.SearchResults.Clear();
        if (query.Length == 0)
        {
            return;
        }

        var matches = _allCoefficients
            .Where(record =>
                record.PlantingTypeCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.SubCategory.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.SubCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.TypeName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchResults);

        foreach (var record in matches)
        {
            row.SearchResults.Add(new CoefficientRow(record));
        }
    }

    private void RowSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (sender.DataContext is not FloorAssignmentRow row || args.SelectedItem is not CoefficientRow selected)
        {
            return;
        }

        SelectManualMatch(row, selected);
    }

    private void RowSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is not FloorAssignmentRow row || args.ChosenSuggestion is not CoefficientRow chosen)
        {
            return;
        }

        SelectManualMatch(row, chosen);
    }

    /// <summary>A pick applies to every loaded floor of the same type — the landscape type is a property of the floor type.</summary>
    private void SelectManualMatch(FloorAssignmentRow row, CoefficientRow chosen)
    {
        foreach (var sameType in Rows.Where(other => other.DisplayName == row.DisplayName))
        {
            sameType.SelectedRecord = chosen.Record;
            sameType.MatchStatus = "Manually selected";
            sameType.SearchQuery = chosen.Display;
            UpdateComputedValues(sameType);
        }

        UpdateCalculateAllEnabled();
    }

    private async void RowCalculate_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FloorAssignmentRow row)
        {
            return;
        }

        if (row.SelectedRecord is null)
        {
            StatusText.Text = $"Search and pick a landscape type for '{row.DisplayName}' first.";
            return;
        }

        if (_rateUnavailableMessage is not null)
        {
            StatusText.Text = _rateUnavailableMessage + " Reopen Floor Calculator to try again.";
            return;
        }

        var record = row.SelectedRecord;
        await RunBusyAsync(async () =>
        {
            var values = BuildValues(row);
            var result = await GetClient().SendAsync<CalculateFloorsBatchResult>(
                PipeCommands.CalculateFloorsBatch,
                new CalculateFloorsBatchRequest([new FloorLdsAssignment(row.UniqueId, values)]));

            var resultRow = result.Rows.FirstOrDefault(r => r.UniqueId == row.UniqueId);
            if (resultRow is not null)
            {
                row.MatchStatus = resultRow.ResultSource;
            }

            StatusText.Text = resultRow is null
                ? "Nothing was updated."
                : resultRow.NotWritten is { Count: > 0 } missing
                    ? $"Calculated '{row.DisplayName}', but {missing.Count:N0} result(s) couldn't be written ({string.Join(", ", missing)}) — they're missing or read-only. Run Import Shared Parameter, then calculate again."
                    : $"Calculated '{row.DisplayName}' as {new CoefficientRow(record).DisplayName} ({resultRow.ResultSource}).";
        });
    }

    private async void CalculateAll_Click(object sender, RoutedEventArgs e)
    {
        var toCalculate = Rows.Where(row => row.SelectedRecord is not null).ToList();
        if (toCalculate.Count == 0)
        {
            StatusText.Text = "No rows have a landscape type selected yet.";
            return;
        }

        if (_rateUnavailableMessage is not null)
        {
            StatusText.Text = _rateUnavailableMessage + " Reopen Floor Calculator to try again.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var assignments = toCalculate
                .Select(row => new FloorLdsAssignment(row.UniqueId, BuildValues(row)))
                .ToList();
            var result = await GetClient().SendAsync<CalculateFloorsBatchResult>(
                PipeCommands.CalculateFloorsBatch, new CalculateFloorsBatchRequest(assignments));

            var resultByUniqueId = result.Rows.ToDictionary(r => r.UniqueId, StringComparer.Ordinal);
            foreach (var row in toCalculate)
            {
                if (resultByUniqueId.TryGetValue(row.UniqueId, out var resultRow))
                {
                    row.MatchStatus = resultRow.ResultSource;
                }
            }

            var remaining = Rows.Count(row => row.SelectedRecord is null);
            var incomplete = result.Rows.Where(r => r.NotWritten is { Count: > 0 }).ToList();
            var missingNames = incomplete.SelectMany(r => r.NotWritten!).Distinct(StringComparer.Ordinal).ToList();
            StatusText.Text =
                $"Calculated {result.Rows.Count:N0} floor(s)." +
                (incomplete.Count > 0
                    ? $" {incomplete.Count:N0} of them are missing results that couldn't be written ({string.Join(", ", missingNames)}) — run Import Shared Parameter, then calculate again."
                    : string.Empty) +
                (remaining > 0
                    ? $" {remaining:N0} still need a manual match — search and pick a type for the highlighted rows."
                    : string.Empty);

            if (FillTreesCheckBox.IsChecked == true)
            {
                StatusText.Text += " " + await FillTreesAsync();
            }
        });
    }

    private async void FillTrees_Click(object sender, RoutedEventArgs e)
    {
        if (_rateUnavailableMessage is not null)
        {
            StatusText.Text = _rateUnavailableMessage + " Reopen Floor Calculator to try again.";
            return;
        }

        await RunBusyAsync(async () => StatusText.Text = await FillTreesAsync());
    }

    /// <summary>
    /// Trees from the sheet: an estimate for each tree i-Tree hasn't calculated, and pollen for every
    /// matched tree (see <see cref="WwpSheetTreeEstimates"/>). Same as Tree Calculator's fill.
    /// </summary>
    private async Task<string> FillTreesAsync()
    {
        var currencies = _sheetCurrencies ??
            (await WwpLdsSheet.CurrencyFactorsAsync(GetClient(), _exchangeRateService, _preferredCurrency, _allCoefficients)).Factors;
        var summary = await WwpSheetTreeEstimates.RunAsync(
            GetClient(), _allCoefficients, _preferredUnitSystem, _preferredCurrency, currencies, _preferredCurrencyFactor);
        return summary.Describe();
    }

    private void UpdateCalculateAllEnabled() => CalculateAllButton.IsEnabled = Rows.Any(row => row.SelectedRecord is not null);

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
            UpdateCalculateAllEnabled();
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
