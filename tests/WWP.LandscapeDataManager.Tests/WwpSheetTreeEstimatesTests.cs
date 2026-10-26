using System.Globalization;
using System.Text.Json;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Shared.Models;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

/// <summary>The WWP landscape data sheet fills what i-Tree doesn't: uncalculated trees, and pollen.</summary>
public sealed class WwpSheetTreeEstimatesTests
{
    private static WwpLdsCoefficientRecord Row(string type, string category = "Vegetation", string? code = null, double? co2 = 28, double? cost = 15.8, double? pollen = 0.2) =>
        new($"{category}|Tree|{type}", "UK/Europe", "Tree", category, "Tree", type, cost, co2 * 32 / 44, 0, co2, 1.28, 0.2, null, null, code, pollen, 0.3, null);

    private static readonly IReadOnlyList<WwpLdsCoefficientRecord> Sheet =
    [
        Row("Acer_Campestre", code: "ACCA"),
        Row("Quercus_Robur", code: "QURO", co2: 51, cost: 15),
        Row("Generic", co2: 27, cost: 10),
        Row("Meadow", category: "Planting")
    ];

    private static PlantingInstanceValidationItem Tree(string family, string type, string? code, string storedStatus = "") =>
        new(Guid.NewGuid().ToString(), 1, family, type, code, null, null, null, null, null, null, storedStatus, string.Empty, string.Empty, string.Empty, string.Empty);

    [Theory]
    [InlineData("£15.80", 15.8)]
    [InlineData("£20-60", 40)]          // a range reads as its midpoint
    [InlineData("1,234.5", 1234.5)]
    [InlineData("-28", -28)]
    [InlineData(" £ 2.00 ", 2)]
    public void Sheet_money_and_numbers_parse(string text, double expected) =>
        Assert.Equal(expected, WwpLdsSheet.ParseNumber(text)!.Value, 9);

    [Theory]
    [InlineData("")]
    [InlineData("n/a")]
    public void Text_that_isnt_a_number_is_null(string text) => Assert.Null(WwpLdsSheet.ParseNumber(text));

    [Fact]
    public void A_sheet_row_reads_money_the_tree_columns_and_its_bom_prefixed_origin()
    {
        var fields = new Dictionary<string, JsonElement>
        {
            ["﻿Origin"] = Json("\"UK/Europe\""),
            ["Types"] = Json("\"Quercus_Robur\""),
            ["WWP_LDS_Category"] = Json("\"Vegetation\""),
            ["WWP_LDS_SubCategory"] = Json("\"Tree\""),
            ["COST_SAVED"] = Json("\"£15.00\""),
            ["Species Code"] = Json("\"QURO\""),
            ["Pollen"] = Json("0.2"),
            ["Irrigation_Demand_Plant Factor"] = Json("0.5"),
            ["Allergenic"] = Json("0.2"),
            ["Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)"] = Json("51")
        };

        var row = WwpLdsSheet.ToCoefficientRecord(new AirtableRecord("rec1", fields));

        Assert.Equal("UK/Europe", row.Origin);
        Assert.Equal(15d, row.CostSavedAnnual);
        Assert.Equal("QURO", row.SpeciesCode);
        Assert.Equal(0.2, row.PollenRate);
        Assert.Equal(0.5, row.IrrigationDemandFactor);
        Assert.Equal("0.2", row.Allergenic);
        Assert.Equal("Vegetation|Tree|Quercus_Robur", row.MatchKey);
    }

    [Fact]
    public void A_cache_from_before_money_parsing_needs_a_resync()
    {
        Assert.True(WwpLdsSheet.NeedsResync([]));
        Assert.True(WwpLdsSheet.NeedsResync([Row("Old", code: null, cost: null, pollen: null)]));
        Assert.False(WwpLdsSheet.NeedsResync(Sheet));
    }

