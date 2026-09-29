using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>The row states Floor Calculator's BNG tab filters by (one status card each).</summary>
public static class BngFloorStatus
{
    /// <summary>Written to Revit, and the floor's inputs/area haven't changed since.</summary>
    public const string Calculated = "Calculated";

    /// <summary>Complete and calculated in the tool, but not yet written to Revit.</summary>
    public const string Ready = "Ready";

    /// <summary>Habitat was guessed from the floor's family/type name and hasn't been confirmed.</summary>
    public const string AutoMatched = "AutoMatched";

    /// <summary>Habitat, condition or strategic significance is missing or not a metric option.</summary>
    public const string NeedsInfo = "NeedsInfo";

    /// <summary>Calculated, but the metric raises a "Check details ⚠" / "Check Data ⚠" flag.</summary>
    public const string CheckData = "CheckData";

    /// <summary>Written before, but the floor's inputs or area have changed since.</summary>
    public const string Stale = "Stale";

    /// <summary>The last write to Revit failed for this floor.</summary>
    public const string Failed = "Failed";
}

public static class BngFloorStatusEvaluator
{
    public const double SquareMetresPerHectare = 10_000d;

    /// <summary>
    /// Picks a row's single status, most urgent first: a failed write, then an unconfirmed
    /// auto-match (ahead of missing inputs, so every guess stays findable under its own card even
    /// before condition etc. are filled in), then missing inputs, then a metric warning; otherwise
    /// whether Revit's stored result is current (<see cref="BngFloorStatus.Calculated"/>), outdated (<see cref="BngFloorStatus.Stale"/>),
    /// or was never written (<see cref="BngFloorStatus.Ready"/>).
    /// </summary>
    public static string Evaluate(
        BngHabitatCreationResult result,
        bool habitatAutoMatched,
        string currentSignature,
        string? storedSignature,
        bool writeFailed) =>
        Evaluate(result.Outcome, habitatAutoMatched, currentSignature, storedSignature, writeFailed);

    /// <summary>Same as the overload above, for any sheet's outcome (A-1, A-2 or A-3 — see <see cref="BngFloorCalculator"/>).</summary>
    public static string Evaluate(
        BngCalculationOutcome outcome,
        bool habitatAutoMatched,
        string currentSignature,
        string? storedSignature,
        bool writeFailed)
    {
        if (writeFailed)
        {
            return BngFloorStatus.Failed;
        }

        if (habitatAutoMatched)
        {
            return BngFloorStatus.AutoMatched;
        }

        if (outcome == BngCalculationOutcome.NeedsInfo)
        {
            return BngFloorStatus.NeedsInfo;
        }

        if (outcome == BngCalculationOutcome.CheckData)
        {
            return BngFloorStatus.CheckData;
        }

        if (string.IsNullOrEmpty(storedSignature))
        {
            return BngFloorStatus.Ready;
        }

        return string.Equals(storedSignature, currentSignature, StringComparison.Ordinal)
            ? BngFloorStatus.Calculated
            : BngFloorStatus.Stale;
    }

    /// <summary>
    /// A stable signature of every input that affects a floor's BNG result — the same idea as
    /// <see cref="InputSignatureCalculator"/> for i-Tree. Written with the result, so a later load
    /// can tell a floor whose area or inputs changed since (Stale) from one that's current.
    /// </summary>
    public static string ComputeSignature(BngHabitatCreationInput input, BngMetricCatalog catalog)
    {
        var habitat = catalog.FindHabitat(input.Habitat)?.Description ?? input.Habitat?.Trim() ?? string.Empty;
        var text = string.Join('|',
            habitat.ToUpperInvariant(),
            (input.Condition ?? string.Empty).Trim().ToUpperInvariant(),
            (input.StrategicSignificance ?? string.Empty).Trim().ToUpperInvariant(),
            input.YearOffset.ToString(CultureInfo.InvariantCulture),
            input.AreaHectares.ToString("F6", CultureInfo.InvariantCulture),
            catalog.DisplayVersion);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
