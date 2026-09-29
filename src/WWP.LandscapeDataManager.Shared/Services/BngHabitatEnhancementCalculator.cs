using System.Globalization;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// The post-enhancement target for an enhanced baseline floor — sheet A-3's inputs (columns R, Y,
/// AA, AE/AF). <paramref name="YearOffset"/> is signed like <see cref="BngHabitatCreationInput"/>:
/// positive = enhanced in advance (AE), negative = delay in starting (AF).
/// </summary>
public sealed record BngEnhancementInput(
    string? Habitat,
    string? Condition,
    string? StrategicSignificance,
    int YearOffset);

public sealed record BngEnhancementResult(
    BngCalculationOutcome Outcome,
    IReadOnlyList<string> Issues,
    string BroadHabitat,
    string HabitatDescription,
    string AreaHectares,
    string Distinctiveness,
    string DistinctivenessScore,
    string ConditionScore,
    string StrategicSignificance,
    string StrategicSignificanceMultiplier,
    string DistinctivenessChange,
    string ConditionChange,
    string StandardTimeToTarget,
    string TimeToTargetStatus,
    string FinalTimeToTarget,
    string FinalTimeToTargetMultiplier,
    string StandardDifficulty,
    string AppliedDifficulty,
    string FinalDifficulty,
    string DifficultyMultiplier,
    string HabitatUnitsText,
    double? HabitatUnits);

/// <summary>
/// A literal port of the metric's "A-3 On-Site Habitat Enhancement" row formulas (23.07.2024
/// release), reading its baseline columns (F–O, V, AU) from the floor's own A-1 row the way the
/// sheet looks them up by baseline reference. Same Excel comparison semantics as
/// <see cref="BngHabitatCreationCalculator"/>. Column letters in comments refer to sheet A-3.
/// </summary>
public static class BngHabitatEnhancementCalculator
{
    public const string CheckDataMessage = "Check Data ⚠";
    public const string NotPossible = BngHabitatCreationCalculator.NotPossible;
    public const string TradingRulesNotSatisfied = "Error - Trading rules not satisfied ▲";
    public const string TradingDown = "Error Trading Down ▲";
    public const string EnhancementNotPossible = "Error - Enhancement not possible ▲";
    public const string LowerDistinctivenessHabitat = "Lower Distinctiveness Habitat";

    private const string LittoralSeagrass = "Littoral seagrass";
    private const string Iggi = "Artificial hard structures with integrated greening of grey infrastructure (IGGI)";

