using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.Dashboard;

public sealed partial class MainPage : Page
{
    private const string AllDesignOptions = "All design options";
    private const string AllLevels = "All levels";
    private const string ProjectDefaultUnitSystem = "Project default";

    private readonly ExchangeRateService _exchangeRateService = new();

    private RevitPipeClient? _revitClient;
    private nint _windowHandle;

    private DashboardReportResult? _lastReport;
    private string _preferredUnitSystem = "Metric";
    private string _preferredCurrency = "USD";
    private double _usdExchangeRate = 1d;

    private string _unitSystemOverride = ProjectDefaultUnitSystem;
    private bool _showAnnual = true;
    private string? _activeStatusFilter;
    private string _selectedDesignOption = AllDesignOptions;
    private string _selectedLevel = AllLevels;
    private bool _suppressFilterEvents;

    public MainPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<SpeciesSubtotalRow> SpeciesSubtotals { get; } = [];
    public ObservableCollection<FloorSubtotalRow> FloorSubtotals { get; } = [];
    public ObservableCollection<DashboardTreeRow> TreeRows { get; } = [];
    public ObservableCollection<DashboardFloorRow> FloorRows { get; } = [];

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(RefreshAsync);

    private async Task RefreshAsync()
    {
        var selectedOnly = ScopeBox.SelectedIndex == 1;
        var catalogTask = GetClient().SendAsync<ParameterCatalogResult>(PipeCommands.GetParameterCatalog, new ModelScanOptions());
        var reportTask = GetClient().SendAsync<DashboardReportResult>(PipeCommands.GetDashboardReport, new DashboardReportRequest(selectedOnly));
        await Task.WhenAll(catalogTask, reportTask);

        var catalog = await catalogTask;
        _lastReport = await reportTask;
        _preferredUnitSystem = catalog.PreferredUnitSystem;
        _preferredCurrency = catalog.PreferredCurrency;
        CurrencyText.Text = _preferredCurrency;

        // One rate lookup per refresh, not per tree — the rate doesn't vary by tree, only by currency.
        var rate = await _exchangeRateService.GetUsdRateAsync(_preferredCurrency);
        _usdExchangeRate = rate.UsdRate;

        RebuildFilterOptions();
        RebuildRows();

        var rateNote = rate.Success ? string.Empty : $" ({rate.Error})";
        StatusText.Text = $"{_lastReport.Trees.Count:N0} tree(s), {_lastReport.Floors.Count:N0} floor(s) — {EffectiveUnitSystem()} / {_preferredCurrency}.{rateNote}";
    }

    private string EffectiveUnitSystem() =>
        _unitSystemOverride == ProjectDefaultUnitSystem ? _preferredUnitSystem : _unitSystemOverride;