    [Theory]
    [InlineData("WWP_Vegetation_Tree", "Anything", "QURO", "Quercus_Robur", SheetRowMatch.SpeciesCode)]
    [InlineData("WWP_Vegetation_Tree", "Acer_Campestre", null, "Acer_Campestre", SheetRowMatch.TypeName)]
    [InlineData("Quercus Robur", "Large", null, "Quercus_Robur", SheetRowMatch.FamilyName)]
    [InlineData("WWP_Vegetation_Tree", "Mystery", "ZZZZ", "Generic", SheetRowMatch.Generic)]
    public void A_tree_finds_its_sheet_row(string family, string type, string? code, string expectedRow, SheetRowMatch expectedHow)
    {
        var (row, how) = WwpSheetTreeEstimates.FindRow(code, family, type, Sheet);

        Assert.Equal(expectedRow, row?.TypeName);
        Assert.Equal(expectedHow, how);
    }

    [Theory]
    [InlineData("WWP_Vegetation_Tree", "Acer_Campestre", "ACCA")]   // the type's own name
    [InlineData("Quercus Robur", "Large", "QURO")]                  // else its family's
    [InlineData("WWP_Vegetation_Tree", "Mystery", null)]            // no Generic fallback by name
    public void Tree_searcher_finds_a_types_species_code_on_the_sheet_by_name(string family, string type, string? expectedCode) =>
        Assert.Equal(expectedCode, WwpSheetTreeEstimates.FindRowByName(family, type, Sheet).Row?.SpeciesCode);

    [Fact]
    public void A_sheet_row_without_a_code_finds_its_species_by_scientific_name()
    {
        var index = WwpSheetTreeEstimates.ScientificNameIndex(
        [
            new SpeciesCatalogueRecord("FREX", "European ash", "Fraxinus excelsior", "Species", null),
            new SpeciesCatalogueRecord("ACCA", "Hedge maple", "Acer campestre", "Species", null)
        ]);

        Assert.Equal("FREX", WwpSheetTreeEstimates.SpeciesCodeFor(Row("Fraxinus_Excelsior"), index));
        Assert.Equal("ACCA", WwpSheetTreeEstimates.SpeciesCodeFor(Row("Hedge_Maple_Variety", code: "ACCA"), index)); // its own code first
        Assert.Null(WwpSheetTreeEstimates.SpeciesCodeFor(Row("Amelanchier_Robin_Hill"), index));
    }

    [Fact]
    public void Planting_area_rows_are_never_a_trees_row()
    {
        var (row, _) = WwpSheetTreeEstimates.FindRow(null, "WWP_Vegetation_Meadow", "Meadow", [Row("Meadow", category: "Planting")]);

        Assert.Null(row);
    }

    [Fact]
    public void Only_trees_without_i_tree_results_are_estimated()
    {
        Assert.False(WwpSheetTreeEstimates.NeedsEstimate(Tree("F", "T", "ACCA", "Calculated")));
        Assert.True(WwpSheetTreeEstimates.NeedsEstimate(Tree("F", "T", "ACCA", "APIError")));
        Assert.True(WwpSheetTreeEstimates.NeedsEstimate(Tree("F", "T", "ACCA")));
        Assert.True(WwpSheetTreeEstimates.NeedsEstimate(Tree("F", "T", "ACCA", WwpSheetTreeEstimates.EstimatedStatus)));
    }

    [Fact]
    public void An_estimate_stores_the_sheets_per_tree_figures_and_no_signature()
    {
        var writes = WwpSheetTreeEstimates.BuildEstimateWrites("uid", Sheet[0], SheetRowMatch.SpeciesCode, "Missing: TreeTrunk_DBH", "Metric", "USD", new CurrencyFactors("USD", new Dictionary<string, double> { ["GBP"] = 1.25 }), 0.8)
            .ToDictionary(write => write.RevitParameter, write => write.SourceValue);
        double Value(string parameter) => double.Parse(writes[parameter], CultureInfo.InvariantCulture);

        Assert.Equal(WwpSheetTreeEstimates.EstimatedStatus, writes["!_S_PLT_iTreeResult_Status_Text"]);
        Assert.Equal(string.Empty, writes["!_S_PLT_iTreeResult_InputSignature_Text"]);
        Assert.Contains("Missing: TreeTrunk_DBH", writes["!_S_PLT_iTreeResult_Details_Text"]);
        Assert.Equal(28, Value("!_S_PLT_iTreeResult_CO2EquivalentAnnual_Mass"), 9);
        Assert.Equal(28 / UnitConversions.CarbonToCo2MassRatio, Value("!_S_PLT_iTreeResult_CarbonSequesteredAnnual_Mass"), 9);
        Assert.Equal(1.28, Value("!_S_PLT_iTreeResult_RunoffAvoidedAnnual_Volume"), 9);
        Assert.Equal(0.2, Value("!_S_PLT_Result_PollutantsRemovedAnnual_Mass"), 9);
        Assert.Equal(15.8 * 1.25, Value("!_S_PLT_iTreeResult_CostSavedAnnual_Currency"), 9); // pounds → project currency
        Assert.Equal(0, Value("!_S_PLT_iTreeResult_PM25RemovedAnnual_Mass"));                 // i-Tree-only: cleared
    }

