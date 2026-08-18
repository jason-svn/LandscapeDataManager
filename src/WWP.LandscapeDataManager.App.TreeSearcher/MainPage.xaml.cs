using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

public sealed partial class MainPage : Page
{
    private const int MaxSearchResults = 25;

    private static readonly string[] TypeKeyFields = ["Types", "Planting type", "Species names", "species", "type"];

    private readonly SpeciesCatalogueDatabase _catalogueDatabase = new();
    private readonly AirtableApiClient _airtableApiClient = new();
    private readonly AirtableCredentialStore _airtableCredentialStore = new();
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private IReadOnlyList<SpeciesCatalogueRecord> _allSpecies = [];
    private AirtableApiSettings _airtableSettings = new(string.Empty, string.Empty, null);

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<TreeAssignmentRow> Rows { get; } = [];

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
            StatusText.Text = $"Failed to open Settings: {exception.Message}";
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _allSpecies = await _catalogueDatabase.GetAllAsync();
        var metadata = await _catalogueDatabase.GetMetadataAsync();
        var lastUpdated = metadata.FormatDownloadedAtLocal();

        CatalogueStatusText.Text = _allSpecies.Count == 0
            ? "No species catalogue has been downloaded yet — run the i-Tree Downloader tool first, then come back here."
            : lastUpdated is null
                ? $"{_allSpecies.Count:N0} species cached locally (catalogue version {metadata.Version ?? "unknown"})."
                : $"{_allSpecies.Count:N0} species cached locally, last updated {lastUpdated} (catalogue version {metadata.Version ?? "unknown"}).";

        try
        {
            var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
            _airtableSettings = snapshot?.AirtableApi ?? new AirtableApiSettings(string.Empty, string.Empty, null);
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }

        try
        {
            await LoadSelectedTreesAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private async void LoadSelectedTrees_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(LoadSelectedTreesAsync);

    private async Task LoadSelectedTreesAsync()
    {
        var result = await GetClient().SendAsync<SelectedPlantingTypesResult>(PipeCommands.GetSelectedPlantingTypes, null);

        Rows.Clear();
        foreach (var item in result.Items)
        {
            var row = new TreeAssignmentRow(item);
            Rows.Add(row);
            ApplyAutoMatch(row);
        }

        RowsCountText.Text = $"{Rows.Count:N0} distinct planting type(s) loaded from the current Revit selection.";
        UpdateAssignAllEnabled();

        var autoMatched = Rows.Count(row => row.MatchStatus == "Auto-matched");
        StatusText.Text = Rows.Count == 0
            ? "Nothing to load."
            : $"Auto-matched {autoMatched:N0} of {Rows.Count:N0} row(s). Search and pick a species manually for the rest, then assign.";
    }

    private void ApplyAutoMatch(TreeAssignmentRow row)
    {
        var match = SpeciesFuzzyMatcher.FindBestMatch(row.FamilyName, row.TypeName, _allSpecies);
        if (match is null)
        {
            row.MatchStatus = "No match found";
            return;
        }

        row.SelectedSpecies = match;
        row.SearchQuery = new SpeciesRow(match).Display;
        row.MatchStatus = "Auto-matched";
    }

    private void RowSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        if (sender.DataContext is not TreeAssignmentRow row)
        {
            return;
        }

        var query = sender.Text.Trim();
        row.SearchResults.Clear();
        if (query.Length == 0)
        {
            return;
        }

        var matches = _allSpecies
            .Where(record =>
                record.CommonName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.ScientificName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.SpeciesCode.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.CommonName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchResults);

        foreach (var record in matches)
        {
            row.SearchResults.Add(new SpeciesRow(record));
        }
    }

    private void RowSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (sender.DataContext is not TreeAssignmentRow row || args.SelectedItem is not SpeciesRow selected)
        {
            return;
        }