    public static BngEnhancementResult Calculate(BngBaselineResult baseline, BngEnhancementInput input, BngMetricCatalog catalog)
    {
        var issues = new List<string>();
        if (baseline.Outcome == BngCalculationOutcome.NeedsInfo || baseline.Habitat is null)
        {
            issues.Add("Complete the baseline habitat first.");
        }

        var habitat = catalog.FindHabitat(input.Habitat);
        if (habitat is null)
        {
            issues.Add(string.IsNullOrWhiteSpace(input.Habitat)
                ? "Enhanced (proposed) habitat is missing."
                : $"'{input.Habitat}' is not a habitat in {catalog.DisplayVersion}.");
        }
        else if (!catalog.IsEnhancementHabitat(habitat))
        {
            issues.Add($"'{habitat.Name}' can't be used as an enhanced habitat.");
        }

        var condition = input.Condition?.Trim();
        if (string.IsNullOrEmpty(condition))
        {
            issues.Add("Enhanced (proposed) condition is missing.");
        }
        else if (habitat is not null && !catalog.GetConditionOptions(habitat).Contains(condition, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add($"Condition '{condition}' isn't an option for '{habitat.Name}' ({string.Join(", ", catalog.GetConditionOptions(habitat))}).");
        }
        else if (habitat is not null)
        {
            condition = catalog.GetConditionOptions(habitat).First(option => string.Equals(option, condition, StringComparison.OrdinalIgnoreCase));
        }

        var strategic = catalog.FindStrategicSignificance(input.StrategicSignificance);
        if (strategic is null)
        {
            issues.Add(string.IsNullOrWhiteSpace(input.StrategicSignificance)
                ? "Strategic significance is missing."
                : $"'{input.StrategicSignificance}' is not a strategic significance option.");
        }

        if (issues.Count > 0 || habitat is null || strategic is null || condition is null || baseline.Habitat is null)
        {
            return NeedsInfo(issues, habitat, baseline);
        }

        // Baseline columns looked up from A-1: F habitat, H/I distinctiveness, J/K condition,
        // M strategic multiplier, N total units, O trading rule, V area enhanced, AU irreplaceable.
        var baselineF = baseline.HabitatDescription;
        var baselineBandH = baseline.Distinctiveness;
        var baselineScoreI = baseline.DistinctivenessScoreCell;
        var baselineConditionJ = baseline.Condition;
        var baselineConditionScoreK = baseline.ConditionScoreCell;
        var baselineUnitsN = baseline.TotalHabitatUnitsCell;
        var tradingRuleO = baseline.TradingRule;
        var irreplaceableAU = baseline.Irreplaceable;
        var areaV = baseline.Fate == BngBaselineFate.Enhanced ? baseline.AreaHectares : 0d;

        // R, S — proposed habitat (name, description); W, X — its distinctiveness.
        var proposedR = habitat.Name;
        var proposedS = habitat.Description;
        var bandW = habitat.Distinctiveness;
        BngCellValue scoreX = catalog.DistinctivenessScores.TryGetValue(bandW, out var score)
            ? BngCellValue.Of(score)
            : BngCellValue.Of(string.Empty);

        // Z — proposed condition score; AB, AC — strategic significance.
        var conditionScoreZ = habitat.ConditionScores.GetValueOrDefault(condition);
        var categoryAB = strategic.Category;
        var multiplierAC = strategic.Multiplier;

        // AE, AF — advance / delay years.
        var advanceAE = ToYearsCell(Math.Max(input.YearOffset, 0));
        var delayAF = ToYearsCell(Math.Max(-input.YearOffset, 0));

        var distinctivenessT = ComputeDistinctivenessChange(tradingRuleO, baselineScoreI, scoreX, baselineF, proposedS, proposedR, baselineBandH, bandW);
        var conditionU = ComputeConditionChange(baselineF, proposedS, irreplaceableAU, distinctivenessT, baselineScoreI, scoreX,
            baselineConditionScoreK, conditionScoreZ, baselineConditionJ, condition);

        // AD — standard time to target condition, from G-5 by habitat and "baseline - proposed" condition change.
        BngCellValue standardTimeAD;
        if (StartsWith(conditionU, "Error") || StartsWith(distinctivenessT, "Error"))
        {
            standardTimeAD = BngCellValue.Of(CheckDataMessage);
        }
        else if (habitat.EnhancementTimeToTargetYears is { } table &&
                 table.Keys.FirstOrDefault(key => string.Equals(key, conditionU, StringComparison.OrdinalIgnoreCase)) is { } key)
        {
            // INDEX of an empty table cell returns 0 in a formula.
            var cell = table[key];
            standardTimeAD = cell.IsEmpty ? BngCellValue.Of(0) : cell;
        }
        else
        {
            standardTimeAD = BngCellValue.Of(string.Empty);
        }

        var statusAG = ComputeTimeToTargetStatus(standardTimeAD, proposedR, advanceAE, delayAF);
        var finalTimeAH = ComputeFinalTimeToTarget(statusAG, standardTimeAD, advanceAE, delayAF);

        // AI — final time to target multiplier.
        BngCellValue timeMultiplierAI = finalTimeAH.Text == CheckDataMessage
            ? BngCellValue.Of(CheckDataMessage)
            : catalog.GetTemporalMultiplier(finalTimeAH.ToString()) is { } temporal
                ? BngCellValue.Of(temporal)
                : BngCellValue.Of(string.Empty);

        // AJ..AM — difficulty.
        var standardDifficultyAJ = habitat.EnhancementDifficulty;
        var appliedDifficultyAK = statusAG == BngHabitatCreationCalculator.ReachedTargetMessage
            ? BngHabitatCreationCalculator.LowDifficultyApplied
            : BngHabitatCreationCalculator.StandardDifficultyApplied;

        string finalDifficultyAL;
        if (appliedDifficultyAK == BngHabitatCreationCalculator.StandardDifficultyApplied && Compare(standardTimeAD, advanceAE) > 0)
        {
            finalDifficultyAL = standardDifficultyAJ;
        }
        else if (appliedDifficultyAK == BngHabitatCreationCalculator.LowDifficultyApplied && Compare(advanceAE, standardTimeAD) >= 0)
        {
            finalDifficultyAL = "Low";
        }
        else
        {
            finalDifficultyAL = standardDifficultyAJ;
        }

        BngCellValue difficultyMultiplierAM = finalTimeAH.Text == CheckDataMessage
            ? BngCellValue.Of(CheckDataMessage)
            : catalog.DifficultyMultipliers.TryGetValue(finalDifficultyAL, out var difficultyMultiplier)
                ? BngCellValue.Of(difficultyMultiplier)
                : BngCellValue.Of(string.Empty);

        // AN — habitat units delivered.
        BngCellValue unitsAN;
        if (baselineUnitsN.Text == BngHabitatBaselineCalculator.IrreplaceableTreesMessage)
        {
            unitsAN = baselineUnitsN;
        }
        else if (Arithmetic(scoreX) is { } x && Arithmetic(conditionScoreZ) is { } z && Arithmetic(baselineScoreI) is { } i &&
                 Arithmetic(difficultyMultiplierAM) is { } m && Arithmetic(timeMultiplierAI) is { } t &&
                 // IF(K>Z, …) compares with Excel's semantics; K is only used in arithmetic when K <= Z.
                 (Compare(baselineConditionScoreK, conditionScoreZ) > 0 ? z : Arithmetic(baselineConditionScoreK)) is { } reference)
        {
            unitsAN = BngCellValue.Of(((areaV * x * z - areaV * i * reference) * (m * t) + areaV * i * reference) * multiplierAC);
        }
        else
        {
            unitsAN = BngCellValue.Of(string.Empty);
        }

        var outputs = new[] { distinctivenessT, conditionU, standardTimeAD.ToString(), statusAG, finalTimeAH.ToString(), timeMultiplierAI.ToString(), appliedDifficultyAK, unitsAN.ToString(), conditionScoreZ.ToString() };
        if (outputs.FirstOrDefault(text => text.Contains('⚠') || text.Contains('▲')) is { } flagged)
        {
            issues.Add(flagged);
        }

        if (unitsAN.Number is null)
        {
            issues.Add("Enhanced habitat units could not be calculated for this combination.");
        }

        return new BngEnhancementResult(
            issues.Count == 0 ? BngCalculationOutcome.Calculated : BngCalculationOutcome.CheckData,
            issues,
            habitat.BroadHabitat ?? string.Empty,
            proposedS,
            areaV.ToString("0.####", CultureInfo.InvariantCulture),
            bandW,
            scoreX.ToString(),
            conditionScoreZ.ToString(),
            categoryAB,
            BngCellValue.Of(multiplierAC).ToString(),
            distinctivenessT,
            conditionU,
            standardTimeAD.ToString(),
            statusAG,
            finalTimeAH.ToString(),
            timeMultiplierAI.Number is { } tm ? tm.ToString("0.000", CultureInfo.InvariantCulture) : timeMultiplierAI.ToString(),
            standardDifficultyAJ,
            appliedDifficultyAK,
            finalDifficultyAL,
            difficultyMultiplierAM.ToString(),
            unitsAN.Number is { } units ? units.ToString("0.00", CultureInfo.InvariantCulture) : unitsAN.ToString(),
            unitsAN.Number);
    }

    /// <summary>Column T — "Distinctiveness change".</summary>
    private static string ComputeDistinctivenessChange(
        string tradingRuleO, BngCellValue scoreI, BngCellValue scoreX, string baselineF, string proposedS, string proposedR, string bandH, string bandW)
    {
        var rule = Left(tradingRuleO, 6);
        if (Is(rule, "Same d") && Compare(scoreI, scoreX) > 0)
        {
            return TradingRulesNotSatisfied;
        }

        if (Is(rule, "Same b") && !Is(Left(baselineF, 5), Left(proposedS, 5)) && Compare(scoreI, scoreX) > 0)
        {
            return TradingRulesNotSatisfied;
        }

        if ((Is(rule, "Same h") || Is(rule, "Bespok")) && !Is(baselineF, proposedS))
        {
            return TradingRulesNotSatisfied;
        }

        if (Compare(scoreX, scoreI) < 0)
        {
            return TradingDown;
        }

        if (Is(proposedR, LittoralSeagrass) &&
            !Is(baselineF, "Intertidal sediment - Littoral seagrass") && !Is(baselineF, "Intertidal sediment - Littoral sand"))
        {
            return EnhancementNotPossible;
        }

        if (Is(proposedR, Iggi) &&
            !Is(baselineF, "Intertidal hard structures - " + Iggi) &&
            !Is(baselineF, "Intertidal hard structures - Artificial hard structures") &&
            !Is(baselineF, "Intertidal hard structures - Artificial features of hard structures"))
        {
            return EnhancementNotPossible;
        }

        return $"{bandH} - {bandW}";
    }

    /// <summary>Column U — "Condition change" (also the G-5 column key).</summary>
    private static string ComputeConditionChange(
        string baselineF, string proposedS, string irreplaceableAU, string distinctivenessT, BngCellValue scoreI, BngCellValue scoreX,
        BngCellValue conditionScoreK, BngCellValue conditionScoreZ, string baselineConditionJ, string proposedConditionY)
    {
        if (!Is(proposedS, baselineF) && Is(irreplaceableAU, "Yes"))
        {
            return "Error you cannot replace an irreplaceable habitat ▲";
        }

        if (!Is(Left(baselineF, 55), Left(proposedS, 55)) && Is(distinctivenessT, "High - High"))
        {
            return "Error - Not like for like ▲";
        }

        var sameDistinctiveness = Compare(scoreI, scoreX) == 0;
        if (sameDistinctiveness && Compare(conditionScoreK, conditionScoreZ) > 0)
        {
            return "Error - Can not reduce condition ▲";
        }

        if (sameDistinctiveness && Compare(conditionScoreK, conditionScoreZ) == 0)
        {
            return "Error - No enhancement ▲";
        }

        if (Compare(conditionScoreK, conditionScoreZ) > 0)
        {
            return "Error - condition cannot be reduced ▲";
        }

        if (Compare(scoreX, scoreI) < 0)
        {
            return TradingDown;
        }

        return !Is(baselineF, proposedS) && Compare(scoreI, scoreX) < 0
            ? $"{LowerDistinctivenessHabitat} - {proposedConditionY}"
            : $"{baselineConditionJ} - {proposedConditionY}";
    }

    /// <summary>
    /// Column AG — "Standard or adjusted time to target condition". The sheet's
    /// <c>IF(O11="","",…)</c> reads the row above's trading rule; in a filled sheet that is never
    /// blank, so it's treated as non-blank. Both-advance-and-delay can't occur with one signed offset.
    /// </summary>
    private static string ComputeTimeToTargetStatus(BngCellValue standardTimeAD, string proposedR, BngCellValue advanceAE, BngCellValue delayAF)
    {
        if (standardTimeAD.IsEmpty)
        {
            return string.Empty;
        }

        if (standardTimeAD.Text == CheckDataMessage)
        {
            return CheckDataMessage;
        }

        if (string.IsNullOrEmpty(proposedR))
        {
            return string.Empty;
        }

        if (Compare(standardTimeAD, advanceAE) <= 0 && Compare(delayAF, BngCellValue.Of(0)) == 0)
        {
            return BngHabitatCreationCalculator.ReachedTargetMessage;
        }

        if (Compare(advanceAE, BngCellValue.Of(0)) > 0)
        {
            return BngHabitatCreationCalculator.StartedOrInPlaceMessage;
        }

        return Compare(delayAF, BngCellValue.Of(0)) > 0
            ? BngHabitatCreationCalculator.DelayMessage
            : BngHabitatCreationCalculator.StandardTimeApplied;
    }

    /// <summary>Column AH — "Final time to target condition (years)".</summary>
    private static BngCellValue ComputeFinalTimeToTarget(string statusAG, BngCellValue standardTimeAD, BngCellValue advanceAE, BngCellValue delayAF)
    {
        if (statusAG == CheckDataMessage)
        {
            return BngCellValue.Of(CheckDataMessage);
        }

        if (standardTimeAD.IsEmpty)
        {
            return BngCellValue.Of(string.Empty);
        }

        if (standardTimeAD.Text == NotPossible)
        {
            return BngCellValue.Of(CheckDataMessage);
        }

        var thirtyPlus = BngCellValue.Of(BngHabitatCreationCalculator.ThirtyPlus);
        if (standardTimeAD.Text == BngHabitatCreationCalculator.ThirtyPlus)
        {
            if (Compare(advanceAE, BngCellValue.Of(0)) == 0)
            {
                return thirtyPlus;
            }

            if (advanceAE.Text == BngHabitatCreationCalculator.ThirtyPlus)
            {
                return BngCellValue.Of(0);
            }

            return BngCellValue.Of(30 - (advanceAE.Number ?? 0));
        }

        if (Compare(advanceAE, standardTimeAD) > 0)
        {
            return BngCellValue.Of(0);
        }

        if (delayAF.Text == BngHabitatCreationCalculator.ThirtyPlus)
        {
            return thirtyPlus;
        }

        var standard = standardTimeAD.Number ?? 0;
        var advance = advanceAE.Number ?? 0;
        var delay = delayAF.Number ?? 0;
        if (standard + delay > 30 || standard - advance > 30)
        {
            return thirtyPlus;
        }

        if (standard + delay - advance > 0)
        {
            return BngCellValue.Of(standard + delay - advance);
        }

        return BngCellValue.Of(standard - advance > 0 ? standard - advance : standard + delay - advance);
    }

    private static BngEnhancementResult NeedsInfo(IReadOnlyList<string> issues, BngHabitat? habitat, BngBaselineResult baseline) => new(
        BngCalculationOutcome.NeedsInfo,
        issues,
        habitat?.BroadHabitat ?? string.Empty,
        habitat?.Description ?? string.Empty,
        baseline.AreaHectares.ToString("0.####", CultureInfo.InvariantCulture),
        habitat?.Distinctiveness ?? string.Empty,
        habitat is null ? string.Empty : BngCellValue.Of(habitat.DistinctivenessScore).ToString(),
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        habitat?.EnhancementDifficulty ?? string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty,
        null);

    private static BngCellValue ToYearsCell(int years) => years switch
    {
        <= 0 => BngCellValue.Empty,
        > 30 => BngCellValue.Of(BngHabitatCreationCalculator.ThirtyPlus),
        _ => BngCellValue.Of(years)
    };

    /// <summary>A cell in arithmetic: a number as itself, blank as 0, any text (incl. a formula's "") as #VALUE!.</summary>
    private static double? Arithmetic(BngCellValue cell) => cell.Number is { } number ? number : cell.Text is null ? 0d : null;

    private static int Compare(BngCellValue left, BngCellValue right) => BngHabitatCreationCalculator.Compare(left, right);

    private static bool StartsWith(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool Is(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Left(string text, int length) => text.Length <= length ? text : text[..length];
}
