namespace WWP.LandscapeDataManager.App.LocationFinder;

/// <summary>
/// Maps an ISO 3166-1 alpha-2 country code (as returned by Nominatim's reverse geocoder) to one of
/// the currency codes the LIM tools support (the same closed set as
/// <c>RevitModelScanner.SupportedCurrencyCodes</c>: USD, GBP, EUR, CAD, AUD, NZD). Countries outside
/// that set (or an unrecognized/missing code) resolve to null — the caller leaves whatever currency
/// preference is already set rather than guessing.
/// </summary>
internal static class CurrencyByCountryCode
{
    private static readonly IReadOnlyDictionary<string, string> Map = BuildMap();

    public static string? Resolve(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode) && Map.TryGetValue(countryCode.Trim(), out var currency)
            ? currency
            : null;

    private static IReadOnlyDictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gb"] = "GBP",
            ["us"] = "USD",
            ["ca"] = "CAD",
            ["au"] = "AUD",
            ["nz"] = "NZD"
        };

        // Eurozone member states all share EUR.
        string[] eurozoneCountryCodes =
        [
            "at", "be", "cy", "de", "ee", "es", "fi", "fr", "gr", "hr",
            "ie", "it", "lt", "lu", "lv", "mt", "nl", "pt", "si", "sk"
        ];
        foreach (var code in eurozoneCountryCodes)
        {
            map[code] = "EUR";
        }

        return map;
    }
}
