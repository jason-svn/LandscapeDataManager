using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WWP.LandscapeDataManager.App.LocationFinder;

/// <summary>
/// Minimal client for OSM's free Nominatim geocoder. Only ever called once per user-initiated
/// search click, well under its usage-policy rate limit (max ~1 request/second, valid User-Agent
/// required — see https://operations.osmfoundation.org/policies/nominatim/).
/// </summary>
public sealed class NominatimGeocodingClient
{
    private static readonly HttpClient HttpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WWP.LandscapeDataManager.LocationFinder", "1.0"));
        return client;
    }

    public async Task<GeocodeResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(query)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var results = await JsonSerializer.DeserializeAsync<List<NominatimEntry>>(stream, cancellationToken: cancellationToken);
        var first = results?.FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        return new GeocodeResult(
            double.Parse(first.Lat, CultureInfo.InvariantCulture),
            double.Parse(first.Lon, CultureInfo.InvariantCulture),
            first.DisplayName);
    }

    /// <summary>Country only (zoom=3) — the coarsest, lightest reverse-geocode lookup Nominatim offers, since that's all currency-matching needs.</summary>
    public async Task<string?> ReverseCountryCodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var url = "https://nominatim.openstreetmap.org/reverse?format=json&zoom=3&addressdetails=1" +
                  $"&lat={latitude.ToString(CultureInfo.InvariantCulture)}&lon={longitude.ToString(CultureInfo.InvariantCulture)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<NominatimReverseResult>(stream, cancellationToken: cancellationToken);
        return result?.Address?.CountryCode;
    }

    private sealed record NominatimEntry(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon,
        [property: JsonPropertyName("display_name")] string DisplayName);

    private sealed record NominatimReverseResult(
        [property: JsonPropertyName("address")] NominatimAddress? Address);

    private sealed record NominatimAddress(
        [property: JsonPropertyName("country_code")] string? CountryCode);
}

public sealed record GeocodeResult(double Latitude, double Longitude, string DisplayName);
