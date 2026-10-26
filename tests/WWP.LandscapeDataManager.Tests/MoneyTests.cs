using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>Money from the planting source put into the project currency.</summary>
public sealed class MoneyTests
{
    private static readonly RevitParameterDescriptor CurrencyTarget =
        new("!_S_PLT_iTreeSpecies_MaintenanceCostAnnual_Currency", "Type", "Double", "autodesk.spec.measurable:currency-2.0.0", true, []);

    private static readonly RevitParameterDescriptor TextTarget =
        new("!_S_PLT_iTreeSpecies_Notes_Text", "Type", "String", "autodesk.spec:spec.string-2.0.0", true, []);

    // A US-dollar project where £1 = $1.25 and €1 = $1.10.
    private static readonly CurrencyFactors ToUsd = new("USD", new Dictionary<string, double> { ["GBP"] = 1.25, ["EUR"] = 1.1 });

    private static readonly Dictionary<string, string> NoFieldCurrencies = [];

    [Theory]
    [InlineData("£20.00", "GBP")]
    [InlineData("US$1,200", "USD")]
    [InlineData("$5", "USD")]
    [InlineData("A$15", "AUD")]
    [InlineData("NZ$ 3", "NZD")]
    [InlineData("20 EUR", "EUR")]
    [InlineData("€8", "EUR")]
    [InlineData("20", null)]
    [InlineData("", null)]
    public void CurrencyOf_reads_the_symbol_or_code(string text, string? expected) =>
        Assert.Equal(expected, Money.CurrencyOf(text));

    [Theory]
    [InlineData("£20-60", 40d)]
    [InlineData("US$1,200", 1200d)]
    [InlineData("20 EUR", 20d)]
    public void Amount_ignores_the_currency(string text, double expected) =>
        Assert.Equal(expected, Money.Amount(text));

    [Fact]
    public void Factors_are_one_for_the_project_currency_and_null_without_a_rate()
    {
        Assert.Equal(1d, ToUsd.For("usd"));
        Assert.Equal(1.25, ToUsd.For("GBP"));
        Assert.Null(ToUsd.For("AUD"));
    }

    [Fact]
    public void A_cell_naming_its_currency_is_converted()
    {
        var result = ImportMoney.Convert("£20.00", "Maintenance", CurrencyTarget, NoFieldCurrencies, ToUsd);

        Assert.True(result.Success);
        Assert.Equal("25", result.Value);
        Assert.Contains("£20.00 → 25 USD", result.Message);
    }

    [Fact]
    public void A_bare_number_takes_its_columns_currency()
    {
        var result = ImportMoney.Convert("20", "Maintenance", CurrencyTarget, new Dictionary<string, string> { ["Maintenance"] = "EUR" }, ToUsd);

        Assert.True(result.Success);
        Assert.Equal("22", result.Value);
    }

    [Fact]
    public void Money_naming_no_currency_or_the_projects_imports_as_it_stands()
    {
        var bare = ImportMoney.Convert("20", "Maintenance", CurrencyTarget, NoFieldCurrencies, ToUsd);
        var dollars = ImportMoney.Convert("$20", "Maintenance", CurrencyTarget, NoFieldCurrencies, ToUsd);

        Assert.Equal(("20", (string?)null), (bare.Value, bare.Message));
        Assert.Equal(("20", (string?)null), (dollars.Value, dollars.Message));
    }

    [Fact]
    public void Money_without_a_rate_is_refused_rather_than_written_unconverted()
    {
        var result = ImportMoney.Convert("A$15", "Maintenance", CurrencyTarget, NoFieldCurrencies, ToUsd);

        Assert.False(result.Success);
        Assert.Contains("AUD", result.Message);
    }

    [Fact]
    public void Other_parameters_pass_through()
    {
        var result = ImportMoney.Convert("£20 per tree", "Notes", TextTarget, NoFieldCurrencies, ToUsd);

        Assert.Equal(new ImportMoneyResult(true, "£20 per tree", null), result);
    }

    [Theory]
    [InlineData("US$10", null, "USD")]
    [InlineData("£10", "EUR", "GBP")]
    [InlineData(null, "EUR", "EUR")]
    [InlineData(null, null, null)]
    public void A_sheet_rows_cost_saved_is_in_its_cells_currency_else_its_columns(string? cell, string? column, string? expected)
    {
        var fields = new Dictionary<string, JsonElement>
        {
            ["COST_SAVED"] = cell is null ? JsonSerializer.SerializeToElement(10) : JsonSerializer.SerializeToElement(cell)
        };
        var columns = column is null ? null : new Dictionary<string, string> { ["COST_SAVED"] = column };

        var row = WwpLdsSheet.ToCoefficientRecord(new AirtableRecord("rec1", fields), columns);

        Assert.Equal(10d, row.CostSavedAnnual);
        Assert.Equal(expected, row.CostSavedCurrency);
        Assert.Equal(expected ?? "GBP", WwpLdsSheet.CostSavedCurrency(row));
    }

    [Fact]
    public void A_sheet_rows_cost_saved_converts_from_its_own_currency()
    {
        var pounds = WwpLdsSheet.ToCoefficientRecord(new AirtableRecord("rec1", new Dictionary<string, JsonElement> { ["COST_SAVED"] = JsonSerializer.SerializeToElement("£10") }));
        var euros = WwpLdsSheet.ToCoefficientRecord(new AirtableRecord("rec2", new Dictionary<string, JsonElement> { ["COST_SAVED"] = JsonSerializer.SerializeToElement("€10") }));
        var dollars = WwpLdsSheet.ToCoefficientRecord(new AirtableRecord("rec3", new Dictionary<string, JsonElement> { ["COST_SAVED"] = JsonSerializer.SerializeToElement("A$10") }));

        Assert.Equal(12.5, WwpLdsSheet.CostSavedIn(ToUsd, pounds));
        Assert.Equal(11d, WwpLdsSheet.CostSavedIn(ToUsd, euros)!.Value, 6);
        Assert.Null(WwpLdsSheet.CostSavedIn(ToUsd, dollars));
    }
}
