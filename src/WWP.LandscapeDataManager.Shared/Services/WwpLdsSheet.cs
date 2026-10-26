using System.Globalization;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Reads the WWP landscape data sheet (Airtable) into <see cref="WwpLdsCoefficientRecord"/>s and
/// syncs the local cache, so Floor Calculator and Tree Calculator read it the same way. The sheet
/// writes money as text, in pounds unless a cell or column names another currency ("£15.80", sometimes a range like "£20-60"), and its first
/// column arrives with a byte-order mark; both are handled here.
/// </summary>
public static class WwpLdsSheet
{
    /// <summary>The currency of the sheet's money when a cell or column doesn't name one: pounds sterling.</summary>
    public const string SheetCurrency = "GBP";

    /// <summary>
    /// One sheet row. Its cost saved is in the currency the cell names ("£", "US$", "EUR"…), else
    /// the column's (<paramref name="fieldCurrencies"/>: an Airtable currency field's symbol), else
    /// null — read as the sheet's own <see cref="SheetCurrency"/>.
    /// </summary>
    public static WwpLdsCoefficientRecord ToCoefficientRecord(AirtableRecord record, IReadOnlyDictionary<string, string>? fieldCurrencies = null)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in record.Fields)
        {
            fields.TryAdd(DefaultParameterMappingCatalog.CleanHeader(key), value);
        }

        string Text(string key) => fields.TryGetValue(key, out var element) ? ReadText(element) ?? string.Empty : string.Empty;
        double? Number(string key) => fields.TryGetValue(key, out var element) ? ReadNumber(element) : null;
        // Money text may carry a code or a prefixed dollar ("US$10", "A$15", "20 EUR").
        double? Amount(string key) => fields.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
            ? Money.Amount(element.GetString())
            : Number(key);

        var category = Text("WWP_LDS_Category");
        var subCategory = Text("WWP_LDS_SubCategory");
        var typeName = Text("Types");
        return new WwpLdsCoefficientRecord(
            $"{category}|{subCategory}|{typeName}",
            Text("Origin"),
            Text("Planting type"),
            category,
            subCategory,
            typeName,
            Amount("COST_SAVED"),
            Number("Oxygen levels O2 kg/yr (16/18 girth)"),
            EmbodiedCarbon.Rate(Number("Product GWP"), Number("Transport GWP"), Number("WWP_Total_GWP")),
            Number("Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)"),
            Number("Avoided runoff m3/yr (16/18 girth)"),
            Number("WWP_Pollutants_Removed"),
            Number("Surface_Temperature_Reduction_Min"),
            Number("Air temperature reduction (1.5m height)"),
            NullIfBlank(Text("Species Code")),
            Number("Pollen"),
            Number("Irrigation_Demand_Plant Factor"),
            NullIfBlank(Text("Allergenic")),
            Money.CurrencyOf(Text("COST_SAVED")) ?? fieldCurrencies?.FirstOrDefault(pair => DefaultParameterMappingCatalog.CleanHeader(pair.Key) == "COST_SAVED").Value);
    }

    /// <summary>
    /// A sheet number: a JSON number, or text such as "£15.80", "1,234.5" or "£20-60" (a range
    /// reads as its midpoint). Null when the cell is empty or isn't a number.
    /// </summary>
    public static double? ReadNumber(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.GetDouble();
            case JsonValueKind.String:
                return ParseNumber(element.GetString());
            case JsonValueKind.Array:
                // A lookup/rollup cell arrives as a one-item array.
                return element.GetArrayLength() == 1 ? ReadNumber(element[0]) : null;
            default:
                return null;
        }
    }

    public static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new string([.. text.Where(character => !(char.IsWhiteSpace(character) || character is '£' or '$' or '€' or ','))]);
        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        // "20-60" (a range) — but not a leading minus sign.
        var dash = cleaned.IndexOf('-', 1);
        return dash > 0 &&
               double.TryParse(cleaned[..dash], NumberStyles.Float, CultureInfo.InvariantCulture, out var low) &&
               double.TryParse(cleaned[(dash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var high)
            ? (low + high) / 2d
            : null;
    }

    private static string? ReadText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Array when element.GetArrayLength() == 1 => ReadText(element[0]),
        _ => null
    };

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// True when the cache is empty, or was synced before LIM read the sheet's "£" money and its tree
    /// columns (no row has a cost, species code or pollen) — sync again before relying on it.
    /// </summary>
    public static bool NeedsResync(IReadOnlyList<WwpLdsCoefficientRecord> rows) =>
        rows.Count == 0 || rows.All(row => row.CostSavedAnnual is null && row.SpeciesCode is null && row.PollenRate is null);

    /// <summary>The cached sheet, synced first when it's missing or out of date (see <see cref="NeedsResync"/>).</summary>
    public static async Task<IReadOnlyList<WwpLdsCoefficientRecord>> LoadAsync(
        WwpLdsCoefficientDatabase cache, Func<Task<WwpLdsAirtableSettings>> source, string token, AirtableApiClient airtable)
    {
        var rows = await cache.GetAllAsync();
        return NeedsResync(rows) && !string.IsNullOrWhiteSpace(token)
            ? await SyncAsync(await source(), token, airtable, cache)
            : rows;
    }

    /// <summary>Downloads the sheet and replaces the local cache; returns the rows cached.</summary>
    public static async Task<IReadOnlyList<WwpLdsCoefficientRecord>> SyncAsync(
        WwpLdsAirtableSettings source, string token, AirtableApiClient airtable, WwpLdsCoefficientDatabase cache)
    {
        var settings = new AirtableApiSettings(source.BaseId, source.TableIdOrName, source.ViewName);
        var records = await airtable.GetRecordsAsync(settings, token);
        IReadOnlyDictionary<string, string> fieldCurrencies;
        try
        {
            fieldCurrencies = await airtable.GetFieldCurrenciesAsync(settings, token);
        }
        catch (Exception exception) when (exception is AirtableInsufficientScopeException or HttpRequestException or InvalidOperationException)
        {
            // Without the schema scope, a bare-number money column reads as pounds, as it always has.
            fieldCurrencies = new Dictionary<string, string>();
        }

        var rows = records.Select(record => ToCoefficientRecord(record, fieldCurrencies)).ToList();
        await cache.SaveAsync(rows);
        return rows;
    }

    /// <summary>
    /// The rates to put the sheet's money into the project currency: from each row's own currency
    /// (<see cref="CostSavedCurrency"/>), and the first of those with no rate available (null when
    /// every row converts).
    /// </summary>
    public static async Task<(CurrencyFactors Factors, string? Missing)> CurrencyFactorsAsync(
        RevitPipeClient client, ExchangeRateService rates, string projectCurrency, IEnumerable<WwpLdsCoefficientRecord> rows)
    {
        var currencies = rows.Select(CostSavedCurrency).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var factors = await CurrencyFactors.LoadAsync(client, rates, projectCurrency, currencies);
        return (factors, currencies.FirstOrDefault(currency => factors.For(currency) is null));
    }

    /// <summary>The currency a row's cost saved is in: the one its cell or column names, else the sheet's pounds.</summary>
    public static string CostSavedCurrency(WwpLdsCoefficientRecord row) => row.CostSavedCurrency ?? SheetCurrency;

    /// <summary>A row's cost saved in the project currency; null without a cost or a rate.</summary>
    public static double? CostSavedIn(CurrencyFactors factors, WwpLdsCoefficientRecord row) =>
        row.CostSavedAnnual is { } cost && factors.For(CostSavedCurrency(row)) is { } factor ? cost * factor : null;
}
