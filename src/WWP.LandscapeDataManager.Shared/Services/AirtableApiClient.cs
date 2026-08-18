using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One Airtable base a token can access, as returned by the Metadata API.</summary>
public sealed record AirtableBaseSummary(string Id, string Name);

/// <summary>One table within a base, including its views — the Metadata API returns both in a single call.</summary>
public sealed record AirtableTableSummary(string Id, string Name, IReadOnlyList<AirtableViewSummary> Views);

/// <summary>One view within a table.</summary>
public sealed record AirtableViewSummary(string Id, string Name);

/// <summary>
/// Thrown when the Airtable Metadata API rejects a request with 403 Forbidden — almost always
/// because the personal access token was created without the <c>schema.bases:read</c> scope that
/// listing bases/tables/views requires (records-only tokens predate this browsing feature and
/// commonly lack it). Kept distinct from a generic <see cref="HttpRequestException"/> so callers
/// can show a scope-specific message instead of a generic network error.
/// </summary>
public sealed class AirtableInsufficientScopeException(string message) : Exception(message);

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

    /// <summary>Patches one or more fields on a single existing record — a partial update, so fields not listed are left untouched.</summary>
    public async Task UpdateRecordFieldsAsync(
        AirtableApiSettings settings,
        string apiToken,
        string recordId,
        IReadOnlyDictionary<string, object?> fields,
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

        if (string.IsNullOrWhiteSpace(recordId))
        {
            throw new ArgumentException("An Airtable record ID is required.", nameof(recordId));
        }

        var relative = $"{Uri.EscapeDataString(settings.BaseId)}/{Uri.EscapeDataString(settings.TableIdOrName)}/{Uri.EscapeDataString(recordId)}";
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(relative, UriKind.Relative))
        {
            Content = JsonContent.Create(new AirtableFieldUpdate(fields), options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

        using var response = await HttpClientInstance.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Airtable returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }
    }

    /// <summary>Lists every base the token can access — the first step of the base/table/view browser in Settings.</summary>
    public async Task<IReadOnlyList<AirtableBaseSummary>> GetBasesAsync(
        string apiToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ArgumentException("An Airtable personal access token is required.", nameof(apiToken));
        }

        var bases = new List<AirtableBaseSummary>();
        string? offset = null;

        do
        {
            var query = string.IsNullOrEmpty(offset) ? "" : $"?offset={Uri.EscapeDataString(offset)}";
            var page = await SendMetadataRequestAsync<AirtableBasesPage>(
                $"meta/bases{query}", apiToken, cancellationToken).ConfigureAwait(false);

            foreach (var b in page.Bases)
            {
                bases.Add(new AirtableBaseSummary(b.Id, b.Name));
            }

            offset = page.Offset;
        }
        while (!string.IsNullOrEmpty(offset));

        return bases;
    }

    /// <summary>Lists every table (and each table's views) in one base — the second/third steps of the browser.</summary>
    public async Task<IReadOnlyList<AirtableTableSummary>> GetTablesAsync(
        string apiToken,
        string baseId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ArgumentException("An Airtable personal access token is required.", nameof(apiToken));
        }

        if (string.IsNullOrWhiteSpace(baseId))
        {
            throw new ArgumentException("An Airtable base ID is required.", nameof(baseId));
        }

        var page = await SendMetadataRequestAsync<AirtableTablesPage>(
            $"meta/bases/{Uri.EscapeDataString(baseId)}/tables", apiToken, cancellationToken).ConfigureAwait(false);

        return page.Tables
            .Select(table => new AirtableTableSummary(
                table.Id,
                table.Name,
                table.Views.Select(view => new AirtableViewSummary(view.Id, view.Name)).ToList()))
            .ToList();
    }

    private static async Task<T> SendMetadataRequestAsync<T>(string relativeUri, string apiToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relativeUri, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

        using var response = await HttpClientInstance.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new AirtableInsufficientScopeException(
                "Airtable rejected this request with 403 Forbidden. The personal access token likely needs the 'schema.bases:read' scope added to browse bases/tables/views.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Airtable returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException("Airtable returned an empty response.");
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

    private sealed record AirtableFieldUpdate(IReadOnlyDictionary<string, object?> Fields);

    private sealed record AirtablePage(IReadOnlyList<AirtableApiRecord> Records, string? Offset);

    private sealed record AirtableApiRecord(string Id, Dictionary<string, JsonElement> Fields);

    private sealed record AirtableBasesPage(IReadOnlyList<AirtableApiBase> Bases, string? Offset);

    private sealed record AirtableApiBase(string Id, string Name);

    private sealed record AirtableTablesPage(IReadOnlyList<AirtableApiTable> Tables);

    private sealed record AirtableApiTable(string Id, string Name, IReadOnlyList<AirtableApiView> Views);

    private sealed record AirtableApiView(string Id, string Name);
}
