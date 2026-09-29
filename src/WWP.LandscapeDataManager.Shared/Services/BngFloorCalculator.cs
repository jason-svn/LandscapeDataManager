using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Everything that decides one floor's BNG result. <paramref name="PhaseRole"/> comes from Revit's
/// phases (<see cref="BngPhaseRoles"/>); <see cref="Role"/> upgrades a retained floor to Enhanced
/// when <paramref name="Enhanced"/> is set. Baseline* are the A-1 inputs of an Existing-phase
/// floor; ProposedHabitat/Condition/YearOffset are the A-2 inputs of a new floor, or the A-3
/// target of an enhanced one.
/// </summary>
public sealed record BngFloorInput(
    string PhaseRole,
    bool Enhanced,
    string? BaselineHabitat,
    string? BaselineCondition,
    string? Irreplaceable,
    string? ProposedHabitat,
    string? Condition,
    string? StrategicSignificance,
    int YearOffset,
    double AreaHectares)
{
    public string Role => PhaseRole == BngPhaseRoles.Retained && Enhanced ? BngPhaseRoles.Enhanced : PhaseRole;

    /// <summary>True for floors that were on site at baseline (A-1): retained, enhanced or lost.</summary>
    public bool IsBaseline => Role is BngPhaseRoles.Retained or BngPhaseRoles.Enhanced or BngPhaseRoles.Lost;

    /// <summary>True for floors whose proposed habitat inputs matter: new (A-2) and enhanced (A-3).</summary>
    public bool UsesProposedHabitat => Role is BngPhaseRoles.Created or BngPhaseRoles.Enhanced;
}

/// <summary>
/// One floor's BNG outcome. The A-2-shaped display columns (<see cref="BroadHabitat"/> …
/// <see cref="DifficultyMultiplier"/>) describe the floor's post-intervention habitat — the A-2 row
/// for a new floor, the A-3 row for an enhanced one, and the A-1 row for a retained or lost floor
/// (no time/difficulty). <see cref="HabitatUnits"/> is its post-intervention contribution (A-2 Y,
/// A-3 AN, A-1 U, or 0 when lost); <see cref="BaselineUnits"/> its A-1 Q (0 for a new floor).
/// </summary>
public sealed record BngFloorResult(
    string Role,
    BngCalculationOutcome Outcome,
    IReadOnlyList<string> Issues,
    string BroadHabitat,
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
    double? HabitatUnits,
    string BaselineUnitsText,
    double? BaselineUnits,
    BngBaselineResult? Baseline);

public static class BngFloorCalculator
{
    public static BngFloorResult Calculate(BngFloorInput input, BngMetricCatalog catalog)
    {
        switch (input.Role)
        {
            case BngPhaseRoles.Created:
                return FromCreation(BngHabitatCreationCalculator.Calculate(ToCreationInput(input), catalog));

            case BngPhaseRoles.Excluded:
                return new BngFloorResult(
                    BngPhaseRoles.Excluded, BngCalculationOutcome.Calculated,
                    ["Demolished in the phase it was created — not counted in the baseline or after development."],
                    string.Empty, FormatArea(input.AreaHectares), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    "0.00", 0d, "0.00", 0d, null);
        }

        var fate = input.Role switch
        {
            BngPhaseRoles.Enhanced => BngBaselineFate.Enhanced,
            BngPhaseRoles.Lost => BngBaselineFate.Lost,
            _ => BngBaselineFate.Retained
        };
        var baseline = BngHabitatBaselineCalculator.Calculate(
            new BngBaselineInput(input.BaselineHabitat, input.Irreplaceable, input.BaselineCondition, input.StrategicSignificance, input.AreaHectares, fate),
            catalog);

        if (fate == BngBaselineFate.Enhanced)
        {
            var enhancement = BngHabitatEnhancementCalculator.Calculate(
                baseline, new BngEnhancementInput(input.ProposedHabitat, input.Condition, input.StrategicSignificance, input.YearOffset), catalog);
            var issues = baseline.Issues.Concat(enhancement.Issues).Distinct(StringComparer.Ordinal).ToList();
            return new BngFloorResult(
                BngPhaseRoles.Enhanced,
                Worst(baseline.Outcome, enhancement.Outcome),
                issues,
                enhancement.BroadHabitat,
                FormatArea(input.AreaHectares),
                enhancement.Distinctiveness,
                enhancement.DistinctivenessScore,
                enhancement.ConditionScore,
                enhancement.StrategicSignificance,
                enhancement.StrategicSignificanceMultiplier,
                enhancement.StandardTimeToTarget,
                enhancement.TimeToTargetStatus,
                enhancement.FinalTimeToTarget,
                enhancement.FinalTimeToTargetMultiplier,
                enhancement.StandardDifficulty,
                enhancement.AppliedDifficulty,
                enhancement.FinalDifficulty,
                enhancement.DifficultyMultiplier,
                enhancement.HabitatUnitsText,
                enhancement.HabitatUnits,
                baseline.TotalHabitatUnitsText,
                baseline.BaselineUnits,
                baseline);
        }

        // Retained or lost: the A-1 row is the whole story — its post-intervention contribution is
        // column U (units retained), which is 0 for a lost floor since nothing of it is retained.
        return new BngFloorResult(
            input.Role,
            baseline.Outcome,
            baseline.Issues,
            baseline.BroadHabitat,
            FormatArea(input.AreaHectares),
            baseline.Distinctiveness,
            baseline.DistinctivenessScore,
            baseline.ConditionScore,
            baseline.StrategicSignificance,
            baseline.StrategicSignificanceMultiplier,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            baseline.UnitsRetainedText,
            baseline.RetainedUnits,
            baseline.TotalHabitatUnitsText,
            baseline.BaselineUnits,
            baseline);
    }

