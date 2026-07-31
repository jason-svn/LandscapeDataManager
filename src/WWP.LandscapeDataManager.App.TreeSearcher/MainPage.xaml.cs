using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

public sealed partial class MainPage : Page
{
    private const int MaxResults = 100;

    private readonly SpeciesCatalogueDatabase _catalogueDatabase = new();
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private IReadOnlyList<SpeciesCatalogueRecord> _allSpecies = [];

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<SpeciesRow> ResultRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
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
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        ResultRows.Clear();

        if (query.Length == 0)
        {
            ResultsCountText.Text = string.Empty;
            return;
        }

        var matches = _allSpecies
            .Where(record =>
                record.CommonName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.ScientificName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.SpeciesCode.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.CommonName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var record in matches.Take(MaxResults))
        {
            ResultRows.Add(new SpeciesRow(record));
        }

        ResultsCountText.Text = matches.Count > MaxResults
            ? $"Showing {MaxResults:N0} of {matches.Count:N0} matches — refine your search to narrow it down."
            : $"{matches.Count:N0} match(es).";
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AssignButton.IsEnabled = ResultsList.SelectedItem is not null;

    private async void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SpeciesRow row)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<AssignSpeciesResult>(
                PipeCommands.AssignSpeciesToSelection,
                new AssignSpeciesRequest(row.Record));

            StatusText.Text = result.UpdatedTypeNames.Count switch
            {
                0 => "Nothing was updated.",
                1 => $"Assigned {row.CommonName} ({row.SpeciesCode}) to '{result.UpdatedTypeNames[0]}'.",
                _ => $"Assigned {row.CommonName} ({row.SpeciesCode}) to {result.UpdatedTypeNames.Count:N0} types: {string.Join(", ", result.UpdatedTypeNames)}."
            };
        });
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        AssignButton.IsEnabled = false;
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
            AssignButton.IsEnabled = ResultsList.SelectedItem is not null;
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
