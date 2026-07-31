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

    private readonly NominatimGeocodingClient _geocodingClient = new();
    private RevitPipeClient? _revitClient;
    private nint _windowHandle;
    private bool _mapReady;

    public MainPage()
    {
        InitializeComponent();
    }

    public void Initialize(string pipeName, nint windowHandle)
    {
        _revitClient = new RevitPipeClient(pipeName);
        _windowHandle = windowHandle;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await MapView.EnsureCoreWebView2Async();
        MapView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        var mapPath = Path.Combine(AppContext.BaseDirectory, "MapContent", "map.html");
        MapView.Source = new Uri(mapPath);
        _mapReady = true;
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
            StatusText.Text = $"Published {result.Latitude:F6}, {result.Longitude:F6} to {result.DocumentTitle}.";
        });
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
