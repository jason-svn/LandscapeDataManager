using System.Globalization;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// What happens to a whole baseline floor, from its Revit phases: kept as-is (A-1 column S =
/// the whole area), kept and enhanced (column T = the whole area, calculated on in sheet A-3), or
/// demolished (neither, so the whole area is lost).
/// </summary>
public enum BngBaselineFate
{
    Retained,
    Enhanced,
    Lost
}

/// <summary>
/// One floor's inputs for the metric's A-1 On-Site Habitat Baseline row. <paramref name="Irreplaceable"/>
/// is column G ("Yes"/"No") — only a real choice for the habitats G-1 flags "Yes/No"; for the rest
/// the dropdown offers a single value, which is used when it is left blank.
/// </summary>
public sealed record BngBaselineInput(
    string? Habitat,
    string? Irreplaceable,
    string? Condition,
    string? StrategicSignificance,
    double AreaHectares,
    BngBaselineFate Fate);

/// <summary>
/// The A-1 row's calculated cells as the text Excel displays in each column, plus the unit totals
/// the Headline Results sheet sums (as numbers; null where the cell holds text, which SUM ignores).
/// </summary>
public sealed record BngBaselineResult(
    BngCalculationOutcome Outcome,
    IReadOnlyList<string> Issues,
    BngHabitat? Habitat,
    BngBaselineFate Fate,
    string BroadHabitat,
    string HabitatDescription,
    string Irreplaceable,
    string Condition,
    double AreaHectares,
    string Distinctiveness,
    string DistinctivenessScore,
    string ConditionScore,
    string StrategicSignificance,
    string StrategicSignificanceMultiplier,
    string TradingRule,
    string TotalHabitatUnitsText,
    string UnitsRetainedText,
    string UnitsEnhancedText,
    string AreaLostText,
    string UnitsLostText,
    double? BaselineUnits,
    double? RetainedUnits,
    double? LostUnits)
{
    /// <summary>The raw cells sheet A-3 reads back from this row (its columns H, I, K, M, N).</summary>
    internal BngCellValue DistinctivenessScoreCell { get; init; }
    internal BngCellValue ConditionScoreCell { get; init; }
    internal BngCellValue StrategicMultiplierCell { get; init; }
    internal BngCellValue TotalHabitatUnitsCell { get; init; }
}

/// <summary>
/// A literal port of the Statutory Biodiversity Metric's "A-1 On-Site Habitat Baseline" row
/// formulas (23.07.2024 release) for a whole floor that is retained, enhanced or lost — same
/// approach as <see cref="BngHabitatCreationCalculator"/>. Bespoke compensation (column Y) is
/// never entered, so it is always blank here. Column letters in comments refer to sheet A-1.
/// </summary>
public static class BngHabitatBaselineCalculator
{
    public const string ConfirmIrreplaceableMessage = "Confirm irreplaceable habitat status ▲";
    public const string IrreplaceableTreesMessage = "Error - you cannot enhance irreplaceable individual trees ▲";
    public const string AnyLossUnacceptableWarning = "Any Loss Unacceptable ⚠";
    public const string AnyLossUnacceptableError = "Any Loss Unacceptable ▲";
    public const string IrreplaceableNoUnits = "Irreplaceable habitat - no units generated ⚠";
    public const string BespokeCompensationLikely = "Bespoke compensation likely to be required";
    public const string CheckDataMessage = "Check Data ▲";
    private const string IndividualTrees = "Individual trees";

