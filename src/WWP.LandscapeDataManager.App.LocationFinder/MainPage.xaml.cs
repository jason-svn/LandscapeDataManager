using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Services;

namespace WWP.LandscapeDataManager.App.LocationFinder;

public sealed partial class MainPage : Page
{
    private static readonly JsonSerializerOptions MessageOptions = new() { PropertyNameCaseInsensitive = true };
    private const string MapVirtualHost = "locationfinder.lim-landscape-data.local";

    private readonly NominatimGeocodingClient _geocodingClient = new();
    private readonly ExchangeRateService _exchangeRateService = new();
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private string _pipeName = string.Empty;
    private bool _mapReady;
    private bool _savedShown;

    public MainPage()
    {
        InitializeComponent();
    }

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
        await MapView.EnsureCoreWebView2Async();
        MapView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        MapView.CoreWebView2.NavigationCompleted += MapView_NavigationCompleted;

        // Served from a virtual https host rather than file://, because OSM's tile usage policy
        // blocks (403 "Access blocked") tile requests that arrive without a Referer header, and
        // file:// pages never send one.
        MapView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            MapVirtualHost,
            Path.Combine(AppContext.BaseDirectory, "MapContent"),
            CoreWebView2HostResourceAccessKind.Allow);
        MapView.Source = new Uri($"https://{MapVirtualHost}/map.html");
        _mapReady = true;
    }

    /// <summary>Once the map has loaded, show the location the project already has.</summary>
    private async void MapView_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_savedShown || !args.IsSuccess)
        {
            return;
        }

        _savedShown = true;
        try
        {
            var result = await GetClient().SendAsync<ProjectSiteLocationResult>(PipeCommands.GetProjectSiteLocation, null);
            await ShowProjectLocationAsync(result.Latitude, result.Longitude, result.PlaceName, result.IsPublished);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Couldn't read the project's location: {exception.Message}";
        }
    }

    /// <summary>
    /// Shows the project's location on the map and says whether it's set: the published i-Tree
    /// location (green), only Revit's Site Location (amber, a starting point), or either (an older
    /// connector can't tell).
    /// </summary>
    private async Task ShowProjectLocationAsync(double latitude, double longitude, string? placeName, bool? isPublished)
    {
        var where = $"{latitude:F6}, {longitude:F6}" + (string.IsNullOrWhiteSpace(placeName) ? string.Empty : $" ({placeName})");
        SetCoordinateBoxes(latitude, longitude);
        if (isPublished == false)
        {
            await MoveMapPinAsync(latitude, longitude);
            PublishButton.IsEnabled = true;
            LocationStatusBar.Severity = InfoBarSeverity.Warning;
            LocationStatusBar.Title = "No location published yet";
            LocationStatusBar.Message = $"Showing Revit's Site Location, {where}, as a starting point. Check it, move the pin if needed, and publish: i-Tree needs it.";
        }
        else
        {
            await ShowSavedMarkerAsync(latitude, longitude);
            LocationStatusBar.Severity = isPublished == true ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
            LocationStatusBar.Title = isPublished == true ? "Location set" : "Project location";
            LocationStatusBar.Message = $"This project's location is {where}" +
                                        (isPublished == true ? "; every i-Tree calculation uses it." : ".") +
                                        " To change it, pick a new point and publish.";
        }

        LocationStatusBar.IsOpen = true;
    }

    private async Task ShowSavedMarkerAsync(double latitude, double longitude)
    {
        if (!_mapReady)
        {
            return;
        }

        var script = $"showSaved({latitude.ToString(CultureInfo.InvariantCulture)}, {longitude.ToString(CultureInfo.InvariantCulture)}, 'Project location')";
        await MapView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var json = args.TryGetWebMessageAsString();
        if (json is null)
        {
            return;
        }

        var message = JsonSerializer.Deserialize<LatLngMessage>(json, MessageOptions);
        if (message is null)
        {
            return;
        }

        SetCoordinateBoxes(message.Lat, message.Lng);
        PublishButton.IsEnabled = true;
        StatusText.Text = $"Pin set at {message.Lat:F6}, {message.Lng:F6}. Click Publish to write it to the project.";
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var query = AddressBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusText.Text = "Type an address or place name to search.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _geocodingClient.SearchAsync(query);
            if (result is null)
            {
                StatusText.Text = $"No match found for '{query}'.";
                return;
            }

            SetCoordinateBoxes(result.Latitude, result.Longitude);
            await MoveMapPinAsync(result.Latitude, result.Longitude);
            PublishButton.IsEnabled = true;
            StatusText.Text = $"Found: {result.DisplayName}";
        });
    }

    private async void GetFromFile_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<ProjectSiteLocationResult>(PipeCommands.GetProjectSiteLocation, null);
            SetCoordinateBoxes(result.Latitude, result.Longitude);
            await MoveMapPinAsync(result.Latitude, result.Longitude);
            PublishButton.IsEnabled = true;
            StatusText.Text = string.IsNullOrWhiteSpace(result.PlaceName)
                ? $"Read {result.Latitude:F6}, {result.Longitude:F6} from the project's Site Location (Manage tab). This may just be the template default if nobody has set it."
                : $"Read '{result.PlaceName}' ({result.Latitude:F6}, {result.Longitude:F6}) from the project's Site Location. This may just be the template default if nobody has set it.";
        });
    }

    private async void UpdatePin_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadCoordinateBoxes(out var latitude, out var longitude))
        {
            StatusText.Text = "Enter valid numeric latitude and longitude first.";
            return;
        }

        await MoveMapPinAsync(latitude, longitude);
        PublishButton.IsEnabled = true;
        StatusText.Text = $"Pin moved to {latitude:F6}, {longitude:F6}. Click Publish to write it to the project.";
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadCoordinateBoxes(out var latitude, out var longitude))
        {
            StatusText.Text = "Enter valid numeric latitude and longitude first.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await GetClient().SendAsync<PublishProjectLocationResult>(
                PipeCommands.PublishProjectLocation,
                new PublishProjectLocationRequest(latitude, longitude));
            var message = $"Published {result.Latitude:F6}, {result.Longitude:F6} to {result.DocumentTitle}.";
            await ShowProjectLocationAsync(result.Latitude, result.Longitude, null, true);

            if (MatchCurrencyBox.IsChecked == true)
            {
                message += " " + await MatchCurrencyToLocationAsync(latitude, longitude);
            }

            StatusText.Text = message;
        });
    }

    /// <summary>Reverse-geocodes to a country, maps it to one of the six LIM currency codes, resolves the current USD exchange rate, and publishes both — best-effort, since not every country maps to a currency this tool supports.</summary>
    private async Task<string> MatchCurrencyToLocationAsync(double latitude, double longitude)
    {
        var countryCode = await _geocodingClient.ReverseCountryCodeAsync(latitude, longitude);
        var currency = CurrencyByCountryCode.Resolve(countryCode);
        if (currency is null)
        {
            return "Couldn't determine a matching currency for this location — left the current preference as-is.";
        }

        var rate = await ProjectExchangeRate.GetUsdRateAsync(GetClient(), _exchangeRateService, currency);
        if (!rate.Success)
        {
            // Publishing at the 1:1 fallback would rescale every stored cost onto a false rate.
            return $"Preferred currency not changed to {currency}: couldn't get today's rate ({ProjectExchangeRate.FailureReason(rate)}). " +
                   "Try again, or set the currency and a fixed rate in Settings > Units & Currency.";
        }

        await GetClient().SendAsync<PublishPreferredCurrencyResult>(
            PipeCommands.PublishPreferredCurrency, new PublishPreferredCurrencyRequest(currency, rate.UsdRate));
        await ProjectSettingsSync.PushAsync(GetClient(), preferredCurrency: currency);
        return rate.Success
            ? $"Set preferred currency to {currency} (factor {rate.UsdRate:G6}) to match this location."
            : $"Set preferred currency to {currency} to match this location. {rate.Error}";
    }

    private void SetCoordinateBoxes(double latitude, double longitude)
    {
        LatitudeBox.Text = latitude.ToString("F6", CultureInfo.InvariantCulture);
        LongitudeBox.Text = longitude.ToString("F6", CultureInfo.InvariantCulture);
    }

    private bool TryReadCoordinateBoxes(out double latitude, out double longitude)
    {
        var latitudeParsed = double.TryParse(LatitudeBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude);
        var longitudeParsed = double.TryParse(LongitudeBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude);
        return latitudeParsed && longitudeParsed;
    }

    private async Task MoveMapPinAsync(double latitude, double longitude)
    {
        if (!_mapReady)
        {
            return;
        }

        var script = $"setMarker({latitude.ToString(CultureInfo.InvariantCulture)}, {longitude.ToString(CultureInfo.InvariantCulture)})";
        await MapView.CoreWebView2.ExecuteScriptAsync(script);
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

    private sealed record LatLngMessage(double Lat, double Lng);
}
