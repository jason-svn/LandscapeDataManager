using System.Globalization;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>A source value ready for a Currency parameter: <see cref="Value"/> to write, <see cref="Message"/> on the conversion, or why it couldn't be converted.</summary>
public sealed record ImportMoneyResult(bool Success, string Value, string? Message);

/// <summary>
/// Puts money from the planting source into the project's currency before the Importer writes it
/// to a Currency parameter (e.g. the WWP sheet's "£20.00" maintenance cost in a US-dollar project).
/// The source currency is the one the cell names ("£", "US$", "EUR", …), else the column's
/// (an Airtable currency field's symbol); with neither, the amount is taken as already in the
/// project currency, as before. Any other parameter's value passes through unchanged.
/// </summary>
public static class ImportMoney
{
    public static ImportMoneyResult Convert(
        string sourceValue,
        string sourceField,
        RevitParameterDescriptor target,
        IReadOnlyDictionary<string, string> fieldCurrencies,
        CurrencyFactors? factors)
    {
        if (!Money.IsCurrencySpec(target.DataTypeId) || Money.Amount(sourceValue) is not { } amount)
        {
            return new ImportMoneyResult(true, sourceValue, null);
        }

        var currency = Money.CurrencyOf(sourceValue) ?? fieldCurrencies.GetValueOrDefault(sourceField);
        if (currency is null || (factors is not null && string.Equals(currency, factors.ProjectCurrency, StringComparison.OrdinalIgnoreCase)))
        {
            return new ImportMoneyResult(true, Format(amount), null);
        }

        if (factors?.For(currency) is not { } factor)
        {
            return new ImportMoneyResult(false, sourceValue,
                $"'{sourceValue}' is in {currency}, and there's no {currency} → {factors?.ProjectCurrency ?? "project currency"} exchange rate to convert it with " +
                "(check the internet connection, or set a fixed rate in Settings > Units & Currency).");
        }

        var converted = Math.Round(amount * factor, 2);
        return new ImportMoneyResult(true, Format(converted),
            $"{sourceValue.Trim()} → {Format(converted)} {factors.ProjectCurrency} (1 {currency} = {factor.ToString("0.####", CultureInfo.InvariantCulture)} {factors.ProjectCurrency}).");
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
