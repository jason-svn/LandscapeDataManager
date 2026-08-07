using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.ITreeDownloader;

public sealed partial class MainPage : Page
{
    private readonly ITreeApiClient _iTreeApiClient = new();
    private readonly ITreeCredentialStore _iTreeCredentialStore = new();
    private readonly SpeciesCatalogueDatabase _database = new();
    private readonly ITreeExcelMergeService _excelMergeService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<SpeciesResultRow> ResultRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApiKeyStatusText.Text = string.IsNullOrWhiteSpace(_iTreeCredentialStore.Load())
            ? "No i-Tree API key saved — open Settings from the LIM ribbon first."
            : "i-Tree API key: saved (managed in Settings).";
        var metadata = await _database.GetMetadataAsync();
        if (metadata.Version is not null)
        {
            CatalogueVersionText.Text = $"Local catalogue last downloaded {metadata.FormatDownloadedAtLocal() ?? metadata.DownloadedAtUtc} (version {metadata.Version[..8]}...).";
        }

        UpdateExcelPanelVisibility();
    }

    private void DestinationCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateExcelPanelVisibility();

    private void UpdateExcelPanelVisibility()
    {
        if (ExcelOptionsPanel is null)
        {
            return;
        }

        ExcelOptionsPanel.Visibility = ExportExcelCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowseExistingWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xlsm");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ExcelPathBox.Text = file.Path;
        }
    }

    private async void ChooseNewWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Species Catalogue {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("Excel workbook", [".xlsx"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            ExcelPathBox.Text = file.Path;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var exportExcel = ExportExcelCheckBox.IsChecked == true;

        var apiKey = _iTreeCredentialStore.Load().Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText.Text = "No i-Tree API key saved — open Settings from the LIM ribbon first.";
            return;
        }

        BusyIndicator.IsActive = true;
        BusyIndicator.Visibility = Visibility.Visible;
        try
        {
            var download = await _iTreeApiClient.DownloadSpeciesCatalogAsync(apiKey);
            var allRecords = download.Records.Select(ToSpeciesCatalogueRecord).ToList();
            var catalogueVersion = ComputeVersionHash(allRecords);

            var fullCatalogueScope = ScopeBox.SelectedIndex == 1;
            var recordsForLocalCache = allRecords;
            if (!fullCatalogueScope)
            {
                var usedCodes = await GetUsedSpeciesCodesAsync();
                recordsForLocalCache = allRecords.Where(r => usedCodes.Contains(r.SpeciesCode)).ToList();
            }

            await _database.SaveAsync(recordsForLocalCache, catalogueVersion);
            CatalogueVersionText.Text =
                $"Downloaded {allRecords.Count:N0} species; cached {recordsForLocalCache.Count:N0} locally (version {catalogueVersion[..8]}...).";

            var notes = new List<string>();

            var updateResult = await GetClient().SendAsync<UpdateSpeciesCatalogueResult>(
                PipeCommands.UpdateSpeciesCatalogue,
                new UpdateSpeciesCatalogueRequest(allRecords));

            ResultRows.Clear();
            foreach (var row in updateResult.Rows)
            {
                ResultRows.Add(new SpeciesResultRow(row));
            }

            AddedCountText.Text = updateResult.Rows.Count(r => r.Status == "Added").ToString("N0");
            ChangedCountText.Text = updateResult.Rows.Count(r => r.Status == "Changed").ToString("N0");
            DeprecatedCountText.Text = updateResult.Rows.Count(r => r.Status == "Deprecated").ToString("N0");
            UnchangedCountText.Text = updateResult.Rows.Count(r => r.Status == "Unchanged").ToString("N0");
            notes.Add(BuildUpdateSummary(updateResult));

            if (exportExcel)
            {
                var exportRecords = fullCatalogueScope ? allRecords : recordsForLocalCache;
                var merge = await _excelMergeService.MergeAsync(
                    exportRecords.Select(ToExportRecord).ToList(),
                    new ITreeExcelMergeOptions(
                        ExcelPathBox.Text.Trim(),
                        WorksheetBox.Text.Trim(),
                        1,
                        AppendMissingCheckBox.IsChecked == true,
                        true));
                notes.Add(
                    $"Excel: updated {merge.UpdatedRows:N0} rows, appended {merge.AppendedRows:N0}, " +
                    $"preserved {merge.PreservedUnmatchedRows:N0} unmatched rows in {Path.GetFileName(merge.FilePath)}.");
            }

            StatusText.Text = string.Join(" ", notes);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed: {exception.Message}";
        }
        finally
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private static string BuildUpdateSummary(UpdateSpeciesCatalogueResult result)
    {
        if (result.TotalPlantingTypes == 0)
        {
            return "No Planting types were found in this model — place at least one Planting family instance (or load a Planting family) before there's anything for species data to attach to.";
        }

        var diagnostics = new List<string>();
        if (result.TypesMissingSpeciesCodeParameter > 0)
        {
            diagnostics.Add($"{result.TypesMissingSpeciesCodeParameter:N0} don't have the Species_Code shared parameter bound yet (run Shared Parameter Setup first)");
        }

        if (result.TypesWithEmptySpeciesCode > 0)
        {
            diagnostics.Add($"{result.TypesWithEmptySpeciesCode:N0} have the parameter but no code entered yet");
        }

        if (result.SkippedNoMatchingType > 0)
        {
            diagnostics.Add($"{result.SkippedNoMatchingType:N0} have a code this catalogue doesn't recognize");
        }

        var summary = $"Updated {result.Rows.Count:N0} of {result.TotalPlantingTypes:N0} Planting types.";
        return diagnostics.Count > 0 ? $"{summary} {string.Join("; ", diagnostics)}." : summary;
    }

    private async Task<HashSet<string>> GetUsedSpeciesCodesAsync()
    {
        var scan = await GetClient().SendAsync<ITreeInputScanResult>(PipeCommands.GetITreeInputs, new ITreeInputOptions(false));
        return scan.Items
            .Select(item => item.SpeciesCode.Trim())
            .Where(code => !string.IsNullOrEmpty(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static SpeciesCatalogueRecord ToSpeciesCatalogueRecord(ITreeExportRecord record)
    {
        string Get(string key) => record.Fields.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        var replaceBy = Get("ReplaceBy");
        return new SpeciesCatalogueRecord(
            record.SpeciesCode,
            Get("Common_Name"),
            Get("Scientific_Name"),
            Get("SpeciesType"),
            string.IsNullOrWhiteSpace(replaceBy) ? null : replaceBy);
    }

    private static ITreeExportRecord ToExportRecord(SpeciesCatalogueRecord record) => new(
        record.SpeciesCode,
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Species_Code"] = record.SpeciesCode,
            ["Common_Name"] = record.CommonName,
            ["Scientific_Name"] = record.ScientificName,
            ["SpeciesType"] = record.SpeciesType,
            ["ReplaceBy"] = record.ReplaceBy ?? string.Empty
        });

    private static string ComputeVersionHash(IReadOnlyList<SpeciesCatalogueRecord> records)
    {
        var builder = new StringBuilder();
        foreach (var record in records.OrderBy(r => r.SpeciesCode, StringComparer.Ordinal))
        {
            builder.Append(record.SpeciesCode).Append('|')
                .Append(record.CommonName).Append('|')
                .Append(record.ScientificName).Append('|')
                .Append(record.SpeciesType).Append('|')
                .Append(record.ReplaceBy).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
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
