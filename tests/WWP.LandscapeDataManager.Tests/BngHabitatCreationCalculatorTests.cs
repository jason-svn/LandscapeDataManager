using System.Globalization;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class BngHabitatCreationCalculatorTests
{
    private static readonly BngMetricCatalog Catalog = BngMetricCatalog.Default;

    [Fact]
    public void Catalog_loads_every_habitat_of_the_embedded_metric()
    {
        Assert.Equal(133, Catalog.Habitats.Count);
        Assert.Equal(15, Catalog.BroadHabitats.Count);
        Assert.DoesNotContain(Catalog.CreatableHabitats, habitat => habitat.Name == "Felled");
    }

    [Theory]
    [InlineData("Hazel scrub")]
    [InlineData("Heathland and shrub - Hazel scrub")]
    [InlineData("  heathland AND shrub - hazel scrub ")]
    public void Habitat_resolves_by_short_name_or_description(string text) =>
        Assert.Equal("Heathland and shrub - Hazel scrub", Catalog.FindHabitat(text)?.Description);

    [Fact]
    public void Condition_options_follow_the_habitats_condition_group()
    {
        var hazelScrub = Catalog.FindHabitat("Hazel scrub")!;
        Assert.Equal(["Good", "Fairly Good", "Moderate", "Fairly Poor", "Poor"], Catalog.GetConditionOptions(hazelScrub));
    }

    /// <summary>Row 11 of the user's workbook: Hazel scrub, Moderate, formally identified in local strategy.</summary>
    [Fact]
    public void Matches_the_workbooks_hazel_scrub_row()
    {
        var result = BngHabitatCreationCalculator.Calculate(
            new BngHabitatCreationInput("Hazel scrub", "Moderate", "Formally identified in local strategy", 0, 12),
            Catalog);

        Assert.Equal(BngCalculationOutcome.Calculated, result.Outcome);
        Assert.Equal("Heathland and shrub", result.BroadHabitat);
        Assert.Equal("Medium", result.Distinctiveness);
        Assert.Equal("4", result.DistinctivenessScore);
        Assert.Equal("2", result.ConditionScore);
        Assert.Equal("High strategic significance", result.StrategicSignificance);
        Assert.Equal("1.15", result.StrategicSignificanceMultiplier);
        Assert.Equal("10", result.StandardTimeToTarget);
        Assert.Equal(BngHabitatCreationCalculator.StandardTimeApplied, result.TimeToTargetStatus);
        Assert.Equal("10", result.FinalTimeToTarget);
        Assert.Equal("0.700", result.FinalTimeToTargetMultiplier);
        Assert.Equal("Medium", result.FinalDifficulty);
        Assert.Equal("0.67", result.DifficultyMultiplier);
        Assert.Equal(12 * 4 * 2 * 1.15 * 0.7002822742 * 0.67, result.HabitatUnits!.Value, 9);
    }

    [Theory]
    [InlineData(null, "Moderate", "Formally identified in local strategy", "Proposed habitat is missing.")]
    [InlineData("Not a habitat", "Moderate", "Formally identified in local strategy", "is not a habitat")]
    [InlineData("Felled", "Moderate", "Formally identified in local strategy", "can't be used as a created habitat")]
    [InlineData("Hazel scrub", null, "Formally identified in local strategy", "Condition is missing.")]
    [InlineData("Hazel scrub", "N/A - Other", "Formally identified in local strategy", "isn't an option")]
    [InlineData("Hazel scrub", "Moderate", "", "Strategic significance is missing.")]
    public void Missing_or_invalid_inputs_need_info(string? habitat, string? condition, string strategic, string expectedIssue)
    {
        var result = BngHabitatCreationCalculator.Calculate(new BngHabitatCreationInput(habitat, condition, strategic, 0, 1), Catalog);

        Assert.Equal(BngCalculationOutcome.NeedsInfo, result.Outcome);
        Assert.Contains(result.Issues, issue => issue.Contains(expectedIssue, StringComparison.Ordinal));
        Assert.Null(result.HabitatUnits);
    }

    /// <summary>
    /// Replays every case the official workbook calculated in
    /// <c>TestData/BngA2GoldenCases.tsv</c> (every creatable habitat × each of its conditions ×
    /// advance/delay offsets) and requires identical results in every calculated column.
    /// </summary>
    [Fact]
    public void Matches_the_official_workbook_for_every_golden_case()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "BngA2GoldenCases.tsv");
        var lines = File.ReadAllLines(path);
        var header = lines[0].Split('\t');
        var mismatches = new List<string>();

        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split('\t');
            string Cell(string name) => cells[Array.IndexOf(header, name)];

            var input = new BngHabitatCreationInput(
                Cell("Habitat"),
                Cell("Condition"),
                Cell("StrategicSignificance"),
                int.Parse(Cell("YearOffset"), CultureInfo.InvariantCulture),
                double.Parse(Cell("AreaHectares"), CultureInfo.InvariantCulture));
            var result = BngHabitatCreationCalculator.Calculate(input, Catalog);

            void Expect(string column, string actual)
            {
                var expected = Cell(column);
                var same = double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
                           && double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber)
                    ? Math.Abs(expectedNumber - actualNumber) < 5e-4
                    : string.Equals(expected, actual, StringComparison.Ordinal);
                if (!same)
                {
                    mismatches.Add($"{input.Habitat} / {input.Condition} / offset {input.YearOffset}: {column} expected '{expected}' but was '{actual}'");
                }
            }

            Expect("H_Distinctiveness", result.Distinctiveness);
            Expect("I_Score", result.DistinctivenessScore);
            Expect("K_ConditionScore", result.ConditionScore);
            Expect("M_StrategicCategory", result.StrategicSignificance);
            Expect("N_StrategicMultiplier", result.StrategicSignificanceMultiplier);
            Expect("O_StandardTime", result.StandardTimeToTarget);
            Expect("R_TimeStatus", result.TimeToTargetStatus);
            Expect("S_FinalTime", result.FinalTimeToTarget);
            Expect("T_TimeMultiplier", result.FinalTimeToTargetMultiplier);
            Expect("U_StandardDifficulty", result.StandardDifficulty);
            Expect("V_AppliedDifficulty", result.AppliedDifficulty);
            Expect("W_FinalDifficulty", result.FinalDifficulty);
            Expect("X_DifficultyMultiplier", result.DifficultyMultiplier);

            var expectedUnits = Cell("Y_HabitatUnits");
            var unitsMatch = expectedUnits.Length == 0
                ? result.HabitatUnits is null
                : result.HabitatUnits is { } units && Math.Abs(units - double.Parse(expectedUnits, CultureInfo.InvariantCulture)) < 1e-9;
            if (!unitsMatch)
            {
                mismatches.Add($"{input.Habitat} / {input.Condition} / offset {input.YearOffset}: Y expected '{expectedUnits}' but was '{result.HabitatUnits}'");
            }
        }

        Assert.True(lines.Length > 3000, "Golden case file is unexpectedly small.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches:\n" + string.Join('\n', mismatches.Take(25)));
    }
}