    public static BngBaselineResult Calculate(BngBaselineInput input, BngMetricCatalog catalog)
    {
        var issues = new List<string>();
        var habitat = catalog.FindHabitat(input.Habitat);
        if (habitat is null)
        {
            issues.Add(string.IsNullOrWhiteSpace(input.Habitat)
                ? "Baseline habitat is missing."
                : $"'{input.Habitat}' is not a habitat in {catalog.DisplayVersion}.");
        }
        else if (!habitat.CanBeBaseline)
        {
            issues.Add($"'{habitat.Name}' can't be used as a baseline habitat.");
        }

        // G — the dropdown only offers the habitat's own flag unless G-1 says "Yes/No".
        string? irreplaceable = null;
        if (habitat is not null)
        {
            var flag = habitat.Irreplaceable?.Trim() ?? "No";
            var entered = NormalizeYesNo(input.Irreplaceable);
            if (flag is "Yes" or "No")
            {
                irreplaceable = entered ?? flag;
                if (!string.Equals(irreplaceable, flag, StringComparison.Ordinal))
                {
                    issues.Add($"Irreplaceable must be '{flag}' for '{habitat.Name}'.");
                }
            }
            else if (entered is null)
            {
                issues.Add($"Irreplaceable habitat status (Yes/No) is missing — '{habitat.Name}' can be either.");
            }
            else
            {
                irreplaceable = entered;
            }
        }

        var condition = input.Condition?.Trim();
        if (string.IsNullOrEmpty(condition))
        {
            issues.Add("Baseline condition is missing.");
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

        if (issues.Count > 0 || habitat is null || strategic is null || condition is null || irreplaceable is null)
        {
            return NeedsInfo(issues, habitat, input);
        }

        // Inputs (E, H, S, T).
        var broadE = habitat.BaselineBroadHabitat ?? string.Empty;
        var areaH = input.AreaHectares;
        var retainedS = input.Fate == BngBaselineFate.Retained ? areaH : 0d;
        var enhancedT = input.Fate == BngBaselineFate.Enhanced ? areaH : 0d;
        var isIrreplaceable = irreplaceable == "Yes";

        // I, J — distinctiveness (G is constrained to the habitat's flag, so I never errors here).
        var distinctivenessI = habitat.Distinctiveness;
        BngCellValue scoreJ = catalog.DistinctivenessScores.TryGetValue(distinctivenessI, out var score)
            ? BngCellValue.Of(score)
            : BngCellValue.Of(string.Empty);

        // L — condition score; N, O — strategic significance.
        var conditionScoreL = habitat.ConditionScores.GetValueOrDefault(condition);
        var categoryN = strategic.Category;
        var multiplierO = strategic.Multiplier;

        // P — required action to meet trading rules.
        var tradingRuleP = isIrreplaceable ? BespokeCompensationLikely : habitat.TradingRule ?? string.Empty;

        double? Units(double area) => scoreJ.Number is { } j && conditionScoreL.Number is { } l ? area * j * l * multiplierO : null;

        // U, V — baseline units retained / enhanced (IFERROR(...,"") around area × J × L × O).
        BngCellValue retainedU = isIrreplaceable
            ? BngCellValue.Of(IrreplaceableNoUnits)
            : Units(retainedS) is { } u ? BngCellValue.Of(u) : BngCellValue.Of(string.Empty);
        BngCellValue enhancedV = broadE == IndividualTrees && enhancedT > 0 && isIrreplaceable
            ? BngCellValue.Of("Error - ▲")
            : Units(enhancedT) is { } v ? BngCellValue.Of(v) : BngCellValue.Of(string.Empty);

        // W — area lost.
        var areaLostW = areaH - retainedS - enhancedT;

        // Q — total habitat units. Branches that need bespoke compensation (column Y) answered, or
        // P = "Bespoke compensation likely" without G = "Yes" (impossible), are left out.
        BngCellValue totalQ;
        if (broadE == IndividualTrees && enhancedT > 0 && isIrreplaceable)
        {
            totalQ = BngCellValue.Of(IrreplaceableTreesMessage);
        }
        else if (retainedU.Text == IrreplaceableNoUnits && retainedS + enhancedT < areaH)
        {
            totalQ = BngCellValue.Of(AnyLossUnacceptableWarning);
        }
        else if (retainedU.Text == IrreplaceableNoUnits)
        {
            totalQ = Units(enhancedT) is { } q ? BngCellValue.Of(q) : BngCellValue.Of(CheckDataMessage);
        }
        else if (string.IsNullOrEmpty(tradingRuleP))
        {
            totalQ = BngCellValue.Of(string.Empty);
        }
        else
        {
            totalQ = Units(areaH) is { } q ? BngCellValue.Of(q) : BngCellValue.Of(CheckDataMessage);
        }

        // X — units lost.
        BngCellValue lostX;
        if (areaLostW == 0)
        {
            lostX = BngCellValue.Of(0);
        }
        else if (tradingRuleP == BespokeCompensationLikely)
        {
            lostX = BngCellValue.Of(AnyLossUnacceptableError);
        }
        else
        {
            lostX = totalQ.Number is { } total && AsArithmetic(retainedU) is { } retained && AsArithmetic(enhancedV) is { } enhanced
                ? BngCellValue.Of(total - retained - enhanced)
                : BngCellValue.Of(CheckDataMessage);
        }

        var outputs = new[] { totalQ.ToString(), retainedU.ToString(), enhancedV.ToString(), lostX.ToString(), conditionScoreL.ToString() };
        if (outputs.FirstOrDefault(text => text.Contains('⚠') || text.Contains('▲')) is { } flagged)
        {
            issues.Add(flagged);
        }

        if (totalQ.Number is null)
        {
            issues.Add("Baseline habitat units could not be calculated for this combination.");
        }

        return new BngBaselineResult(
            issues.Count == 0 ? BngCalculationOutcome.Calculated : BngCalculationOutcome.CheckData,
            issues,
            habitat,
            input.Fate,
            broadE,
            habitat.Description,
            irreplaceable,
            condition,
            areaH,
            distinctivenessI,
            scoreJ.ToString(),
            conditionScoreL.ToString(),
            categoryN,
            FormatGeneral(multiplierO),
            tradingRuleP,
            FormatUnits(totalQ),
            FormatUnits(retainedU),
            FormatUnits(enhancedV),
            FormatArea(areaLostW),
            FormatUnits(lostX),
            totalQ.Number,
            retainedU.Number,
            lostX.Number)
        {
            DistinctivenessScoreCell = scoreJ,
            ConditionScoreCell = conditionScoreL,
            StrategicMultiplierCell = BngCellValue.Of(multiplierO),
            TotalHabitatUnitsCell = totalQ
        };
    }

    private static BngBaselineResult NeedsInfo(IReadOnlyList<string> issues, BngHabitat? habitat, BngBaselineInput input) => new(
        BngCalculationOutcome.NeedsInfo,
        issues,
        habitat,
        input.Fate,
        habitat?.BaselineBroadHabitat ?? string.Empty,
        habitat?.Description ?? string.Empty,
        NormalizeYesNo(input.Irreplaceable) ?? string.Empty,
        input.Condition?.Trim() ?? string.Empty,
        input.AreaHectares,
        habitat?.Distinctiveness ?? string.Empty,
        habitat is null ? string.Empty : FormatGeneral(habitat.DistinctivenessScore),
        string.Empty, string.Empty, string.Empty,
        habitat?.TradingRule ?? string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        null, null, null);

    /// <summary>A cell used in arithmetic: blank counts as 0, a number as itself, and any text (including a formula's "") as #VALUE!.</summary>
    private static double? AsArithmetic(BngCellValue cell) =>
        cell.Number is { } number ? number : cell.Text is null ? 0d : null;

    private static string? NormalizeYesNo(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "YES" => "Yes",
        "NO" => "No",
        _ => null
    };

    private static string FormatUnits(BngCellValue cell) =>
        cell.Number is { } number ? number.ToString("0.00", CultureInfo.InvariantCulture) : cell.Text ?? string.Empty;

    private static string FormatGeneral(double value) => BngCellValue.Of(value).ToString();

    private static string FormatArea(double hectares) => hectares.ToString("0.####", CultureInfo.InvariantCulture);
}