    private void RebuildFilterOptions()
    {
        if (_lastReport is null)
        {
            return;
        }

        _suppressFilterEvents = true;
        try
        {
            var designOptions = _lastReport.Trees.Select(tree => tree.DesignOption)
                .Concat(_lastReport.Floors.Select(floor => floor.DesignOption))
                .Select(DashboardUnitLabels.FormatDesignOption)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            DesignOptionBox.Items.Clear();
            DesignOptionBox.Items.Add(AllDesignOptions);
            foreach (var option in designOptions)
            {
                DesignOptionBox.Items.Add(option);
            }

            DesignOptionBox.SelectedItem = DesignOptionBox.Items.Contains(_selectedDesignOption)
                ? _selectedDesignOption
                : AllDesignOptions;

            var levels = _lastReport.Trees.Select(tree => tree.LevelName)
                .Concat(_lastReport.Floors.Select(floor => floor.LevelName))
                .Select(level => level ?? "—")
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            LevelBox.Items.Clear();
            LevelBox.Items.Add(AllLevels);
            foreach (var level in levels)
            {
                LevelBox.Items.Add(level);
            }

            LevelBox.SelectedItem = LevelBox.Items.Contains(_selectedLevel) ? _selectedLevel : AllLevels;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void RebuildRows()
    {
        if (_lastReport is null)
        {
            return;
        }

        var unitSystem = EffectiveUnitSystem();
        var normalizedTrees = _lastReport.Trees
            .Select(tree => DashboardAggregationService.NormalizeTree(tree, unitSystem, _preferredCurrency, _usdExchangeRate))
            .ToList();

        var searchText = SpeciesSearchBox.Text?.Trim() ?? string.Empty;
        bool MatchesDimensionFilters(DesignOptionInfo designOption, string? levelName) =>
            (_selectedDesignOption == AllDesignOptions || DashboardUnitLabels.FormatDesignOption(designOption) == _selectedDesignOption) &&
            (_selectedLevel == AllLevels || (levelName ?? "—") == _selectedLevel);

        var treesInScope = normalizedTrees
            .Where(tree => MatchesDimensionFilters(tree.Source.DesignOption, tree.Source.LevelName))
            .Where(tree => string.IsNullOrEmpty(searchText) ||
                           (tree.Source.SpeciesCode ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                           (tree.Source.CommonName ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        UpdateStatusCounts(treesInScope);

        var filteredTrees = treesInScope
            .Where(tree => _activeStatusFilter is null || tree.Source.Status == _activeStatusFilter)
            .ToList();
        var filteredFloors = _lastReport.Floors
            .Where(floor => MatchesDimensionFilters(floor.DesignOption, floor.LevelName))
            .ToList();

        SpeciesSubtotals.Clear();
        foreach (var subtotal in DashboardAggregationService.AggregateTreesBySpecies(filteredTrees))
        {
            SpeciesSubtotals.Add(new SpeciesSubtotalRow(subtotal, unitSystem, _preferredCurrency, _showAnnual));
        }

        FloorSubtotals.Clear();
        foreach (var subtotal in DashboardAggregationService.AggregateFloorsByType(filteredFloors))
        {
            FloorSubtotals.Add(new FloorSubtotalRow(subtotal));
        }

        TreeRows.Clear();
        foreach (var tree in filteredTrees)
        {
            TreeRows.Add(new DashboardTreeRow(tree, unitSystem, _preferredCurrency, _showAnnual));
        }

        FloorRows.Clear();
        foreach (var floor in filteredFloors)
        {
            FloorRows.Add(new DashboardFloorRow(floor));
        }

        var grandTotal = DashboardAggregationService.BuildGrandTotal(filteredTrees, filteredFloors);
        var co2Unit = DashboardUnitLabels.Co2Unit(unitSystem);
        var pollutantUnit = DashboardUnitLabels.PollutantUnit(unitSystem);
        var period = _showAnnual ? "Annual" : "Lifetime";

        TreeCountText.Text = grandTotal.TreeCount.ToString("N0");
        TreeExcludedText.Text = grandTotal.TreesExcludedFromTotals > 0
            ? $"{grandTotal.TreesExcludedFromTotals:N0} excluded (not yet Calculated)"
            : string.Empty;
        PollutionMassText.Text = _showAnnual
            ? $"{grandTotal.TotalPollutionMassRemovedAnnual:N2} {pollutantUnit}"
            : $"{grandTotal.TotalPollutionMassRemovedLifetimeTotal:N2} {pollutantUnit}";
        CO2SequesteredText.Text = _showAnnual
            ? $"{grandTotal.CO2SequesteredAnnual:N1} {co2Unit}"
            : $"{grandTotal.CO2SequesteredLifetimeTotal:N1} {co2Unit}";
        TreeCostSavedText.Text = _showAnnual
            ? $"{grandTotal.TreeCostSavedAnnual:N2} {_preferredCurrency}"
            : $"{grandTotal.TreeCostSavedLifetimeTotal:N2} {_preferredCurrency}";

        FloorCountText.Text = grandTotal.FloorCount.ToString("N0");
        FloorAreaText.Text = $"{grandTotal.FloorAreaSquareMeters:N1} m²";
        FloorCO2Text.Text = $"{grandTotal.FloorCO2SequesteredAnnual:N1} kg";
        FloorGwpText.Text = grandTotal.FloorTotalGwp.ToString("N1");
        FloorCostSavedText.Text = $"{grandTotal.FloorCostSavedAnnual:N2} (as calculated)";

        PeriodLabelText.Text = $"Showing {period} benefits.";
    }

    private void UpdateStatusCounts(IReadOnlyList<NormalizedTreeMetrics> treesInScope)
    {
        int Count(string status) => treesInScope.Count(tree => tree.Source.Status == status);
        CalculatedCountText.Text = Count("Calculated").ToString("N0");
        ReadyCountText.Text = Count("Ready").ToString("N0");
        StaleCountText.Text = Count("Stale").ToString("N0");
        MissingCountText.Text = Count("MissingInput").ToString("N0");
        InvalidCountText.Text = Count("InvalidInput").ToString("N0");
        WarningCountText.Text = Count("APIWarning").ToString("N0");
        ErrorCountText.Text = Count("APIError").ToString("N0");

        foreach (var card in new[] { CalculatedCard, ReadyCard, StaleCard, MissingCard, InvalidCard, WarningCard, ErrorCard })
        {
            var isActive = _activeStatusFilter is not null && (string)card.Tag == _activeStatusFilter;
            if (isActive)
            {
                card.BorderBrush = (Brush)Application.Current.Resources["LimAccentBrush"];
                card.BorderThickness = new Thickness(2);
            }
            else
            {
                card.ClearValue(Border.BorderBrushProperty);
                card.ClearValue(Border.BorderThicknessProperty);
            }
        }
    }

    private void StatusCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var status = (string)((FrameworkElement)sender).Tag;
        _activeStatusFilter = _activeStatusFilter == status ? null : status;
        RebuildRows();
    }

    private void DesignOptionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || DesignOptionBox.SelectedItem is not string value)
        {
            return;
        }

        _selectedDesignOption = value;
        RebuildRows();
    }

    private void LevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || LevelBox.SelectedItem is not string value)
        {
            return;
        }

        _selectedLevel = value;
        RebuildRows();
    }

