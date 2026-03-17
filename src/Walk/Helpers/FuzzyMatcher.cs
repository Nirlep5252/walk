using System.Text;

namespace Walk.Helpers;

public readonly record struct FuzzyMatchResult(bool IsMatch, double Score);

public static class FuzzyMatcher
{
    public static FuzzyMatchResult Match(string query, string target)
    {
        if (string.IsNullOrEmpty(query))
            return new FuzzyMatchResult(true, 0.0);

        if (string.IsNullOrEmpty(target))
            return new FuzzyMatchResult(false, 0.0);

        // Exact match keeps the hot path allocation-free.
        if (query.Equals(target, StringComparison.OrdinalIgnoreCase))
            return new FuzzyMatchResult(true, 1.0);

        if (target.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return new FuzzyMatchResult(true, 0.9 + (0.1 * query.Length / target.Length));

        if (target.Contains(query, StringComparison.OrdinalIgnoreCase))
            return new FuzzyMatchResult(true, 0.6 + (0.1 * query.Length / target.Length));

        var subsequenceScore = GetSubsequenceScore(query, target);
        var typoScore = GetTypoScore(query, target);
        var bestScore = Math.Max(subsequenceScore, typoScore);

        return bestScore > 0.0
            ? new FuzzyMatchResult(true, bestScore)
            : new FuzzyMatchResult(false, 0.0);
    }

    private static double GetSubsequenceScore(string query, string target)
    {
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

        if (qi != query.Length)
            return 0.0;

        double baseScore = 0.3 * query.Length / target.Length;
        double bonus = 0.1 * consecutiveBonus / query.Length;
        return Math.Min(0.59, baseScore + bonus);
    }

    private static double GetTypoScore(string query, string target)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return 0.0;

        var bestScore = 0.0;
        foreach (var candidate in GetCandidates(target))
        {
            var similarity = GetSimilarity(normalizedQuery, candidate);
            if (similarity is null)
                continue;

            var score = Math.Min(0.74, 0.25 + (similarity.Value * 0.35));
            bestScore = Math.Max(bestScore, score);
        }

        return bestScore;
    }

    private static List<string> GetCandidates(string target)
    {
        var candidates = new List<string>();
        var compact = Normalize(target);
        if (compact.Length > 0)
            candidates.Add(compact);

        foreach (var token in Tokenize(target))
        {
            if (!candidates.Contains(token, StringComparer.Ordinal))
                candidates.Add(token);
        }

        return candidates;
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static double? GetSimilarity(string query, string candidate)
    {
        if (candidate.Length == 0)
            return null;

        if (query.Length > candidate.Length &&
            query.StartsWith(candidate, StringComparison.Ordinal))
        {
            return null;
        }

        var maxDistance = GetMaxDistance(query.Length, candidate.Length);
        if (Math.Abs(query.Length - candidate.Length) > maxDistance)
            return null;

        var distance = GetOptimalStringAlignmentDistance(query, candidate, maxDistance);
        if (distance is null)
            return null;

        var maxLength = Math.Max(query.Length, candidate.Length);
        if (maxLength == 0)
            return 1.0;

        var similarity = 1.0 - (distance.Value / (double)maxLength);
        return similarity >= 0.7 ? similarity : null;
    }

    private static int GetMaxDistance(int queryLength, int candidateLength)
    {
        var shortest = Math.Min(queryLength, candidateLength);
        return shortest switch
        {
            <= 4 => 1,
            <= 8 => 2,
            _ => 3,
        };
    }

    private static int? GetOptimalStringAlignmentDistance(string source, string target, int maxDistance)
    {
        var previousPrevious = new int[target.Length + 1];
        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (int j = 0; j <= target.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];

            for (int j = 1; j <= target.Length; j++)
            {
                var substitutionCost = source[i - 1] == target[j - 1] ? 0 : 1;
                var insertion = current[j - 1] + 1;
                var deletion = previous[j] + 1;
                var substitution = previous[j - 1] + substitutionCost;

                var value = Math.Min(Math.Min(insertion, deletion), substitution);

                if (i > 1 &&
                    j > 1 &&
                    source[i - 1] == target[j - 2] &&
                    source[i - 2] == target[j - 1])
                {
                    value = Math.Min(value, previousPrevious[j - 2] + 1);
                }

                current[j] = value;
                rowMinimum = Math.Min(rowMinimum, value);
            }

            if (rowMinimum > maxDistance)
                return null;

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        var distance = previous[target.Length];
        return distance <= maxDistance ? distance : null;
    }
}
