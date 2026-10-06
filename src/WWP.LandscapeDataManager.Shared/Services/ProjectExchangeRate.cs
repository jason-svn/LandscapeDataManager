namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// A fixed USD exchange rate the project uses instead of the day's market rate — e.g. a budget
/// or appraisal rate a report has to be costed at. Saved on the project with the other settings
/// (<see cref="ProjectSettingsSnapshot.ExchangeRateOverride"/>). <see cref="Enabled"/> rather
/// than null switches it off, because a null field in a settings push means "keep what's there".
/// </summary>
public sealed record ExchangeRateOverride(bool Enabled, string CurrencyCode, double UsdRate, string? Note = null)
{
    /// <summary>The fixed rate when it is on, valid, and for <paramref name="currencyCode"/>; else null.</summary>
    public double? RateFor(string currencyCode) =>
        Enabled && UsdRate > 0 && double.IsFinite(UsdRate) &&
        string.Equals(CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase)
            ? UsdRate
            : null;
}

/// <summary>
/// The one place LIM tools get the USD rate for the project's currency: the project's fixed rate
/// when one is set for that currency, otherwise the live rate from <see cref="ExchangeRateService"/>.
/// </summary>
public static class ProjectExchangeRate
{
    /// <summary>The rate that needs no lookup — USD itself, or the project's fixed rate for this currency — else null (use the live rate).</summary>
    public static ExchangeRateResult? ResolveWithoutLookup(ExchangeRateOverride? fixedRate, string currencyCode)
    {
        if (string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return new ExchangeRateResult("USD", 1d, true, null);
        }

        return fixedRate?.RateFor(currencyCode) is { } rate
            ? new ExchangeRateResult(currencyCode.ToUpperInvariant(), rate, true, null, IsFixed: true)
            : null;
    }

    /// <summary>Reads the project's fixed rate over the pipe; falls back to the live rate if it can't (e.g. a cached snapshot with Revit closed).</summary>
    public static async Task<ExchangeRateResult> GetUsdRateAsync(RevitPipeClient client, ExchangeRateService liveService, string currencyCode)
    {
        ExchangeRateOverride? fixedRate = null;
        try
        {
            fixedRate = (await ProjectSettingsSync.PullAsync(client))?.ExchangeRateOverride;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
        }

        return ResolveWithoutLookup(fixedRate, currencyCode) ?? await liveService.GetUsdRateAsync(currencyCode);
    }

    /// <summary>"0.8 (fixed: 2026 budget rate)" / "0.7912 (today's rate)" — for status lines.</summary>
    public static string Describe(ExchangeRateResult rate, string? note = null) =>
        rate.IsFixed
            ? $"{rate.UsdRate:G6} (fixed{(string.IsNullOrWhiteSpace(note) ? string.Empty : ": " + note)})"
            : $"{rate.UsdRate:G6} (today's rate)";
}
