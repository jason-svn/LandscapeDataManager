using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

/// <summary>"Show original data": one row per element with every stored value, see <see cref="OriginalDataTable"/>.</summary>
public sealed partial class OriginalDataWindow : Window
{
    // Header and row cells share one left padding (12) plus the ListView's scrollbar gutter.
    private const double TableChrome = 12 + 12 + 16;

    private readonly RevitPipeClient? _revitClient;
    private DashboardReportResult _report;
    private IReadOnlyList<OriginalDataCell> _headers = [];
    private List<OriginalDataRow> _allRows = [];
    private List<OriginalDataRow> _visibleRows = [];

    public OriginalDataWindow(DashboardReportResult report, RevitPipeClient? revitClient)
    {
        InitializeComponent();
        _report = report;
        _revitClient = revitClient;

        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
        appWindow.Resize(new SizeInt32(1400, 820));
        Rebuild();
    }

    /// <summary>Called when the Dashboard refreshes while this window is open, so it never shows stale rows.</summary>
    public void ShowReport(DashboardReportResult report)
    {
        _report = report;
        Rebuild();
    }

    private bool ShowingTrees => DatasetBox.SelectedIndex != 1;

    private void Rebuild()
    {
        double width;
        if (ShowingTrees)
        {
            _headers = OriginalDataTable.Headers(OriginalDataTable.TreeColumns);
            _allRows = OriginalDataTable.Rows(_report.Trees, OriginalDataTable.TreeColumns, tree => tree.UniqueId);
            width = OriginalDataTable.TotalWidth(OriginalDataTable.TreeColumns);
        }
        else
        {
            _headers = OriginalDataTable.Headers(OriginalDataTable.FloorColumns);
            _allRows = OriginalDataTable.Rows(_report.Floors, OriginalDataTable.FloorColumns, floor => floor.UniqueId);
            width = OriginalDataTable.TotalWidth(OriginalDataTable.FloorColumns);
        }

        SubtitleText.Text = $"{_report.DocumentTitle} — every element and every value the Dashboard read from Revit, exactly as stored: " +
                            "no unit or currency conversion, no projection, every design option and level. " +
                            (ShowingTrees ? "Each tree's Stored units and Stored currency columns say what basis its numbers are in." : string.Empty);
        TableRoot.Width = width + TableChrome;
        HeaderRow.ItemsSource = _headers;
        ApplySearch();
    }

    private void ApplySearch()
    {
        var search = SearchBox.Text?.Trim() ?? string.Empty;
        _visibleRows = _allRows.Where(row => row.Matches(search)).ToList();
        RowsList.ItemsSource = _visibleRows;
        var noun = ShowingTrees ? "tree" : "planting area";
        StatusText.Text = _visibleRows.Count == _allRows.Count
            ? $"{_allRows.Count:N0} {noun}(s)."
            : $"{_visibleRows.Count:N0} of {_allRows.Count:N0} {noun}(s) match \"{search}\".";
        UpdateRevitButtons();
    }

    private void DatasetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Fires once during InitializeComponent, before the table controls exist.
        if (TableRoot is not null)
        {
            Rebuild();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void RowsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRevitButtons();

    private void UpdateRevitButtons()
    {
        var canTalkToRevit = _revitClient is not null && RowsList.SelectedItems.Count > 0;
        SelectInRevitButton.IsEnabled = canTalkToRevit;
        ZoomInRevitButton.IsEnabled = canTalkToRevit;
    }

    private List<string> SelectedUniqueIds() => RowsList.SelectedItems.OfType<OriginalDataRow>().Select(row => row.UniqueId).ToList();

    private async void SelectInRevit_Click(object sender, RoutedEventArgs e) => await SendToRevitAsync(PipeCommands.SelectElements, "Selected");

    private async void ZoomInRevit_Click(object sender, RoutedEventArgs e) => await SendToRevitAsync(PipeCommands.ZoomToElements, "Zoomed to");

    private async Task SendToRevitAsync(string command, string verb)
    {
        var ids = SelectedUniqueIds();
        if (_revitClient is null || ids.Count == 0)
        {
            return;
        }

        try
        {
            var result = await _revitClient.SendAsync<OperationResult>(command, new ElementSelectionRequest(ids));
            StatusText.Text = result.Message ?? $"{verb} {ids.Count:N0} element(s) in Revit.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Couldn't reach Revit ({exception.Message}). If this is a cached snapshot, open the model and Refresh first.";
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{(ShowingTrees ? "Trees" : "Planting areas")} original data {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("CSV (opens in Excel)", [".csv"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await OriginalDataTable.WriteCsvAsync(file.Path, _headers, _visibleRows);
            StatusText.Text = $"Exported {_visibleRows.Count:N0} row(s) to {file.Name}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Export failed: {exception.Message}";
        }
    }
}
