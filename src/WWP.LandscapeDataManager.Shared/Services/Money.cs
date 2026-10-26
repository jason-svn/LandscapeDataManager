using System.Text.RegularExpressions;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// An amount of money as a source writes it: "£20.00", "US$1,200", "A$15", "20 EUR", "£20-60" (a
/// range reads as its midpoint). <see cref="CurrencyOf"/> is the currency it names by symbol or ISO
/// code — "$" alone is US dollars — and <see cref="Amount"/> the number.
/// </summary>
public static partial class Money
{
    /// <summary>The currencies LIM converts between (the project currency list in Settings).</summary>
    public static readonly IReadOnlyList<string> Currencies = ["USD", "GBP", "EUR", "CAD", "AUD", "NZD"];

    /// <summary>A Revit Currency-spec parameter's data type id ("autodesk.spec.measurable:currency-2.0.0").</summary>
    public static bool IsCurrencySpec(string? dataTypeId) =>
        dataTypeId is not null && dataTypeId.Contains(":currency", StringComparison.OrdinalIgnoreCase);

    /// <summary>The ISO code the text names, by code or symbol; null when it names none.</summary>
    public static string? CurrencyOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (CodePattern().Match(text) is { Success: true } code)
        {
            return code.Value.ToUpperInvariant();
        }

        var compact = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        return CodeForSymbol(DollarPattern().Match(compact) is { Success: true } dollar ? dollar.Value
            : compact.Contains('£') ? "£"
            : compact.Contains('€') ? "€"
            : null);
    }

    /// <summary>The ISO code of a currency symbol: "£", "€", "$" or "US$" (USD), "A$"/"AU$", "C$"/"CA$", "NZ$"; null for anything else.</summary>
    public static string? CodeForSymbol(string? symbol) => symbol?.Trim().ToUpperInvariant() switch
    {
        "£" => "GBP",
        "€" => "EUR",
        "$" or "US$" or "U$" => "USD",
        "A$" or "AU$" => "AUD",
        "C$" or "CA$" => "CAD",
        "NZ$" => "NZD",
        { } code when Currencies.Contains(code) => code,
        _ => null
    };

    /// <summary>The amount, whatever symbol or code it's written with; null when there's no number.</summary>
    public static double? Amount(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : WwpLdsSheet.ParseNumber(LetterPattern().Replace(text, string.Empty));

    [GeneratedRegex("\\b(USD|GBP|EUR|CAD|AUD|NZD)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex CodePattern();

    [GeneratedRegex("(US|AU|CA|NZ|A|C|U)?\\$", RegexOptions.IgnoreCase)]
    private static partial Regex DollarPattern();

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex LetterPattern();
}

/// <summary>
/// How many units of the project currency one unit of each other currency is worth, at the rates
/// LIM uses everywhere (<see cref="ProjectExchangeRate"/>: the project's fixed rate for its own
/// currency when set, else today's). <see cref="For"/> is null for a currency it has no rate for.
/// </summary>
public sealed class CurrencyFactors(string projectCurrency, IReadOnlyDictionary<string, double> factors)
{
    public string ProjectCurrency { get; } = projectCurrency.ToUpperInvariant();

    public double? For(string currency) =>
        string.Equals(currency, ProjectCurrency, StringComparison.OrdinalIgnoreCase) ? 1d
        : factors.TryGetValue(currency.ToUpperInvariant(), out var factor) ? factor
        : null;

    /// <summary>Factors from every currency in <paramref name="currencies"/> (default: all LIM converts between) into the project's.</summary>
    public static async Task<CurrencyFactors> LoadAsync(RevitPipeClient client, ExchangeRateService rates, string projectCurrency, IEnumerable<string>? currencies = null)
    {
        var factors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var toProject = await ProjectExchangeRate.GetUsdRateAsync(client, rates, projectCurrency);
        foreach (var currency in (currencies ?? Money.Currencies).Select(code => code.ToUpperInvariant()).Distinct())
        {
            if (string.Equals(currency, projectCurrency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var toSource = await ProjectExchangeRate.GetUsdRateAsync(client, rates, currency);
            if (toProject.Success && toSource.Success && toSource.UsdRate > 0)
            {
                factors[currency] = toProject.UsdRate / toSource.UsdRate;
            }
        }

        return new CurrencyFactors(projectCurrency, factors);
    }
}