    [Fact]
    public void The_plan_adds_pollen_to_every_matched_tree_and_estimates_only_the_uncalculated()
    {
        var trees = new[]
        {
            Tree("WWP_Vegetation_Tree", "Acer_Campestre", "ACCA", "Calculated"),
            Tree("WWP_Vegetation_Tree", "Quercus_Robur", "QURO", "APIError"),
            Tree("Shrub", "Box", null)
        };

        var (writes, summary) = WwpSheetTreeEstimates.Plan(trees, [Sheet[0], Sheet[1]], "Metric", "GBP", new CurrencyFactors("GBP", new Dictionary<string, double>()), 1);

        Assert.Equal(1, summary.Estimated);
        Assert.Equal(2, summary.PollenWritten);
        Assert.Equal(["Shrub : Box"], summary.UnmatchedTypes);
        Assert.DoesNotContain(writes, write => write.UniqueId == trees[0].UniqueId && write.RevitParameter == "!_S_PLT_iTreeResult_Status_Text");
        Assert.Contains(writes, write => write.UniqueId == trees[0].UniqueId && write.RevitParameter == WwpSheetTreeEstimates.PollenParameter);
    }

    [Fact]
    public void Estimated_trees_count_toward_dashboard_totals_with_their_pollutant_total()
    {
        var estimated = DashboardAggregationTestTrees.Item(1, WwpSheetTreeEstimates.EstimatedStatus) with { PollutantsRemovedAnnual = 0.2, CO2EquivalentAnnual = 28 };
        var calculated = DashboardAggregationTestTrees.Item(1) with { PM25RemovedAnnual = 0.1, PollutantsRemovedAnnual = 0.1 };
        var trees = new[] { estimated, calculated }.Select(tree => DashboardAggregationService.NormalizeTree(tree, "Metric", "USD", 1)).ToList();

        var total = DashboardAggregationService.BuildGrandTotal(trees, []);

        Assert.True(trees[0].CountsTowardTotals && trees[0].IsSheetEstimate);
        Assert.Equal(2, total.TreeCount);
        Assert.Equal(0.3, total.TotalPollutionMassRemovedAnnual, 9); // the estimate's 0.2 + i-Tree's PM2.5, not its total twice
        Assert.Equal(28, total.CO2EquivalentAnnual, 9);
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("0.2", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void Allergenic_reads_yes_or_a_positive_score(string? value, bool expected) =>
        Assert.Equal(expected, DashboardAggregationService.IsAllergenic(value));

    [Fact]
    public void Site_kpis_total_pollen_and_average_irrigation_demand()
    {
        var trees = new[]
        {
            DashboardAggregationTestTrees.Item(1) with { PollenAnnual = 0.2, IrrigationDemandFactor = 0.3, Allergenic = "0.2" },
            DashboardAggregationTestTrees.Item(1) with { PollenAnnual = 0.1, IrrigationDemandFactor = 0.7 }
        }.Select(tree => DashboardAggregationService.NormalizeTree(tree, "Metric", "USD", 1)).ToList();

        var summary = DashboardAggregationService.BuildSiteKpiSummary(trees, [], [], null, null);

        Assert.Equal(0.3, summary.TreePollenAnnual, 9);
        Assert.Equal(1, summary.AllergenicTreeCount);
        Assert.Equal(0.5, summary.MeanIrrigationDemandFactor!.Value, 9);
        Assert.Equal(1, summary.LowWaterTreeCount);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