        SelectManualMatch(row, selected);
    }

    private void RowSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is not TreeAssignmentRow row || args.ChosenSuggestion is not SpeciesRow chosen)
        {
            return;
        }

        SelectManualMatch(row, chosen);
    }

    private void SelectManualMatch(TreeAssignmentRow row, SpeciesRow chosen)
    {
        row.SelectedSpecies = chosen.Record;
        row.MatchStatus = "Manually selected";
        row.SearchQuery = chosen.Display;
        UpdateAssignAllEnabled();
    }

    private async void RowAssign_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TreeAssignmentRow row)
        {
            return;
        }

        if (row.SelectedSpecies is null)
        {
            StatusText.Text = $"Search and pick a species for '{row.DisplayName}' first.";
            return;
        }

        var species = row.SelectedSpecies;
        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<AssignSpeciesBatchResult>(
                PipeCommands.AssignSpeciesBatch,
                new AssignSpeciesBatchRequest([new TypeSpeciesAssignment(row.UniqueId, species)]));

            if (result.UpdatedTypeNames.Count > 0)
            {
                row.CurrentSpeciesCode = species.SpeciesCode;
                row.MatchStatus = "Assigned";
            }

            StatusText.Text = result.UpdatedTypeNames.Count > 0
                ? $"Assigned {species.CommonName} ({species.SpeciesCode}) to '{row.DisplayName}'."
                : "Nothing was updated.";
        });
    }

    private async void AssignAll_Click(object sender, RoutedEventArgs e)
    {
        var toAssign = Rows.Where(row => row.SelectedSpecies is not null).ToList();
        if (toAssign.Count == 0)
        {
            StatusText.Text = "No rows have a species selected yet.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var assignments = toAssign
                .Select(row => new TypeSpeciesAssignment(row.UniqueId, row.SelectedSpecies!))
                .ToList();
            var result = await GetClient().SendAsync<AssignSpeciesBatchResult>(
                PipeCommands.AssignSpeciesBatch, new AssignSpeciesBatchRequest(assignments));

            foreach (var row in toAssign)
            {
                row.CurrentSpeciesCode = row.SelectedSpecies!.SpeciesCode;
                row.MatchStatus = "Assigned";
            }

            var remaining = Rows.Count(row => row.SelectedSpecies is null);
            StatusText.Text = remaining == 0
                ? $"Assigned {result.UpdatedTypeNames.Count:N0} type(s)."
                : $"Assigned {result.UpdatedTypeNames.Count:N0} type(s). {remaining:N0} still need a manual match — search and pick a species for the highlighted rows.";
        });
    }

    private async void PushSpeciesCodes_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(PushSpeciesCodesToAirtableAsync);

    private async Task PushSpeciesCodesToAirtableAsync()
    {
        var token = _airtableCredentialStore.Load().Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusText.Text = "No Airtable personal access token saved — open Settings from the LIM ribbon first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_airtableSettings.BaseId))
        {
            StatusText.Text = "No planting data source saved — open Settings from the LIM ribbon first.";
            return;
        }

        var scan = await GetClient().SendAsync<ModelScanResult>(PipeCommands.ScanModel, new ModelScanOptions());
        var codedTypes = scan.Items
            .Where(item => item.Category == "Planting" && !string.IsNullOrWhiteSpace(item.SpeciesCode))
            .ToList();
        if (codedTypes.Count == 0)
        {
            StatusText.Text = "No Planting types have a species code assigned yet — assign one above first.";
            return;
        }

        var records = await _airtableApiClient.GetRecordsAsync(_airtableSettings, token);
        var matches = StableTypeMatcher.Build(codedTypes, records, TypeKeyFields, []);

        var pushed = 0;
        var problems = new List<string>();
        foreach (var match in matches)
        {
            if (match.Status != "Matched" || match.Record is null)
            {
                problems.Add($"{match.RevitType.TypeName} ({match.Status})");
                continue;
            }

            try
            {
                await _airtableApiClient.UpdateRecordFieldsAsync(
                    _airtableSettings,
                    token,
                    match.Record.Id,
                    new Dictionary<string, object?> { ["Species Code"] = match.RevitType.SpeciesCode });
                pushed++;
            }
            catch (Exception exception)
            {
                problems.Add($"{match.RevitType.TypeName} (failed: {exception.Message})");
            }

            await Task.Delay(210);
        }

        StatusText.Text = problems.Count == 0
            ? $"Pushed {pushed:N0} species code(s) to Airtable."
            : $"Pushed {pushed:N0} species code(s) to Airtable. {problems.Count:N0} skipped: " +
              $"{string.Join("; ", problems.Take(5))}{(problems.Count > 5 ? $" (+{problems.Count - 5} more)" : ".")}";
    }

    private void UpdateAssignAllEnabled() => AssignAllButton.IsEnabled = Rows.Any(row => row.SelectedSpecies is not null);

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
            UpdateAssignAllEnabled();
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
