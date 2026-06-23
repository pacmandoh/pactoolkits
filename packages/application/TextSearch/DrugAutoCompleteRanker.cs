namespace PacToolkits.Application.TextSearch;

public static class DrugAutoCompleteRanker
{
    public static List<T> FilterAndSort<T>(
        IReadOnlyList<T> catalog,
        string? searchText,
        Func<T, string> rawSelector,
        Func<T, string> displaySelector)
    {
        var query = NormalizeSearch(searchText);
        if (query.Length == 0)
        {
            return catalog.Count == 0 ? [] : [.. catalog];
        }

        var ranked = new List<(T Item, int Score, string Raw)>(catalog.Count);
        foreach (var item in catalog)
        {
            var raw = rawSelector(item) ?? string.Empty;
            var display = displaySelector(item) ?? string.Empty;
            var score = ScoreCandidate(query, raw, display);
            if (score >= 0)
            {
                ranked.Add((item, score, raw));
            }
        }

        ranked.Sort(static (left, right) =>
        {
            var byScore = right.Score.CompareTo(left.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            return string.Compare(left.Raw, right.Raw, StringComparison.Ordinal);
        });

        var result = new List<T>(ranked.Count);
        foreach (var entry in ranked)
        {
            result.Add(entry.Item);
        }

        return result;
    }

    internal static int ScoreCandidate(string query, string raw, string display)
    {
        var best = -1;
        best = Math.Max(best, PinyinInitialMatcher.Score(query, raw));
        best = Math.Max(best, PinyinInitialMatcher.Score(query, display));
        return best;
    }

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Replace(" ", string.Empty);
    }
}
