using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;

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

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<FloorAssignmentRow> Rows { get; } = [];

    /// <summary>Reused across "Review values" dialog openings — cleared and repopulated with the target row's own Metrics objects (not copies) so edits apply directly.</summary>
    public ObservableCollection<FloorMetricEntry> EditingMetrics { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _windowHandle = windowHandle;
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
            StatusText.Text = $"Failed to open Settings: {exception.Message}";
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        _wwpLdsSettings = snapshot?.WwpLdsSource ?? WwpLdsAirtableSettings.CompanyDefault;
        SourceStatusText.Text = string.IsNullOrWhiteSpace(_credentialStore.Load())
            ? "No Airtable token saved, and no landscape data sheet source configured — open Settings from the LIM ribbon first."
            : $"Source: base {_wwpLdsSettings.BaseId} / table {_wwpLdsSettings.TableIdOrName} (managed in Settings).";

        await RefreshCatalogueStatusAsync();

        try
        {
            var catalog = await GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
            var rate = await _exchangeRateService.GetUsdRateAsync(catalog.PreferredCurrency);
            _preferredCurrency = rate.CurrencyCode;
            _preferredCurrencyFactor = rate.UsdRate;
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
            ? "No landscape data sheet has been synced yet — click \"Sync coefficient data\" (make sure your Airtable token is saved in Settings first)."
            : lastUpdated is null
                ? $"{_allCoefficients.Count:N0} landscape data sheet rows cached locally."
                : $"{_allCoefficients.Count:N0} landscape data sheet rows cached locally, last synced {lastUpdated}.";
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
            var apiSettings = new AirtableApiSettings(_wwpLdsSettings.BaseId, _wwpLdsSettings.TableIdOrName, _wwpLdsSettings.ViewName);
            var records = await _airtableClient.GetRecordsAsync(apiSettings, token);
            var coefficients = records.Select(ToCoefficientRecord).ToList();
            await _coefficientDatabase.SaveAsync(coefficients);

            await RefreshCatalogueStatusAsync();
            StatusText.Text = $"Synced {coefficients.Count:N0} landscape data sheet rows from Airtable.";
        });
    }

    private static WwpLdsCoefficientRecord ToCoefficientRecord(AirtableRecord record)
    {
        var fields = record.Fields.ToDictionary(kv => kv.Key.Trim(), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var origin = GetString(fields, "Origin");
        var plantingTypeCode = GetString(fields, "Planting type");
        var category = GetString(fields, "WWP_LDS_Category");
        var subCategory = GetString(fields, "WWP_LDS_SubCategory");
        var typeName = GetString(fields, "Types");
        var matchKey = $"{category}|{subCategory}|{typeName}";

        return new WwpLdsCoefficientRecord(
            matchKey,
            origin,
            plantingTypeCode,
            category,
            subCategory,
            typeName,
            GetDouble(fields, "COST_SAVED"),
            GetDouble(fields, "Oxygen levels O2 kg/yr (16/18 girth)"),
            GetDouble(fields, "WWP_Total_GWP"),
            GetDouble(fields, "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)"),
            GetDouble(fields, "Avoided runoff m3/yr (16/18 girth)"),
            GetDouble(fields, "WWP_Pollutants_Removed"),
            GetDouble(fields, "Surface_Temperature_Reduction_Min"),
            GetDouble(fields, "Air temperature reduction (1.5m height)"));
    }

    private static string GetString(IReadOnlyDictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private static double? GetDouble(IReadOnlyDictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private async void LoadSelectedFloors_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(LoadSelectedFloorsAsync);

    private async Task LoadSelectedFloorsAsync()
    {
        var result = await GetClient().SendAsync<SelectedFloorsResult>(PipeCommands.GetSelectedFloors, null);

        Rows.Clear();
        foreach (var item in result.Items)
        {
            var row = new FloorAssignmentRow(item);
            Rows.Add(row);
            ApplyAutoMatch(row);
        }

        RowsCountText.Text = $"{Rows.Count:N0} floor instance(s) loaded from the current Revit selection.";
        UpdateCalculateAllEnabled();

        var autoMatched = Rows.Count(row => row.MatchStatus == "Auto-matched");
        StatusText.Text = Rows.Count == 0
            ? "Nothing to load."
            : $"Auto-matched {autoMatched:N0} of {Rows.Count:N0} row(s). Search and pick manually for the rest, then calculate.";
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
    /// cost-saved coefficient is the WWP landscape data sheet's raw USD figure, so it's converted to
    /// the project's preferred currency here — the same point i-Tree Calculator applies its rate —
    /// so a manual override afterward starts from (and edits) the already-converted number.
    /// </summary>
    private void UpdateComputedValues(FloorAssignmentRow row)
    {
        var record = row.SelectedRecord;
        var area = row.AreaSquareMeters;
        var metrics = row.Metrics;

        metrics[FloorAssignmentRow.Co2Index].ComputedValue = (record?.Co2SequesteredAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.RunoffIndex].ComputedValue = (record?.RunoffAvoidedAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.PollutionIndex].ComputedValue = (record?.PollutionMassRemovedAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.CostSavedIndex].ComputedValue = (record?.CostSavedAnnual ?? 0) * area * _preferredCurrencyFactor;
        metrics[FloorAssignmentRow.OxygenIndex].ComputedValue = (record?.OxygenProducedAnnual ?? 0) * area;
        metrics[FloorAssignmentRow.GwpIndex].ComputedValue = (record?.TotalGwp ?? 0) * area;
        metrics[FloorAssignmentRow.SurfaceTempIndex].ComputedValue = record?.SurfaceTempReduction ?? 0;
        metrics[FloorAssignmentRow.AirTempIndex].ComputedValue = record?.AirTempReduction ?? 0;
    }

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
            _preferredCurrencyFactor);
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

    private void SelectManualMatch(FloorAssignmentRow row, CoefficientRow chosen)
    {
        row.SelectedRecord = chosen.Record;
        row.MatchStatus = "Manually selected";
        row.SearchQuery = chosen.Display;
        UpdateComputedValues(row);
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

            StatusText.Text = resultRow is not null
                ? $"Calculated '{row.DisplayName}' as {new CoefficientRow(record).DisplayName} ({resultRow.ResultSource})."
                : "Nothing was updated.";
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
            StatusText.Text = remaining == 0
                ? $"Calculated {result.Rows.Count:N0} floor(s)."
                : $"Calculated {result.Rows.Count:N0} floor(s). {remaining:N0} still need a manual match — search and pick a type for the highlighted rows.";
        });
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
