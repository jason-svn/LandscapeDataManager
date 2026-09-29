using System.Globalization;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// One floor's inputs for the metric's A-2 On-Site Habitat Creation row.
/// <paramref name="YearOffset"/> combines the sheet's two year columns into one signed number:
/// positive = "Habitat created in advance (years)" (column P), negative = "Delay in starting
/// habitat creation (years)" (column Q), 0 = neither.
/// </summary>
public sealed record BngHabitatCreationInput(
    string? Habitat,
    string? Condition,
    string? StrategicSignificance,
    int YearOffset,
    double AreaHectares);

public enum BngCalculationOutcome
{
    /// <summary>Every input resolved and the metric raised no warning.</summary>
    Calculated,

    /// <summary>Habitat, condition or strategic significance is missing or not a metric option — nothing was calculated.</summary>
    NeedsInfo,

    /// <summary>Calculated, but the metric itself flags the row (a "Check details ⚠" or "Check Data ⚠" message).</summary>
    CheckData
}

/// <summary>
/// The A-2 row's calculated cells, as the text Excel displays in each column, plus the habitat
/// units as a number so Revit schedules can total them.
/// </summary>
public sealed record BngHabitatCreationResult(
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
/// A literal port of the Statutory Biodiversity Metric's "A-2 On-Site Habitat Creation" row
/// formulas (columns A–Y of the 23.07.2024 release), so a floor's result is the same number the
/// official workbook would give for the same inputs. The formulas are ported branch-for-branch,
/// including how Excel compares blanks, numbers and text (a blank cell equals 0, and any text is
/// greater than any number) — that's what decides e.g. which "Check details" message a row gets,
/// so it is kept even where it reads oddly. Column letters in comments refer to sheet A-2.
/// </summary>
public static class BngHabitatCreationCalculator
{
    public const string NotPossible = "Not Possible ▲";
    public const string CheckDataMessage = "Check Data ⚠";
    public const string ThirtyPlus = "30+";

    public const string StandardTimeApplied = "Standard time to target condition applied";
    public const string ReachedTargetMessage = "Check details - Is there evidence that habitat has reached target condition? ⚠";
    public const string PoorThresholdMessage = "Check details - Is there evidence habitat creation started and the threshold for Poor condition reached? ⚠";
    public const string CreationInPlaceMessage = "Check details - Is there evidence habitat creation in place? ⚠";
    public const string DelayMessage = "Check details- Delay in starting habitat in required condition? ⚠";
    public const string StartedOrInPlaceMessage = "Check details - Is there evidence habitat creation started/in place? ⚠";

    public const string StandardDifficultyApplied = "Standard difficulty applied";
    public const string LowDifficultyApplied = "Low Difficulty - only applicable if all habitat created before losses ⚠";
    public const string EnhancementDifficultyApplied = "Enhancement difficulty applied";

    /// <summary>Column V's hard-coded exceptions: these habitats never switch to enhancement difficulty.</summary>
    private static readonly HashSet<string> EnhancementDifficultyExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Traditional orchards",
        "Ornamental lake or pond",
        "Ponds (non-priority habitat)",
        "Ruderal/Ephemeral",
        "Tall forbs",
        "Developed land; sealed surface"
    };

    /// <summary>The year dropdown in columns P/Q stops at 30; anything longer is its "30+" option.</summary>
    private const int MaxListedYears = 30;

    public static BngHabitatCreationResult Calculate(BngHabitatCreationInput input, BngMetricCatalog catalog)
    {
        var issues = new List<string>();
        var habitat = catalog.FindHabitat(input.Habitat);
        if (habitat is null)
        {
            issues.Add(string.IsNullOrWhiteSpace(input.Habitat)
                ? "Proposed habitat is missing."
                : $"'{input.Habitat}' is not a habitat in {catalog.DisplayVersion}.");
        }
        else if (!habitat.CanBeCreated)
        {
            issues.Add($"'{habitat.Name}' can't be used as a created habitat.");
        }

        var condition = input.Condition?.Trim();
        if (string.IsNullOrEmpty(condition))
        {
            issues.Add("Condition is missing.");
        }
        else if (habitat is not null && !catalog.GetConditionOptions(habitat).Contains(condition, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add($"Condition '{condition}' isn't an option for '{habitat.Name}' ({string.Join(", ", catalog.GetConditionOptions(habitat))}).");
        }
        else if (habitat is not null)
        {
            // Normalize to the metric's own casing so the text written to Revit matches Excel's.
            condition = catalog.GetConditionOptions(habitat).First(option => string.Equals(option, condition, StringComparison.OrdinalIgnoreCase));
        }

        var strategic = catalog.FindStrategicSignificance(input.StrategicSignificance);
        if (strategic is null)
        {
            issues.Add(string.IsNullOrWhiteSpace(input.StrategicSignificance)
                ? "Strategic significance is missing."
                : $"'{input.StrategicSignificance}' is not a strategic significance option.");
        }

        if (issues.Count > 0 || habitat is null || strategic is null || condition is null)
        {
            return NeedsInfo(issues, habitat, input.AreaHectares);
        }

        // Inputs (columns G, P, Q).
        var areaG = input.AreaHectares;
        var advanceP = ToYearsCell(Math.Max(input.YearOffset, 0));
        var delayQ = ToYearsCell(Math.Max(-input.YearOffset, 0));

        // H, I — distinctiveness.
        var distinctivenessH = habitat.Distinctiveness;
        var scoreI = catalog.DistinctivenessScores.TryGetValue(distinctivenessH, out var distinctivenessScore)
            ? distinctivenessScore
            : habitat.DistinctivenessScore;

        // K — condition score (a number, or "Not Possible ▲").
        var conditionScoreK = habitat.ConditionScores.GetValueOrDefault(condition);

        // M, N — strategic significance.
        var categoryM = strategic.Category;
        var multiplierN = strategic.Multiplier;

        // O — standard time to target condition; AE — time to Poor condition.
        var standardTimeO = habitat.TimeToTargetYears.GetValueOrDefault(condition);
        var timeToPoorAE = habitat.TimeToTargetYears.GetValueOrDefault("Poor");

        var statusR = ComputeTimeToTargetStatus(scoreI, standardTimeO, advanceP, delayQ, timeToPoorAE);
        var finalTimeS = ComputeFinalTimeToTarget(standardTimeO, advanceP, delayQ);

        // T — final time to target multiplier.
        BngCellValue timeMultiplierT;
        if (finalTimeS.Text == CheckDataMessage)
        {
            timeMultiplierT = BngCellValue.Of(CheckDataMessage);
        }
        else
        {
            timeMultiplierT = catalog.GetTemporalMultiplier(finalTimeS.ToString()) is { } temporal
                ? BngCellValue.Of(temporal)
                : BngCellValue.Empty;
        }

        // U, V, W, X — difficulty.
        var standardDifficultyU = habitat.CreationDifficulty;
        string appliedDifficultyV;
        if (statusR == ReachedTargetMessage)
        {
            appliedDifficultyV = LowDifficultyApplied;
        }
        else if (statusR == PoorThresholdMessage && !EnhancementDifficultyExclusions.Contains(habitat.Name))
        {
            appliedDifficultyV = EnhancementDifficultyApplied;
        }
        else
        {
            appliedDifficultyV = StandardDifficultyApplied;
        }

        string finalDifficultyW;
        if (appliedDifficultyV == StandardDifficultyApplied && Compare(standardTimeO, advanceP) > 0)
        {
            finalDifficultyW = standardDifficultyU;
        }
        else if (appliedDifficultyV == LowDifficultyApplied && Compare(advanceP, standardTimeO) >= 0)
        {
            finalDifficultyW = "Low";
        }
        else
        {
            finalDifficultyW = habitat.EnhancementDifficulty;
        }

        double? difficultyMultiplierX = catalog.DifficultyMultipliers.TryGetValue(finalDifficultyW, out var difficultyMultiplier)
            ? difficultyMultiplier
            : null;

        // Y — habitat units delivered = G × I × K × N × T × X (blank if any factor isn't a number).
        double? unitsY = conditionScoreK.Number is { } conditionScore
                         && timeMultiplierT.Number is { } timeMultiplier
                         && difficultyMultiplierX is { } difficultyValue
            ? areaG * scoreI * conditionScore * multiplierN * timeMultiplier * difficultyValue
            : null;

        var outputs = new[] { statusR, finalTimeS.ToString(), timeMultiplierT.ToString(), appliedDifficultyV, conditionScoreK.ToString() };
        if (outputs.Any(text => text.Contains('⚠') || text.Contains('▲')))
        {
            issues.Add(outputs.First(text => text.Contains('⚠') || text.Contains('▲')));
        }

        if (unitsY is null)
        {
            issues.Add("Habitat units could not be calculated for this combination.");
        }

        return new BngHabitatCreationResult(
            issues.Count == 0 ? BngCalculationOutcome.Calculated : BngCalculationOutcome.CheckData,
            issues,
            habitat.BroadHabitat ?? string.Empty,
            habitat.Description,
            FormatArea(areaG),
            distinctivenessH,
            FormatGeneral(scoreI),
            conditionScoreK.ToString(),
            categoryM,
            FormatGeneral(multiplierN),
            standardTimeO.ToString(),
            statusR,
            finalTimeS.ToString(),
            timeMultiplierT.Number is { } t ? t.ToString("0.000", CultureInfo.InvariantCulture) : timeMultiplierT.ToString(),
            standardDifficultyU,
            appliedDifficultyV,
            finalDifficultyW,
            difficultyMultiplierX is { } x ? FormatGeneral(x) : string.Empty,
            unitsY is { } units ? units.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
            unitsY);
    }

    /// <summary>Column R — "Standard or adjusted time to target condition".</summary>
    private static string ComputeTimeToTargetStatus(
        double scoreI, BngCellValue standardTimeO, BngCellValue advanceP, BngCellValue delayQ, BngCellValue timeToPoorAE)
    {
        // The sheet's first branch ("Error - both advance and delayed habitat creation ▲") can't
        // occur here: a single signed year offset is never both positive and negative.
        if (scoreI == 0)
        {
            return StandardTimeApplied;
        }

        if (Compare(standardTimeO, advanceP) <= 0)
        {
            return ReachedTargetMessage;
        }

        if (standardTimeO.IsEmpty)
        {
            return string.Empty;
        }

        var advanceIsPositive = Compare(advanceP, BngCellValue.Of(0)) > 0;
        if (advanceIsPositive && Compare(advanceP, timeToPoorAE) >= 0)
        {
            return PoorThresholdMessage;
        }

        if (advanceIsPositive && Compare(standardTimeO, advanceP) > 0)
        {
            return CreationInPlaceMessage;
        }

        if (Compare(delayQ, BngCellValue.Of(0)) > 0)
        {
            return DelayMessage;
        }

        return advanceIsPositive ? StartedOrInPlaceMessage : StandardTimeApplied;
    }

    /// <summary>Column S — "Final time to target condition (years)".</summary>
    private static BngCellValue ComputeFinalTimeToTarget(BngCellValue standardTimeO, BngCellValue advanceP, BngCellValue delayQ)
    {
        if (standardTimeO.Text == NotPossible)
        {
            return BngCellValue.Of(CheckDataMessage);
        }

        if (standardTimeO.IsEmpty)
        {
            return BngCellValue.Empty;
        }

        if (standardTimeO.Text == ThirtyPlus)
        {
            if (advanceP.Text is null && AsNumber(advanceP) == 0)
            {
                return BngCellValue.Of(ThirtyPlus);
            }

            if (advanceP.Text == ThirtyPlus)
            {
                return BngCellValue.Of(0);
            }

            // Numeric advance (the only case left, and always < 32 since the list stops at 30).
            return BngCellValue.Of(30 - AsNumber(advanceP));
        }

        var standard = standardTimeO.Number ?? 0;
        if (Compare(advanceP, standardTimeO) > 0)
        {
            return BngCellValue.Of(0);
        }

        if (delayQ.Text == ThirtyPlus)
        {
            return BngCellValue.Of(ThirtyPlus);
        }

        var advance = AsNumber(advanceP);
        var delay = AsNumber(delayQ);
        if (standard + delay > 30 || standard - advance > 30)
        {
            return BngCellValue.Of(ThirtyPlus);
        }

        if (standard + delay - advance > 0)
        {
            return BngCellValue.Of(standard + delay - advance);
        }

        return BngCellValue.Of(standard - advance > 0 ? standard - advance : standard + delay - advance);
    }

    private static BngHabitatCreationResult NeedsInfo(IReadOnlyList<string> issues, BngHabitat? habitat, double areaHectares) => new(
        BngCalculationOutcome.NeedsInfo,
        issues,
        habitat?.BroadHabitat ?? string.Empty,
        habitat?.Description ?? string.Empty,
        FormatArea(areaHectares),
        habitat?.Distinctiveness ?? string.Empty,
        habitat is null ? string.Empty : FormatGeneral(habitat.DistinctivenessScore),
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        habitat?.CreationDifficulty ?? string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty,
        null);

    /// <summary>A years cell as the P/Q dropdown would hold it: blank for 0, a number up to 30, or "30+".</summary>
    private static BngCellValue ToYearsCell(int years) => years switch
    {
        <= 0 => BngCellValue.Empty,
        > MaxListedYears => BngCellValue.Of(ThirtyPlus),
        _ => BngCellValue.Of(years)
    };

    private static double AsNumber(BngCellValue value) => value.Number ?? 0;

    /// <summary>
    /// Excel's comparison semantics for the value types the metric uses: a blank cell compares as
    /// 0 against a number and as "" against text, any text is greater than any number, and text
    /// compares case-insensitively.
    /// </summary>
    internal static int Compare(BngCellValue left, BngCellValue right)
    {
        var leftIsText = left.Text is not null;
        var rightIsText = right.Text is not null;

        if (left.IsEmpty && right.IsEmpty)
        {
            return 0;
        }

        if (left.IsEmpty)
        {
            return rightIsText ? string.Compare(string.Empty, right.Text, StringComparison.OrdinalIgnoreCase) : 0.0.CompareTo(right.Number!.Value);
        }

        if (right.IsEmpty)
        {
            return leftIsText ? string.Compare(left.Text, string.Empty, StringComparison.OrdinalIgnoreCase) : left.Number!.Value.CompareTo(0.0);
        }

        return (leftIsText, rightIsText) switch
        {
            (false, false) => left.Number!.Value.CompareTo(right.Number!.Value),
            (true, false) => 1,
            (false, true) => -1,
            _ => string.Compare(left.Text, right.Text, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string FormatGeneral(double value) => BngCellValue.Of(value).ToString();

    private static string FormatArea(double hectares) => hectares.ToString("0.####", CultureInfo.InvariantCulture);
}
