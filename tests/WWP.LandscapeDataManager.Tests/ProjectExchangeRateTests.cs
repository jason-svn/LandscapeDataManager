using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public class ProjectExchangeRateTests
{
    [Fact]
    public void A_fixed_rate_for_the_project_currency_is_used_instead_of_a_lookup()
    {
        var rate = ProjectExchangeRate.ResolveWithoutLookup(new ExchangeRateOverride(true, "GBP", 0.8, "2026 budget rate"), "gbp");

        Assert.NotNull(rate);
        Assert.Equal(0.8, rate.UsdRate);
        Assert.True(rate.IsFixed);
        Assert.Equal("GBP", rate.CurrencyCode);
    }

    [Theory]
    [InlineData(false, "GBP", 0.8)] // switched off
    [InlineData(true, "EUR", 0.9)]  // saved for another currency
    [InlineData(true, "GBP", 0)]    // not a usable rate
    [InlineData(true, "GBP", -1)]
    public void Falls_back_to_the_live_rate_when_the_fixed_rate_does_not_apply(bool enabled, string currency, double usdRate) =>
        Assert.Null(ProjectExchangeRate.ResolveWithoutLookup(new ExchangeRateOverride(enabled, currency, usdRate), "GBP"));

    [Fact]
    public void Without_a_fixed_rate_non_USD_needs_a_lookup() =>
        Assert.Null(ProjectExchangeRate.ResolveWithoutLookup(null, "GBP"));

    [Fact]
    public void USD_is_always_one_to_one()
    {
        var rate = ProjectExchangeRate.ResolveWithoutLookup(new ExchangeRateOverride(true, "USD", 2), "USD");

        Assert.Equal(1d, rate!.UsdRate);
        Assert.False(rate.IsFixed);
    }

    [Fact]
    public void Describe_says_whether_the_rate_is_fixed()
    {
        Assert.Equal("0.8 (fixed: 2026 budget rate)", ProjectExchangeRate.Describe(new ExchangeRateResult("GBP", 0.8, true, null, IsFixed: true), "2026 budget rate"));
        Assert.Equal("0.7912 (today's rate)", ProjectExchangeRate.Describe(new ExchangeRateResult("GBP", 0.7912, true, null)));
    }

    [Fact]
    public void A_fixed_rate_travels_in_a_settings_file()
    {
        var snapshot = new ProjectSettingsSnapshot(PreferredCurrency: "GBP", ExchangeRateOverride: new ExchangeRateOverride(true, "GBP", 0.8, "2026 budget rate"));
        var file = LimSettingsFileFormat.Build(snapshot, "Metric", "GBP", "Site A", "1.2.4", null, DateTimeOffset.Now);

        var parsed = LimSettingsFileFormat.Parse(LimSettingsFileFormat.Serialize(file));

        Assert.Equal(new ExchangeRateOverride(true, "GBP", 0.8, "2026 budget rate"), parsed.Settings.ExchangeRateOverride);
    }
}
