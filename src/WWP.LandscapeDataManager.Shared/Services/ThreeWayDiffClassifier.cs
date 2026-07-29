namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// The audit tool's core diff rule: compares the value last recorded as synced against the
/// current Revit value and the latest source value. Whether a field is safe to auto-apply, or
/// needs a user decision, follows directly from which of the three values agree.
/// </summary>
public static class ThreeWayDiffClassifier
{
    /// <param name="previous">The value recorded after the last successful sync, or null if this field has never been synced before.</param>
    /// <param name="current">The value currently in Revit.</param>
    /// <param name="latest">The value in the latest pull from the source.</param>
    public static ThreeWayDiffResult Classify(string? previous, string current, string latest)
    {
        if (previous is null)
        {
            // Never synced before: nothing to compare a manual edit against, so this can only be
            // "still matches the source" or "needs the first sync" — never a conflict.
            return string.Equals(current, latest, StringComparison.Ordinal)
                ? new ThreeWayDiffResult("Unchanged", false)
                : new ThreeWayDiffResult("Changed", true);
        }

        var currentMatchesPrevious = string.Equals(current, previous, StringComparison.Ordinal);
        var latestMatchesPrevious = string.Equals(latest, previous, StringComparison.Ordinal);

        if (currentMatchesPrevious && latestMatchesPrevious)
        {
            return new ThreeWayDiffResult("Unchanged", false);
        }

        if (currentMatchesPrevious && !latestMatchesPrevious)
        {
            // Revit still holds what we last synced; only the source moved. Safe to auto-apply.
            return new ThreeWayDiffResult("Changed", true);
        }

        if (!currentMatchesPrevious && latestMatchesPrevious)
        {
            // Revit was edited by hand since the last sync; the source didn't move. A silent
            // re-apply would discard that manual edit, so this needs an explicit user decision.
            return new ThreeWayDiffResult("Conflict", false);
        }

        // Both Revit and the source diverged from what was last synced.
        return string.Equals(current, latest, StringComparison.Ordinal)
            ? new ThreeWayDiffResult("Unchanged", false) // they coincidentally landed on the same value
            : new ThreeWayDiffResult("Conflict", false);
    }
}

/// <summary>Category is one of: "Unchanged", "Changed", "Conflict".</summary>
public sealed record ThreeWayDiffResult(string Category, bool SafeToAutoApply);
