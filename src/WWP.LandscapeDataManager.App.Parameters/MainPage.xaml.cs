using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Parameters;

public sealed partial class MainPage : Page
{
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;

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
            ShowStatus($"Failed to open Settings: {exception.Message}");
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var snapshot = await ProjectSettingsSync.PullAsync(GetClient());
        SharedParameterFilePathBox.Text = snapshot?.SharedParameterFilePath ?? string.Empty;
    }

    private async void BrowseSharedParameterFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".txt");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            SharedParameterFilePathBox.Text = file.Path;
        }
    }

    private async void LoadParameters_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SharedParameterFilePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            ShowStatus("Select a shared parameter file first.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            await ProjectSettingsSync.PushAsync(GetClient(), sharedParameterFilePath: filePath);

            var result = await GetClient().SendAsync<SharedParameterSetupResult>(
                PipeCommands.PreviewSharedParameters,
                new PreviewSharedParametersRequest(filePath));

            ResultRows.Clear();
            foreach (var row in result.Rows
                         .OrderBy(row => StatusSortOrder(row.Status))
                         .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                ResultRows.Add(new SetupRow(row));
            }

            ApplyButton.IsEnabled = ResultRows.Count > 0;
            UpdateCounts();
            ShowStatus($"{result.DocumentTitle}: loaded {ResultRows.Count:N0} parameters. " +
                       "Review the rows below, uncheck anything you don't want touched, then click Apply.");
        });
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SharedParameterFilePathBox.Text.Trim();
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
                new EnsureSharedParametersRequest(filePath, includedNames));

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
