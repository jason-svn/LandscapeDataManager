using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.TreeSearcher;

/// <summary>
/// Best-effort automatic match from a Revit family/type name to a cached i-Tree species record —
/// tried first so Tree Searcher only needs manual search for whatever this can't confidently
/// resolve. Family name is weighted first (Revit tree families are usually named after the
/// species itself; the type name is often just a caliper/height variant), falling back to the
/// type name only if the family name alone scores nothing.
/// </summary>
internal static class SpeciesFuzzyMatcher
{
    private const double MatchThreshold = 0.55;
    private const double ContainmentScore = 0.85;

    public static SpeciesCatalogueRecord? FindBestMatch(
        string familyName, string typeName, IReadOnlyList<SpeciesCatalogueRecord> catalogue)
    {
        var best = FindBestMatch(familyName, catalogue);
        return best ?? FindBestMatch(typeName, catalogue);
    }

    private static SpeciesCatalogueRecord? FindBestMatch(string candidateName, IReadOnlyList<SpeciesCatalogueRecord> catalogue)
    {
        var normalizedCandidate = Normalize(candidateName);
        if (normalizedCandidate.Length == 0)
        {
            return null;
        }

        var candidateTokens = Tokenize(normalizedCandidate);

        SpeciesCatalogueRecord? best = null;
        var bestScore = 0.0;

        foreach (var record in catalogue)
        {
            var score = Math.Max(
                ScoreAgainst(normalizedCandidate, candidateTokens, record.CommonName),
                ScoreAgainst(normalizedCandidate, candidateTokens, record.ScientificName));

            if (score > bestScore)
            {
                bestScore = score;
                best = record;
            }
        }

        return bestScore >= MatchThreshold ? best : null;
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

        return JaccardSimilarity(candidateTokens, Tokenize(normalizedField));
    }

    private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string normalizedText) =>
        normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    private static string Normalize(string text) =>
        new(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
}
