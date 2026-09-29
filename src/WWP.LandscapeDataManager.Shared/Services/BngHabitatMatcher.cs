namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>
/// Best-effort guess of a Floor's BNG habitat from its family/type name, so the BNG tab only
/// needs manual picks for whatever this can't resolve. Scored the same way as Floor Calculator's
/// landscape data sheet matcher (exact, then containment, then token overlap, with punctuation
/// treated as word boundaries so "WWP_Mixed_Scrub" tokenizes like "Mixed scrub"). A guess is
/// only ever a starting point: the tab marks it Auto-matched until someone confirms it.
/// </summary>
public static class BngHabitatMatcher
{
    private const double MatchThreshold = 0.5;
    private const double ContainmentScore = 0.85;

    /// <summary>
    /// Common landscape-drawing words that never appear in a metric habitat name, mapped to the
    /// habitat they usually mean in a UK landscape scheme. Matched as whole words.
    /// </summary>
    private static readonly (string Keyword, string Habitat)[] Aliases =
    [
        ("lawn", "Modified grassland"),
        ("turf", "Modified grassland"),
        ("amenity grass", "Modified grassland"),
        ("wildflower", "Other neutral grassland"),
        ("meadow", "Other neutral grassland"),
        ("paving", "Developed land; sealed surface"),
        ("asphalt", "Developed land; sealed surface"),
        ("tarmac", "Developed land; sealed surface"),
        ("concrete", "Developed land; sealed surface"),
        ("hardstanding", "Developed land; sealed surface"),
        ("gravel", "Artificial unvegetated, unsealed surface"),
        ("pond", "Ponds (non-priority habitat)"),
        ("green roof", "Other green roof")
    ];

    public static BngHabitat? FindBestMatch(string familyName, string typeName, BngMetricCatalog catalog)
    {
        var candidates = catalog.CreatableHabitats.ToList();
        var (familyMatch, familyScore) = FindBestMatch(familyName, candidates, catalog);
        var (typeMatch, typeScore) = FindBestMatch(typeName, candidates, catalog);
        return typeScore > familyScore ? typeMatch : familyMatch;
    }

    private static (BngHabitat? Habitat, double Score) FindBestMatch(
        string candidateName, IReadOnlyList<BngHabitat> habitats, BngMetricCatalog catalog)
    {
        var normalizedCandidate = Normalize(candidateName);
        if (normalizedCandidate.Length == 0)
        {
            return (null, 0);
        }

        var candidateTokens = Tokenize(normalizedCandidate);

        BngHabitat? best = null;
        var bestScore = 0.0;
        foreach (var habitat in habitats)
        {
            var score = Math.Max(
                ScoreAgainst(normalizedCandidate, candidateTokens, habitat.Name),
                ScoreAgainst(normalizedCandidate, candidateTokens, habitat.Description));
            if (score > bestScore)
            {
                bestScore = score;
                best = habitat;
            }
        }

        if (bestScore < MatchThreshold)
        {
            var padded = $" {normalizedCandidate} ";
            foreach (var (keyword, habitatName) in Aliases)
            {
                if (padded.Contains($" {keyword} ", StringComparison.Ordinal) && catalog.FindHabitat(habitatName) is { CanBeCreated: true } aliased)
                {
                    return (aliased, MatchThreshold);
                }
            }
        }

        return bestScore >= MatchThreshold ? (best, bestScore) : (null, 0);
    }

    private static double ScoreAgainst(string normalizedCandidate, HashSet<string> candidateTokens, string fieldValue)
    {
        var normalizedField = Normalize(fieldValue);
        if (normalizedField.Length == 0)
        {
            return 0;
        }

        if (normalizedCandidate == normalizedField)
        {
            return 1.0;
        }

        if (normalizedCandidate.Contains(normalizedField, StringComparison.Ordinal) ||
            normalizedField.Contains(normalizedCandidate, StringComparison.Ordinal))
        {
            var shorter = Math.Min(normalizedCandidate.Length, normalizedField.Length);
            var longer = Math.Max(normalizedCandidate.Length, normalizedField.Length);
            return ContainmentScore * shorter / longer;
        }

        var fieldTokens = Tokenize(normalizedField);
        var intersection = candidateTokens.Intersect(fieldTokens).Count();
        var union = candidateTokens.Union(fieldTokens).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string normalizedText) =>
        normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    /// <summary>Lower-cases and turns punctuation into single spaces, so tokens and containment line up.</summary>
    private static string Normalize(string text) =>
        string.Join(' ', new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
