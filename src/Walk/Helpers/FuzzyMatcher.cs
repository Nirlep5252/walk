namespace Walk.Helpers;

public readonly record struct FuzzyMatchResult(bool IsMatch, double Score);

public static class FuzzyMatcher
{
    public static FuzzyMatchResult Match(string query, string target)
    {
        if (string.IsNullOrEmpty(query))
            return new FuzzyMatchResult(true, 0.0);

        // Exact match — zero allocations
        if (query.Equals(target, StringComparison.OrdinalIgnoreCase))
            return new FuzzyMatchResult(true, 1.0);

        // Prefix match — zero allocations
        if (target.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return new FuzzyMatchResult(true, 0.9 + (0.1 * query.Length / target.Length));

        // Contains (substring) match — zero allocations
        if (target.Contains(query, StringComparison.OrdinalIgnoreCase))
            return new FuzzyMatchResult(true, 0.6 + (0.1 * query.Length / target.Length));

        // Subsequence match: every char in query appears in target in order
        // Uses char.ToLowerInvariant per-character to avoid string allocations
        int qi = 0;
        int consecutiveBonus = 0;
        int lastMatchIndex = -2;
        char currentQueryChar = char.ToLowerInvariant(query[0]);

        for (int ti = 0; ti < target.Length && qi < query.Length; ti++)
        {
            if (char.ToLowerInvariant(target[ti]) == currentQueryChar)
            {
                if (ti == lastMatchIndex + 1)
                    consecutiveBonus++;
                lastMatchIndex = ti;
                qi++;
                if (qi < query.Length)
                    currentQueryChar = char.ToLowerInvariant(query[qi]);
            }
        }

        if (qi == query.Length)
        {
            double baseScore = 0.3 * query.Length / target.Length;
            double bonus = 0.1 * consecutiveBonus / query.Length;
            return new FuzzyMatchResult(true, Math.Min(0.59, baseScore + bonus));
        }

        return new FuzzyMatchResult(false, 0.0);
    }
}
