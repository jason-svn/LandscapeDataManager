using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Parameters;

public sealed partial class MainPage : Page
{
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private SetupRow? _lastClickedRow;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<SetupRow> ResultRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        _pipeName = pipeName;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SiblingToolLauncher.ShowOrStart(Path.Combine("Settings", "WWP.LandscapeDataManager.Settings.exe"), _pipeName);
        }
        catch (Exception exception)
        {
            ShowStatus($"Failed to open Settings: {exception.Message}");
        }
    }

    private async void LoadParameters_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<SharedParameterSetupResult>(
                PipeCommands.PreviewSharedParameters,
                new PreviewSharedParametersRequest());

            ResultRows.Clear();
            _lastClickedRow = null;
            foreach (var row in result.Rows
                         .OrderBy(row => StatusSortOrder(row.Status))
                         .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                ResultRows.Add(new SetupRow(row));
            }

            ApplyButton.IsEnabled = ResultRows.Count > 0;
            UpdateCounts();
            ShowStatus($"{result.DocumentTitle}: loaded {ResultRows.Count:N0} parameters. " +
                       "Review the rows below, uncheck anything you don't want touched (Shift+click does a range), then click Apply.");
        });
    }

    /// <summary>Shift+click ticks or unticks every row from the last one clicked to this one, as in a file list.</summary>
    private void IncludeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: SetupRow row } checkBox)
        {
            return;
        }

        var anchor = _lastClickedRow;
        _lastClickedRow = row;
        if (anchor is null || !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }

        var from = ResultRows.IndexOf(anchor);
        var to = ResultRows.IndexOf(row);
        if (from < 0 || to < 0)
        {
            return;
        }

        var include = checkBox.IsChecked == true;
        for (var index = Math.Min(from, to); index <= Math.Max(from, to); index++)
        {
            ResultRows[index].IsIncluded = include;
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var includedNames = ResultRows.Where(row => row.IsIncluded).Select(row => row.Name).ToList();
        if (includedNames.Count == 0)
        {
            ShowStatus("Check at least one parameter to apply.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<SharedParameterSetupResult>(
                PipeCommands.EnsureSharedParameters,
                new EnsureSharedParametersRequest(includedNames));

            var updatedByName = result.Rows.ToDictionary(row => row.Name, StringComparer.Ordinal);
            foreach (var row in ResultRows)
            {
                if (updatedByName.TryGetValue(row.Name, out var updated))
                {
                    row.ApplyResult(updated);
                }
            }

            UpdateCounts();

            var created = result.Rows.Count(row => row.Status is "Created" or "Updated");
            var valid = result.Rows.Count(row => row.Status == "Already valid");
            var attention = result.Rows.Count(row => row.Status is "Conflict" or "Error");
            ShowStatus(attention == 0
                ? $"{result.DocumentTitle}: {created:N0} created/updated, {valid:N0} already valid. Nothing needs attention."
                : $"{result.DocumentTitle}: {created:N0} created/updated, {valid:N0} already valid, {attention:N0} need attention — see the rows below.");
        });
    }

    private static int StatusSortOrder(string status) => status switch
    {
        "Error" => 0,
        "Conflict" => 1,
        "Will create" => 2,
        "Will update" => 3,
        "Created" => 4,
        "Updated" => 5,
        "Already valid" => 6,
        _ => 7
    };

    private void UpdateCounts()
    {
        CreatedCountText.Text = ResultRows.Count(row => row.Status is "Created" or "Updated").ToString("N0");
        ValidCountText.Text = ResultRows.Count(row => row.Status == "Already valid").ToString("N0");
        PendingCountText.Text = ResultRows.Count(row => row.Status is "Will create" or "Will update").ToString("N0");
        AttentionCountText.Text = ResultRows.Count(row => row.Status is "Conflict" or "Error").ToString("N0");
    }

    private void ShowStatus(string message) => StatusText.Text = message;

    private async Task RunBusyAsync(Func<Task> operation)
    {
        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        LoadButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            ShowStatus($"Failed: {exception.Message}");
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
            LoadButton.IsEnabled = true;
            ApplyButton.IsEnabled = ResultRows.Count > 0;
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
