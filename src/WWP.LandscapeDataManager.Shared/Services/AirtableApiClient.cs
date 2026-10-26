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
/// Thrown when Airtable rejects a request with 403 Forbidden — for the Metadata API almost always
/// because the personal access token was created without the <c>schema.bases:read</c> scope that
/// listing bases/tables/views requires (records-only tokens predate this browsing feature and
/// commonly lack it); for adding records, a token without <c>data.records:write</c> or edit access.
/// Kept distinct from a generic <see cref="HttpRequestException"/> so callers can show a
/// scope-specific message instead of a generic network error.
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

    /// <summary>
    /// Adds records to the table, ten per request (Airtable's limit) with a pause between requests to
    /// stay under its five requests a second; select values are typecast. Returns the added records,
    /// with their new IDs, in order. A failed request stops there: the earlier requests' records are
    /// already added, and the exception says how many. A 403 means the token can't write records.
    /// </summary>
    public async Task<IReadOnlyList<AirtableRecord>> CreateRecordsAsync(
        AirtableApiSettings settings,
        string apiToken,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
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

        var created = new List<AirtableRecord>();
        var relative = $"{Uri.EscapeDataString(settings.BaseId)}/{Uri.EscapeDataString(settings.TableIdOrName)}";
        foreach (var batch in records.Chunk(MaxRecordsPerRequest))
        {
            if (created.Count > 0)
            {
                await Task.Delay(RequestSpacing, cancellationToken).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(relative, UriKind.Relative))
            {
                Content = JsonContent.Create(new AirtableRecordsCreate([.. batch.Select(fields => new AirtableFieldUpdate(fields))], true), options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

            using var response = await HttpClientInstance.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var added = created.Count > 0 ? $" {created.Count:N0} record(s) were added before this." : string.Empty;
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new AirtableInsufficientScopeException(
                        "Airtable refused to add records (403 Forbidden): the token needs the 'data.records:write' scope and edit access to this base." + added);
                }

                throw new HttpRequestException(
                    $"Airtable returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}{added}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<AirtablePage>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException("Airtable returned an empty response.");
            created.AddRange(page.Records.Select(record => new AirtableRecord(record.Id, record.Fields)));
        }

        return created;
    }

    /// <summary>
    /// Makes sure the source table has a number column called <paramref name="fieldName"/>, adding
    /// it when it's missing. True if it was added. Adding needs the token's 'schema.bases:write'
    /// scope and creator access to the base.
    /// </summary>
    public async Task<bool> EnsureNumberFieldAsync(
        AirtableApiSettings settings,
        string apiToken,
        string fieldName,
        int precision,
        string description,
        CancellationToken cancellationToken = default)
    {
        var page = await SendMetadataRequestAsync<AirtableTablesPage>(
            $"meta/bases/{Uri.EscapeDataString(settings.BaseId)}/tables", apiToken, cancellationToken).ConfigureAwait(false);
        var table = page.Tables.FirstOrDefault(table =>
                        string.Equals(table.Id, settings.TableIdOrName, StringComparison.Ordinal) ||
                        string.Equals(table.Name.Trim(), settings.TableIdOrName.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Airtable has no table \"{settings.TableIdOrName}\" in this base.");
        if ((table.Fields ?? []).Any(field => string.Equals(field.Name.Trim(), fieldName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"meta/bases/{Uri.EscapeDataString(settings.BaseId)}/tables/{Uri.EscapeDataString(table.Id)}/fields", UriKind.Relative))
        {
            Content = JsonContent.Create(
                new { name = fieldName, type = "number", description, options = new { precision } }, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

        using var response = await HttpClientInstance.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new AirtableInsufficientScopeException(
                $"Airtable didn't let this token add the \"{fieldName}\" column (it needs the 'schema.bases:write' scope and creator access). " +
                "Add it by hand as a Number column with 2 decimals, then push again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Airtable returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        return true;
    }

    /// <summary>
    /// The currency of each of the table's Airtable currency fields, by field name ("£" → GBP; see
    /// <see cref="Money.CodeForSymbol"/>): their cells arrive as bare numbers, the symbol only in the
    /// field's settings. Needs the token's 'schema.bases:read' scope.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetFieldCurrenciesAsync(
        AirtableApiSettings settings,
        string apiToken,
        CancellationToken cancellationToken = default)
    {
        var page = await SendMetadataRequestAsync<AirtableTablesPage>(
            $"meta/bases/{Uri.EscapeDataString(settings.BaseId)}/tables", apiToken, cancellationToken).ConfigureAwait(false);
        var table = page.Tables.FirstOrDefault(table =>
            string.Equals(table.Id, settings.TableIdOrName, StringComparison.Ordinal) ||
            string.Equals(table.Name.Trim(), settings.TableIdOrName.Trim(), StringComparison.OrdinalIgnoreCase));
        var currencies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in table?.Fields ?? [])
        {
            if (field.Type == "currency" &&
                field.Options is { ValueKind: JsonValueKind.Object } options &&
                options.TryGetProperty("symbol", out var symbol) &&
                Money.CodeForSymbol(symbol.GetString()) is { } code)
            {
                currencies[field.Name] = code;
            }
        }

        return currencies;
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

    private sealed record AirtableRecordsCreate(IReadOnlyList<AirtableFieldUpdate> Records, bool Typecast);

    /// <summary>Airtable takes at most ten records per create request, and five requests a second per base.</summary>
    private const int MaxRecordsPerRequest = 10;

    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(210);

    private sealed record AirtablePage(IReadOnlyList<AirtableApiRecord> Records, string? Offset);

    private sealed record AirtableApiRecord(string Id, Dictionary<string, JsonElement> Fields);

    private sealed record AirtableBasesPage(IReadOnlyList<AirtableApiBase> Bases, string? Offset);

    private sealed record AirtableApiBase(string Id, string Name);

    private sealed record AirtableTablesPage(IReadOnlyList<AirtableApiTable> Tables);

    private sealed record AirtableApiTable(string Id, string Name, IReadOnlyList<AirtableApiView> Views, IReadOnlyList<AirtableApiField>? Fields = null);

    private sealed record AirtableApiField(string Id, string Name, string Type, JsonElement? Options = null);

    private sealed record AirtableApiView(string Id, string Name);
}
