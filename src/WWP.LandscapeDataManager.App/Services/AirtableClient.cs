using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WWP.LandscapeDataManager.App.Services;

internal sealed class AirtableClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<IReadOnlyList<AirtableRecord>> GetRecordsAsync(
        string baseId,
        string tableName,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseId))
        {
            throw new ArgumentException("Enter the Airtable base ID.", nameof(baseId));
        }

        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Enter the Airtable table name.", nameof(tableName));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Set the WWP_AIRTABLE_TOKEN user environment variable before starting Revit.");
        }

        var records = new List<AirtableRecord>();
        string? offset = null;

        do
        {
            var url = $"https://api.airtable.com/v0/{Uri.EscapeDataString(baseId)}/{Uri.EscapeDataString(tableName)}";
            if (!string.IsNullOrWhiteSpace(offset))
            {
                url += $"?offset={Uri.EscapeDataString(offset)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Airtable returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var page = JsonSerializer.Deserialize<AirtablePage>(json, JsonOptions)
                       ?? throw new InvalidDataException("Airtable returned an empty response.");
            records.AddRange(page.Records);
            offset = page.Offset;
        }
        while (!string.IsNullOrWhiteSpace(offset));

        return records;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record AirtablePage(
        [property: JsonPropertyName("records")] List<AirtableRecord> Records,
        [property: JsonPropertyName("offset")] string? Offset);
}

internal sealed record AirtableRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fields")] Dictionary<string, JsonElement> Fields);
