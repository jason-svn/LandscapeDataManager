using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Parameters;

public sealed partial class MainPage : Page
{
    private readonly SharedParameterFileSettingsStore _settingsStore = new();
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<SetupRow> ResultRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsStore.LoadAsync();
        SharedParameterFilePathBox.Text = settings.FilePath;
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

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SharedParameterFilePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            ShowStatus("Select a shared parameter file first.");
            return;
        }

        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        RunButton.IsEnabled = false;
        try
        {
            await _settingsStore.SaveAsync(new SharedParameterFileSettings(filePath));

            var result = await GetClient().SendAsync<SharedParameterSetupResult>(
                PipeCommands.EnsureSharedParameters,
                new EnsureSharedParametersRequest(filePath));

            ResultRows.Clear();
            foreach (var row in result.Rows
                         .OrderBy(row => StatusSortOrder(row.Status))
                         .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                ResultRows.Add(new SetupRow(row));
            }

            var created = result.Rows.Count(row => row.Status == "Created");
            var valid = result.Rows.Count(row => row.Status == "Already valid");
            var attention = result.Rows.Count(row => row.Status is "Conflict" or "Error");
            CreatedCountText.Text = created.ToString("N0");
            ValidCountText.Text = valid.ToString("N0");
            AttentionCountText.Text = attention.ToString("N0");

            ShowStatus(attention == 0
                ? $"{result.DocumentTitle}: {created:N0} created, {valid:N0} already valid. Nothing needs attention."
                : $"{result.DocumentTitle}: {created:N0} created, {valid:N0} already valid, {attention:N0} need attention — see the rows below.");
        }
        catch (Exception exception)
        {
            ShowStatus($"Setup failed: {exception.Message}");
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
            RunButton.IsEnabled = true;
        }
    }

    private static int StatusSortOrder(string status) => status switch
    {
        "Error" => 0,
        "Conflict" => 1,
        "Created" => 2,
        "Already valid" => 3,
        _ => 4
    };

    private void ShowStatus(string message) => StatusText.Text = message;

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
