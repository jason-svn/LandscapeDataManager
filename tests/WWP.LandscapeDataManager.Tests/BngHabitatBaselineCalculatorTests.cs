using System.Globalization;
using WWP.LandscapeDataManager.Shared.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class BngHabitatBaselineCalculatorTests
{
    private static readonly BngMetricCatalog Catalog = BngMetricCatalog.Default;

    [Fact]
    public void Catalog_loads_the_baseline_and_enhancement_tables()
    {
        Assert.Contains(Catalog.BaselineHabitats, habitat => habitat.Name == "Felled");
        Assert.DoesNotContain(Catalog.BaselineHabitats, habitat => habitat.Name == "Replacement for felled woodland");
        Assert.Equal("Yes/No", Catalog.FindHabitat("Urban tree")!.Irreplaceable);
        Assert.NotNull(Catalog.FindHabitat("Modified grassland")!.EnhancementTimeToTargetYears);
        Assert.NotEmpty(Catalog.EnhancementHabitats);
    }

    [Fact]
    public void A_yes_no_habitat_needs_its_irreplaceable_status_answered()
    {
        var input = new BngBaselineInput("Urban tree", null, "Good", Catalog.StrategicSignificance[0].Description, 0.1, BngBaselineFate.Retained);

        var result = BngHabitatBaselineCalculator.Calculate(input, Catalog);

        Assert.Equal(BngCalculationOutcome.NeedsInfo, result.Outcome);
    }

    [Fact]
    public void A_retained_floor_keeps_its_baseline_units_and_a_lost_one_loses_them()
    {
        var strategic = Catalog.StrategicSignificance[2].Description;
        var retained = BngHabitatBaselineCalculator.Calculate(
            new BngBaselineInput("Modified grassland", null, "Poor", strategic, 0.5, BngBaselineFate.Retained), Catalog);
        var lost = BngHabitatBaselineCalculator.Calculate(
            new BngBaselineInput("Modified grassland", null, "Poor", strategic, 0.5, BngBaselineFate.Lost), Catalog);

        Assert.Equal(BngCalculationOutcome.Calculated, retained.Outcome);
        Assert.Equal(retained.BaselineUnits, retained.RetainedUnits);
        Assert.Equal(0d, lost.RetainedUnits);
        Assert.Equal(lost.BaselineUnits, lost.LostUnits);
    }

    /// <summary>Replays <c>TestData/BngA1GoldenCases.tsv</c> — every baseline habitat × condition × fate, calculated by the official workbook.</summary>
    [Fact]
    public void A1_matches_the_official_workbook_for_every_golden_case()
    {
        var (header, rows) = Read("BngA1GoldenCases.tsv");
        var mismatches = new List<string>();

        foreach (var cells in rows)
        {
            string Cell(string name) => cells[Array.IndexOf(header, name)];
            var input = new BngBaselineInput(
                Cell("Habitat"), Cell("Irreplaceable"), Cell("Condition"), Cell("StrategicSignificance"),
                double.Parse(Cell("AreaHectares"), CultureInfo.InvariantCulture),
                Enum.Parse<BngBaselineFate>(Cell("Fate")));
            var result = BngHabitatBaselineCalculator.Calculate(input, Catalog);
            var label = $"{input.Habitat} / {input.Irreplaceable} / {input.Condition} / {input.Fate}";

            Expect(mismatches, label, "I_Distinctiveness", Cell("I_Distinctiveness"), result.Distinctiveness);
            Expect(mismatches, label, "J_Score", Cell("J_Score"), result.DistinctivenessScore);
            Expect(mismatches, label, "L_ConditionScore", Cell("L_ConditionScore"), result.ConditionScore);
            Expect(mismatches, label, "N_StrategicCategory", Cell("N_StrategicCategory"), result.StrategicSignificance);
            Expect(mismatches, label, "O_StrategicMultiplier", Cell("O_StrategicMultiplier"), result.StrategicSignificanceMultiplier);
            Expect(mismatches, label, "P_TradingRule", Cell("P_TradingRule"), result.TradingRule);
            Expect(mismatches, label, "Q_TotalUnits", Cell("Q_TotalUnits"), result.TotalHabitatUnitsText);
            Expect(mismatches, label, "U_UnitsRetained", Cell("U_UnitsRetained"), result.UnitsRetainedText);
            Expect(mismatches, label, "V_UnitsEnhanced", Cell("V_UnitsEnhanced"), result.UnitsEnhancedText);
            Expect(mismatches, label, "W_AreaLost", Cell("W_AreaLost"), result.AreaLostText);
            Expect(mismatches, label, "X_UnitsLost", Cell("X_UnitsLost"), result.UnitsLostText);
            ExpectNumber(mismatches, label, "Q_TotalUnits", Cell("Q_TotalUnits"), result.BaselineUnits);
            ExpectNumber(mismatches, label, "U_UnitsRetained", Cell("U_UnitsRetained"), result.RetainedUnits);
            ExpectNumber(mismatches, label, "X_UnitsLost", Cell("X_UnitsLost"), result.LostUnits);
        }

        Assert.True(rows.Count > 1000, "A-1 golden case file is unexpectedly small.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches:\n" + string.Join('\n', mismatches.Take(25)));
    }

    /// <summary>Replays <c>TestData/BngA3GoldenCases.tsv</c> — baseline habitat × condition paired with enhancement targets, calculated by the official workbook.</summary>
    [Fact]
    public void A3_matches_the_official_workbook_for_every_golden_case()
    {
        var (header, rows) = Read("BngA3GoldenCases.tsv");
        var mismatches = new List<string>();

        foreach (var cells in rows)
        {
            string Cell(string name) => cells[Array.IndexOf(header, name)];
            var strategic = Cell("StrategicSignificance");
            var baseline = BngHabitatBaselineCalculator.Calculate(
                new BngBaselineInput(Cell("BaselineHabitat"), Cell("Irreplaceable"), Cell("BaselineCondition"), strategic,
                    double.Parse(Cell("AreaHectares"), CultureInfo.InvariantCulture), BngBaselineFate.Enhanced),
                Catalog);
            var input = new BngEnhancementInput(
                Cell("ProposedHabitat"), Cell("ProposedCondition"), strategic, int.Parse(Cell("YearOffset"), CultureInfo.InvariantCulture));
            var result = BngHabitatEnhancementCalculator.Calculate(baseline, input, Catalog);
            var label = $"{Cell("BaselineHabitat")} ({Cell("BaselineCondition")}) → {input.Habitat} ({input.Condition}) / offset {input.YearOffset}";

            Expect(mismatches, label, "T_DistinctivenessChange", Cell("T_DistinctivenessChange"), result.DistinctivenessChange);
            Expect(mismatches, label, "U_ConditionChange", Cell("U_ConditionChange"), result.ConditionChange);
            Expect(mismatches, label, "W_Distinctiveness", Cell("W_Distinctiveness"), result.Distinctiveness);
            Expect(mismatches, label, "X_Score", Cell("X_Score"), result.DistinctivenessScore);
            Expect(mismatches, label, "Z_ConditionScore", Cell("Z_ConditionScore"), result.ConditionScore);
            Expect(mismatches, label, "AB_StrategicCategory", Cell("AB_StrategicCategory"), result.StrategicSignificance);
            Expect(mismatches, label, "AC_StrategicMultiplier", Cell("AC_StrategicMultiplier"), result.StrategicSignificanceMultiplier);
            Expect(mismatches, label, "AD_StandardTime", Cell("AD_StandardTime"), result.StandardTimeToTarget);
            Expect(mismatches, label, "AG_TimeStatus", Cell("AG_TimeStatus"), result.TimeToTargetStatus);
            Expect(mismatches, label, "AH_FinalTime", Cell("AH_FinalTime"), result.FinalTimeToTarget);
            Expect(mismatches, label, "AI_TimeMultiplier", Cell("AI_TimeMultiplier"), result.FinalTimeToTargetMultiplier);
            Expect(mismatches, label, "AJ_StandardDifficulty", Cell("AJ_StandardDifficulty"), result.StandardDifficulty);
            Expect(mismatches, label, "AK_AppliedDifficulty", Cell("AK_AppliedDifficulty"), result.AppliedDifficulty);
            Expect(mismatches, label, "AL_FinalDifficulty", Cell("AL_FinalDifficulty"), result.FinalDifficulty);
            Expect(mismatches, label, "AM_DifficultyMultiplier", Cell("AM_DifficultyMultiplier"), result.DifficultyMultiplier);
            Expect(mismatches, label, "AN_HabitatUnits", Cell("AN_HabitatUnits"), result.HabitatUnitsText);
            ExpectNumber(mismatches, label, "AN_HabitatUnits", Cell("AN_HabitatUnits"), result.HabitatUnits);
        }

        Assert.True(rows.Count > 1000, "A-3 golden case file is unexpectedly small.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches:\n" + string.Join('\n', mismatches.Take(25)));
    }

    private static (string[] Header, List<string[]> Rows) Read(string fileName)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
        return (lines[0].Split('\t'), lines.Skip(1).Select(line => line.Split('\t')).ToList());
    }

    /// <summary>Numbers match within rounding (the result texts are rounded for display), everything else exactly.</summary>
    private static void Expect(List<string> mismatches, string label, string column, string expected, string actual)
    {
        var same = double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
                   && double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber)
            ? Math.Abs(expectedNumber - actualNumber) <= 5.0001e-3
            : string.Equals(expected, actual, StringComparison.Ordinal);
        if (!same)
        {
            mismatches.Add($"{label}: {column} expected '{expected}' but was '{actual}'");
        }
    }

    /// <summary>A units cell Excel left numeric must match to full precision; one it left as text must be null here.</summary>
    private static void ExpectNumber(List<string> mismatches, string label, string column, string expected, double? actual)
    {
        var same = double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
            ? actual is { } value && Math.Abs(value - expectedNumber) < 1e-9
            : actual is null;
        if (!same)
        {
            mismatches.Add($"{label}: {column} (number) expected '{expected}' but was '{actual}'");
        }
    }
}
