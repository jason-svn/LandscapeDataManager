using System.Net.Http.Headers;
using System.Text.Json;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Reads records directly from the Airtable REST API using a personal access token — the
/// reliable replacement for scraping a public shared view through a WebView2 control. Requires
/// a base ID and table (name or ID) in addition to the token, since a scoped token no longer
/// carries an implicit "which base/view" the way a public share link did.
/// </summary>
public sealed class AirtableApiClient
{
    private static readonly HttpClient HttpClientInstance = new()
    {
        BaseAddress = new Uri("https://api.airtable.com/v0/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<IReadOnlyList<AirtableRecord>> GetRecordsAsync(
        AirtableApiSettings settings,
        string apiToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ArgumentException("An Airtable personal access token is required.", nameof(apiToken));
        }

        if (string.IsNullOrWhiteSpace(settings.BaseId) || string.IsNullOrWhiteSpace(settings.TableIdOrName))
        {
            throw new ArgumentException("An Airtable base ID and table name (or ID) are required.");
        }

        var records = new List<AirtableRecord>();
        string? offset = null;

        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(settings, offset));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

            using var response = await HttpClientInstance.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Airtable returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<AirtablePage>(stream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException("Airtable returned an empty response.");

            foreach (var record in page.Records)
            {
                records.Add(new AirtableRecord(record.Id, record.Fields));
            }

            offset = page.Offset;
        }
        while (!string.IsNullOrEmpty(offset));

        return records;
    }

    private static Uri BuildUri(AirtableApiSettings settings, string? offset)
    {
        var query = new List<string> { "pageSize=100" };
        if (!string.IsNullOrWhiteSpace(settings.ViewName))
        {
            query.Add($"view={Uri.EscapeDataString(settings.ViewName)}");
        }

        if (!string.IsNullOrEmpty(offset))
        {
            query.Add($"offset={Uri.EscapeDataString(offset)}");
        }

        var relative = $"{Uri.EscapeDataString(settings.BaseId)}/{Uri.EscapeDataString(settings.TableIdOrName)}?{string.Join('&', query)}";
        return new Uri(relative, UriKind.Relative);
    }

    private static string Truncate(string value) => value.Length > 300 ? value[..300] + "..." : value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record AirtablePage(IReadOnlyList<AirtableApiRecord> Records, string? Offset);

    private sealed record AirtableApiRecord(string Id, Dictionary<string, JsonElement> Fields);
}
