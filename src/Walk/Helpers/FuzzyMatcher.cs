namespace Walk.Helpers;

public readonly record struct FuzzyMatchResult(bool IsMatch, double Score);

public static class FuzzyMatcher
{
    public static FuzzyMatchResult Match(string query, string target)
    {
        if (string.IsNullOrEmpty(query))
            return new FuzzyMatchResult(true, 0.0);

        var queryLower = query.ToLowerInvariant();
        var targetLower = target.ToLowerInvariant();

        // Exact match
        if (queryLower == targetLower)
            return new FuzzyMatchResult(true, 1.0);

        // Prefix match
        if (targetLower.StartsWith(queryLower))
            return new FuzzyMatchResult(true, 0.9 + (0.1 * query.Length / target.Length));

        // Contains (substring) match
        if (targetLower.Contains(queryLower))
            return new FuzzyMatchResult(true, 0.6 + (0.1 * query.Length / target.Length));

        // Subsequence match: every char in query appears in target in order
        int qi = 0;
        int consecutiveBonus = 0;
        int lastMatchIndex = -2;

        for (int ti = 0; ti < targetLower.Length && qi < queryLower.Length; ti++)
        {
            if (targetLower[ti] == queryLower[qi])
            {
                if (ti == lastMatchIndex + 1)
                    consecutiveBonus++;
                lastMatchIndex = ti;
                qi++;
            }
        }

        if (qi == queryLower.Length)
        {
            double baseScore = 0.3 * query.Length / target.Length;
            double bonus = 0.1 * consecutiveBonus / query.Length;
            return new FuzzyMatchResult(true, Math.Min(0.59, baseScore + bonus));
        }

        return new FuzzyMatchResult(false, 0.0);
    }
}