    /// <summary>
    /// A stable signature of every input that affects the floor's result, written with it so a later
    /// load can tell a current result from a Stale one. New floors keep the original A-2 signature
    /// (<see cref="BngFloorStatusEvaluator.ComputeSignature"/>), so results written before phases
    /// were considered stay current.
    /// </summary>
    public static string ComputeSignature(BngFloorInput input, BngMetricCatalog catalog)
    {
        if (input.Role == BngPhaseRoles.Created)
        {
            return BngFloorStatusEvaluator.ComputeSignature(ToCreationInput(input), catalog);
        }

        static string Normalize(string? text) => (text ?? string.Empty).Trim().ToUpperInvariant();
        string Habitat(string? name) => Normalize(catalog.FindHabitat(name)?.Description ?? name);

        var parts = new List<string>
        {
            input.Role,
            Habitat(input.BaselineHabitat),
            Normalize(input.BaselineCondition),
            Normalize(input.Irreplaceable),
            Normalize(catalog.FindStrategicSignificance(input.StrategicSignificance)?.Description ?? input.StrategicSignificance),
            input.AreaHectares.ToString("F6", CultureInfo.InvariantCulture),
            catalog.DisplayVersion
        };
        if (input.Role == BngPhaseRoles.Enhanced)
        {
            parts.Add(Habitat(input.ProposedHabitat));
            parts.Add(Normalize(input.Condition));
            parts.Add(input.YearOffset.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts))));
    }

    private static BngHabitatCreationInput ToCreationInput(BngFloorInput input) =>
        new(input.ProposedHabitat, input.Condition, input.StrategicSignificance, input.YearOffset, input.AreaHectares);

    private static BngFloorResult FromCreation(BngHabitatCreationResult result) => new(
        BngPhaseRoles.Created,
        result.Outcome,
        result.Issues,
        result.BroadHabitat,
        result.AreaHectares,
        result.Distinctiveness,
        result.DistinctivenessScore,
        result.ConditionScore,
        result.StrategicSignificance,
        result.StrategicSignificanceMultiplier,
        result.StandardTimeToTarget,
        result.TimeToTargetStatus,
        result.FinalTimeToTarget,
        result.FinalTimeToTargetMultiplier,
        result.StandardDifficulty,
        result.AppliedDifficulty,
        result.FinalDifficulty,
        result.DifficultyMultiplier,
        result.HabitatUnitsText,
        result.HabitatUnits,
        "0.00",
        0d,
        null);

    private static BngCalculationOutcome Worst(BngCalculationOutcome left, BngCalculationOutcome right) =>
        left == BngCalculationOutcome.NeedsInfo || right == BngCalculationOutcome.NeedsInfo ? BngCalculationOutcome.NeedsInfo
        : left == BngCalculationOutcome.CheckData || right == BngCalculationOutcome.CheckData ? BngCalculationOutcome.CheckData
        : BngCalculationOutcome.Calculated;

    private static string FormatArea(double hectares) => hectares.ToString("0.####", CultureInfo.InvariantCulture);
}
