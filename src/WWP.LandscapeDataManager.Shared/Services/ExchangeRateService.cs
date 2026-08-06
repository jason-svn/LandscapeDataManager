using System.Text.Json;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>One USD-to-target-currency rate, plus how it was obtained — always usable even on failure.</summary>
public sealed record ExchangeRateResult(string CurrencyCode, double UsdRate, bool Success, string? Error);

/// <summary>
/// Converts i-Tree's USD-denominated monetary benefits into the project's preferred currency.
/// The i-Tree API itself only ever reports dollars (confirmed: it doesn't localize based on tree
/// location), so anything other than USD requires an actual exchange-rate lookup, not a relabel.
/// Uses the free, keyless Frankfurter API (European Central Bank reference rates, updated daily on
/// banking days) — no account or API key needed, consistent with keeping this a low-friction tool.
/// </summary>
public sealed class ExchangeRateService
{
    private const string RateApiUrl = "https://api.frankfurter.dev/v1/latest";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<ExchangeRateResult> GetUsdRateAsync(string currencyCode, CancellationToken cancellationToken = default)
    {
        if (string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return new ExchangeRateResult("USD", 1d, true, null);
        }

        try
        {
            var requestUrl = $"{RateApiUrl}?from=USD&to={Uri.EscapeDataString(currencyCode)}";
            using var response = await HttpClient.GetAsync(requestUrl, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ExchangeRateResult(currencyCode, 1d, false,
                    $"Exchange rate lookup returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Using 1:1 (USD) instead.");
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("rates", out var rates) ||
                !rates.TryGetProperty(currencyCode.ToUpperInvariant(), out var rateElement) ||
                rateElement.ValueKind is not (JsonValueKind.Number))
            {
                return new ExchangeRateResult(currencyCode, 1d, false,
                    $"Exchange rate lookup did not return a rate for {currencyCode}. Using 1:1 (USD) instead.");
            }

            return new ExchangeRateResult(currencyCode, rateElement.GetDouble(), true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ExchangeRateResult(currencyCode, 1d, false,
                $"Exchange rate lookup failed: {exception.Message}. Using 1:1 (USD) instead.");
        }
    }
}
