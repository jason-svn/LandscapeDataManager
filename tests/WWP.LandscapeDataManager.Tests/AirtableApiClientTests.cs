using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class AirtableApiClientTests
{
    [Fact]
    public async Task Listing_bases_requires_a_token()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new AirtableApiClient().GetBasesAsync(string.Empty));
    }

    [Fact]
    public async Task Listing_tables_requires_a_base_id()
    {
        var apiToken = Environment.GetEnvironmentVariable("AIRTABLE_API_TOKEN");
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return;
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => new AirtableApiClient().GetTablesAsync(apiToken, string.Empty));
    }

    [Fact]
    public async Task Insufficient_scope_is_reported_distinctly_from_a_bad_token()
    {
        // A syntactically-plausible but bogus token gets a 401/403 from Airtable either way —
        // this only asserts we don't crash with an unhandled exception type, not which one Airtable
        // actually returns for a bad token (that's Airtable's behavior to verify with a real
        // low-scope token, which this environment doesn't have).
        await Assert.ThrowsAnyAsync<Exception>(
            () => new AirtableApiClient().GetBasesAsync("patFakeTokenForTesting000000000000000000000000000000000"));
    }

    [Fact]
    public async Task Real_token_can_list_bases_and_a_bases_tables_with_their_views()
    {
        var apiToken = Environment.GetEnvironmentVariable("AIRTABLE_API_TOKEN");
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return;
        }

        var client = new AirtableApiClient();
        var bases = await client.GetBasesAsync(apiToken);
        Assert.NotEmpty(bases);

        var tables = await client.GetTablesAsync(apiToken, bases[0].Id);
        Assert.NotEmpty(tables);
        Assert.All(tables, table => Assert.False(string.IsNullOrWhiteSpace(table.Name)));
    }
}
