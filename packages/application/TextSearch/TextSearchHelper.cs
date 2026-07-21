namespace PacToolkits.Application.TextSearch;

/// <summary>
/// 文本/拼音检索辅助（匹配判定与拼音查询形态识别）
/// </summary>
public static class TextSearchHelper
{
    public const int DefaultMaxPinyinExactMatches = 25;

    public static bool Matches(string? keyword, string? text)
        => PinyinInitialMatcher.IsMatch(keyword, text);

    public static bool MatchesAny(string? keyword, params string?[] texts)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        foreach (var text in texts)
        {
            if (Matches(keyword, text))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikePinyinQuery(string? keyword)
    {
        var text = (keyword ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public static string[] FindPinyinExactMatches(
        string? keyword,
        IReadOnlyList<string> catalogTexts,
        int maxMatches = DefaultMaxPinyinExactMatches)
    {
        var token = (keyword ?? string.Empty).Trim();
        if (token.Length == 0 || !LooksLikePinyinQuery(token) || maxMatches <= 0)
        {
            return [];
        }

        var matches = new List<string>(Math.Min(maxMatches, 8));
        foreach (var text in catalogTexts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!PinyinInitialMatcher.IsMatch(token, text))
            {
                continue;
            }

            matches.Add(text);
            if (matches.Count >= maxMatches)
            {
                break;
            }
        }

        return matches.ToArray();
    }

    public static string[][]? BuildPinyinExactPerToken(string? keyword, IReadOnlyList<string> catalogTexts)
    {
        var tokens = SplitTokens(keyword);
        if (tokens.Length == 0)
        {
            return null;
        }

        var result = new string[tokens.Length][];
        var hasAny = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            result[i] = FindPinyinExactMatches(tokens[i], catalogTexts);
            if (result[i].Length > 0)
            {
                hasAny = true;
            }
        }

        return hasAny ? result : null;
    }

    private static string[] SplitTokens(string? keyword)
    {
        var text = (keyword ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return [];
        }

        return text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
