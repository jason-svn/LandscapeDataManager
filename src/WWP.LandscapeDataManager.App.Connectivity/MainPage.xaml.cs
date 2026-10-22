using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Connectivity;

public sealed partial class MainPage : Page
{
    private RevitPipeClient? _revitClient;
    private string _pipeName = string.Empty;
    private ConnectivitySettings _lastSettings = ConnectivitySettings.Default;

    public MainPage()
    {
        InitializeComponent();
        ShowSettings(ConnectivitySettings.Default);
    }

    public ObservableCollection<ConnectivityRow> ResultRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _pipeName = pipeName;
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
            if (snapshot?.Connectivity is { } settings)
            {
                ShowSettings(settings);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Couldn't read the project's connectivity settings, using defaults: {exception.Message}";
        }
    }

    private void ShowSettings(ConnectivitySettings settings)
    {
        DistancesBox.Text = string.Join(", ", settings.DispersalDistancesMeters.Select(distance => distance.ToString("0.##", CultureInfo.InvariantCulture)));
        ProbabilityBox.Value = settings.ProbabilityAtDistance;
        IncludeFloorsCheckBox.IsChecked = settings.IncludeFloors;
        IncludeUnclassifiedCheckBox.IsChecked = settings.IncludeUnclassifiedFloors;
    }

    private ConnectivitySettings ReadSettings()
    {
        var distances = ConnectivityCalculator.ParseDistances(DistancesBox.Text)
                        ?? throw new InvalidOperationException("Enter one or more dispersal distances in metres, e.g. 50, 250, 1000.");
        var probability = ProbabilityBox.Value;
        if (double.IsNaN(probability) || probability is <= 0 or >= 1)
        {
            throw new InvalidOperationException("The dispersal probability must be between 0 and 1 (0.5 = median dispersal distance).");
        }

        return new ConnectivitySettings(
            distances,
            probability,
            IncludeFloorsCheckBox.IsChecked == true,
            IncludeUnclassifiedCheckBox.IsChecked == true);
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

    private async void Calculate_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var settings = ReadSettings();
        var scan = await GetClient().SendAsync<ConnectivityPatchesResult>(
            PipeCommands.GetConnectivityPatches,
            new ConnectivityPatchesRequest(settings.IncludeFloors));
        // Growth Projections' copies stand where their originals do: only the trees as drawn count.
        var organizer = await GetClient().SendAsync<OrganizerTreesResult>(PipeCommands.GetOrganizerTrees);
        var growthCopies = organizer.Trees
            .Where(tree => tree.LinkedYear is not null)
            .Select(tree => tree.UniqueId)
            .ToHashSet(StringComparer.Ordinal);

        var included = new List<ConnectivityPatchItem>();
        var excluded = new List<(ConnectivityPatchItem Item, string Reason)>();
        foreach (var item in scan.Items)
        {
            if (ConnectivityPatchSelector.ExclusionReason(item, settings, growthCopies) is { } reason)
            {
                excluded.Add((item, reason));
            }
            else
            {
                included.Add(item);
            }
        }

        if (included.Count == 0)
        {
            throw new InvalidOperationException("No trees or planting areas found to score — place Planting families, or tag floor types with a pervious LDS Type.");
        }

        StatusText.Text = $"Calculating connectivity for {included.Count:N0} patch(es)…";
        CalculationProgress.Value = 0;
        CalculationProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value => CalculationProgress.Value = value);
        var patches = included.Select(ConnectivityPatchSelector.ToPatch).ToList();
        var options = new ConnectivityOptions(settings.DispersalDistancesMeters, settings.ProbabilityAtDistance);
        ConnectivityRunResult run;
        try
        {
            run = await Task.Run(() => ConnectivityCalculator.Calculate(patches, options, progress));
        }
        finally
        {
            CalculationProgress.Visibility = Visibility.Collapsed;
        }

        _lastSettings = settings;
        var resultsById = run.Patches.ToDictionary(result => result.Id);
        ResultRows.Clear();
        foreach (var row in included
                     .Select(item => new ConnectivityRow(item, resultsById[item.UniqueId], null))
                     .OrderByDescending(row => row.SortKey)
                     .Concat(excluded.Select(entry => new ConnectivityRow(entry.Item, null, entry.Reason))))
        {
            ResultRows.Add(row);
        }

        UpdateCounts();
        EquivalentAreaText.Text = "Equivalent connected area: " + string.Join("  ·  ", settings.DispersalDistancesMeters
            .Select((distance, index) => $"{distance:0.##} m → {run.EquivalentConnectedAreaByDistance[index]:N0} m²"));
        WriteButton.IsEnabled = true;
        StatusText.Text = $"{scan.DocumentTitle}: scored {included.Count:N0} patch(es)" +
                          (excluded.Count > 0 ? $", {excluded.Count:N0} floor(s) not counted as planting areas." : ".");

        try
        {
            await ProjectSettingsSync.PushAsync(GetClient(), connectivity: settings);
        }
        catch (Exception exception)
        {
            StatusText.Text += $" (Settings not saved to the project: {exception.Message})";
        }
    });

    private async void WriteResults_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var values = ResultRows
            .Where(row => row.Result is not null)
            .Select(row => new ConnectivityResultValues(
                row.UniqueId,
                row.Result!.Band,
                ConnectivityCalculator.BandName(row.Result.Band),
                Math.Round(row.Result.Score, 4),
                Math.Round(row.Result.MeanDpcPercent, 4),
                FormatDetails(row.Result, _lastSettings)))
            .ToList();
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Calculate first — there are no results to write.");
        }

        var result = await GetClient().SendAsync<WriteConnectivityResultsResult>(
            PipeCommands.WriteConnectivityResults,
            new WriteConnectivityResultsRequest(values));

        var message = $"Wrote connectivity to {result.Written.Count:N0} element(s)";
        if (result.SkippedLocked.Count > 0)
        {
            message += $", left {result.SkippedLocked.Count:N0} locked element(s) alone";
        }

        if (result.Failures.Count > 0)
        {
            message += $"; {result.Failures.Count:N0} failed — {result.Failures[0].Reason}";
        }

        StatusText.Text = message + ".";
    });

    private static string FormatDetails(ConnectivityPatchResult result, ConnectivitySettings settings)
    {
        var perDistance = settings.DispersalDistancesMeters.Select((distance, index) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{distance:0.##} m: dPC {result.DpcByDistance[index] * 100:0.###}%, score {result.ScoreByDistance[index]:0.00} ({ConnectivityCalculator.BandName(result.BandByDistance[index])})"));
        var details = string.Create(CultureInfo.InvariantCulture, $"p = {settings.ProbabilityAtDistance:0.##} at ") + string.Join("; ", perDistance) + ".";
        return result.Uncertain ? details + " Uncertain: band changes by two or more between distances." : details;
    }

    private void UpdateCounts()
    {
        int Count(int band) => ResultRows.Count(row => row.Result?.Band == band);
        PatchCountText.Text = ResultRows.Count(row => row.Result is not null).ToString("N0");
        CriticalCountText.Text = Count(ConnectivityCalculator.CriticalBand).ToString("N0");
        StrongCountText.Text = Count(ConnectivityCalculator.StrongBand).ToString("N0");
        ModerateCountText.Text = Count(ConnectivityCalculator.ModerateBand).ToString("N0");
        LowCountText.Text = Count(ConnectivityCalculator.LowBand).ToString("N0");
        UncertainCountText.Text = ResultRows.Count(row => row.Result?.Uncertain == true).ToString("N0");
    }

    private IReadOnlyList<string> GetSelectedUniqueIds() =>
        ResultList.SelectedItems.Cast<ConnectivityRow>().Select(row => row.UniqueId).ToList();

    private async void SelectRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.SelectElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void ZoomRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.ZoomToElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void IsolateRows_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.IsolateElements, new ElementSelectionRequest(GetSelectedUniqueIds())));

    private async void ResetIsolate_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => GetClient().SendAsync<OperationResult>(PipeCommands.ResetIsolation, null));

    private async void ApplyColours_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var statusByUniqueId = ResultRows
            .Where(row => row.ColourStatus is not null)
            .ToDictionary(row => row.UniqueId, row => row.ColourStatus!);
        var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ApplyStatusColourOverrides, new StatusColourOverrideRequest(statusByUniqueId));
        StatusText.Text = result.Message ?? "Band colours applied.";
    });

    private async void ResetColours_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var allIds = ResultRows.Select(row => row.UniqueId).ToList();
        var result = await GetClient().SendAsync<OperationResult>(PipeCommands.ResetColourOverrides, new ElementSelectionRequest(allIds));
        StatusText.Text = result.Message ?? "Colour overrides reset.";
    });

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        CalculateButton.IsEnabled = false;
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
            CalculateButton.IsEnabled = true;
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
