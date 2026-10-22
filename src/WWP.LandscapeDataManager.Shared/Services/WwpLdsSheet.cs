using System.Globalization;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Reads the WWP landscape data sheet (Airtable) into <see cref="WwpLdsCoefficientRecord"/>s and
/// syncs the local cache, so Floor Calculator and Tree Calculator read it the same way. The sheet
/// writes money as text in pounds ("£15.80", sometimes a range like "£20-60"), and its first
/// column arrives with a byte-order mark; both are handled here.
/// </summary>
public static class WwpLdsSheet
{
    /// <summary>The sheet's money columns are in pounds sterling.</summary>
    public const string SheetCurrency = "GBP";

    public static WwpLdsCoefficientRecord ToCoefficientRecord(AirtableRecord record)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in record.Fields)
        {
            fields.TryAdd(DefaultParameterMappingCatalog.CleanHeader(key), value);
        }

        string Text(string key) => fields.TryGetValue(key, out var element) ? ReadText(element) ?? string.Empty : string.Empty;
        double? Number(string key) => fields.TryGetValue(key, out var element) ? ReadNumber(element) : null;

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
            Number("COST_SAVED"),
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
            NullIfBlank(Text("Allergenic")));
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
        var records = await airtable.GetRecordsAsync(new AirtableApiSettings(source.BaseId, source.TableIdOrName, source.ViewName), token);
        var rows = records.Select(ToCoefficientRecord).ToList();
        await cache.SaveAsync(rows);
        return rows;
    }

    /// <summary>
    /// How many units of the project currency one sheet pound is worth: 1 for a GBP project, else
    /// (USD→project) ÷ (USD→GBP). Null when a rate isn't available.
    /// </summary>
    public static async Task<double?> CurrencyFactorAsync(RevitPipeClient client, ExchangeRateService rates, string projectCurrency)
    {
        if (string.Equals(projectCurrency, SheetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        var toProject = await ProjectExchangeRate.GetUsdRateAsync(client, rates, projectCurrency);
        var toPounds = await ProjectExchangeRate.GetUsdRateAsync(client, rates, SheetCurrency);
        return toProject.Success && toPounds.Success && toPounds.UsdRate > 0 ? toProject.UsdRate / toPounds.UsdRate : null;
    }
}
