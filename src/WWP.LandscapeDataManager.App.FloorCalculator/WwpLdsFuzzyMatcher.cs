using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.FloorCalculator;

/// <summary>
/// Best-effort automatic match from a Revit Floor family/type name to a cached WWP landscape
/// data sheet row — tried first so Floor Calculator only needs manual search for whatever this
/// can't confidently resolve. Family name is scored first (Floor families are usually named
/// after the landscape/surface material itself), falling back to the type name only if the
/// family name alone scores nothing. Mirrors <see cref="WWP.LandscapeDataManager.App.TreeSearcher.SpeciesFuzzyMatcher"/>:
/// the best-scoring catalogue row wins instead of the first substring hit, and normalization
/// treats underscores (and other punctuation) as word boundaries rather than removing them, so
/// "WWP_Wetland" and "Wwp Wetland" tokenize identically without losing match score.
/// </summary>
internal static class WwpLdsFuzzyMatcher
{
    private const double MatchThreshold = 0.55;
    private const double ContainmentScore = 0.85;

    public static WwpLdsCoefficientRecord? FindBestMatch(
        string familyName, string typeName, IReadOnlyList<WwpLdsCoefficientRecord> catalogue)
    {
        var best = FindBestMatch(familyName, catalogue);
        return best ?? FindBestMatch(typeName, catalogue);
    }

    private static WwpLdsCoefficientRecord? FindBestMatch(string candidateName, IReadOnlyList<WwpLdsCoefficientRecord> catalogue)
    {
        var normalizedCandidate = Normalize(candidateName);
        if (normalizedCandidate.Length == 0)
        {
            return null;
        }

        var candidateTokens = Tokenize(normalizedCandidate);

        WwpLdsCoefficientRecord? best = null;
        var bestScore = 0.0;

        foreach (var record in catalogue)
        {
            var score = Math.Max(
                ScoreAgainst(normalizedCandidate, candidateTokens, record.PlantingTypeCode),
                Math.Max(
                    ScoreAgainst(normalizedCandidate, candidateTokens, record.SubCategory),
                    ScoreAgainst(normalizedCandidate, candidateTokens, record.TypeName)));

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