    private void SpeciesSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildRows();

    private void UnitSystemBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || UnitSystemBox.SelectedItem is not ComboBoxItem { Content: string value })
        {
            return;
        }

        _unitSystemOverride = value;
        RebuildRows();
    }

    private void PeriodToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _showAnnual = PeriodToggle.IsOn;
        RebuildRows();
    }

    private async void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
        {
            StatusText.Text = "Refresh the dashboard before exporting.";
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Landscape Dashboard {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("Excel workbook", [".xlsx"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var unitSystem = EffectiveUnitSystem();
            var normalizedTrees = TreeRows.Select(row => row.Metrics).ToList();
            var floors = FloorRows.Select(row => row.Item).ToList();
            var grandTotal = DashboardAggregationService.BuildGrandTotal(normalizedTrees, floors);
            var request = new DashboardExcelExportRequest(
                file.Path,
                _lastReport.DocumentTitle,
                unitSystem,
                _preferredCurrency,
                _showAnnual ? "Annual" : "Lifetime Total",
                grandTotal,
                DashboardAggregationService.AggregateTreesBySpecies(normalizedTrees),
                DashboardAggregationService.AggregateFloorsByType(floors),
                normalizedTrees,
                floors);

            var result = await DashboardExcelExportService.ExportAsync(request);
            StatusText.Text = $"Exported dashboard workbook to {Path.GetFileName(result.FilePath)}.";
        });
    }

    private async void ExportImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"Landscape Dashboard {DateTime.Now:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var renderTarget = new RenderTargetBitmap();
            await renderTarget.RenderAsync(CaptureRoot);
            var pixels = await renderTarget.GetPixelsAsync();

            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)renderTarget.PixelWidth,
                (uint)renderTarget.PixelHeight,
                96, 96,
                pixels.ToArray());
            await encoder.FlushAsync();

            StatusText.Text = $"Exported dashboard image to {file.Name}.";
        });
    }

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
