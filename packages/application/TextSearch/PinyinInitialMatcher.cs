using System.Collections.Concurrent;
using System.Text;
using ToolGood.Words.Pinyin;

namespace PacToolkits.Application.TextSearch;

/// <summary>
/// 拼音首字母匹配与分档打分（供自动完成排序）
/// </summary>
public static class PinyinInitialMatcher
{
    private sealed record InitialProfile(string Primary, char[][] Options);

    private static readonly ConcurrentDictionary<string, InitialProfile> ProfileCache =
        new(StringComparer.Ordinal);

    internal static string GetInitials(string candidate)
        => GetProfile(candidate).Primary;

    internal static char[][] GetInitialOptions(string candidate)
        => GetProfile(candidate).Options;

    public static bool IsMatch(string? searchText, string? candidate)
        => Score(searchText, candidate) >= 0;

    /// <summary>
    /// 拼音档分值：精确 490、前缀 480、连续包含 470、子序列 460
    /// 文本档由 <see cref="DrugAutoCompleteRanker"/> 单独处理
    /// </summary>
    public static int ScorePinyin(string? searchText, string? candidate)
    {
        var query = NormalizeSearch(searchText);
        if (query.Length == 0 || string.IsNullOrWhiteSpace(candidate))
        {
            return -1;
        }

        var profile = GetProfile(candidate);
        if (profile.Primary.Length == 0)
        {
            return -1;
        }

        var initials = profile.Primary;
        if (string.Equals(initials, query, StringComparison.OrdinalIgnoreCase))
        {
            return 490;
        }

        if (initials.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 480;
        }

        if (initials.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 470;
        }

        return IsSubsequenceMatch(query, profile.Options) ? 460 : -1;
    }

    public static int Score(string? searchText, string? candidate)
    {
        var query = NormalizeSearch(searchText);
        if (query.Length == 0)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return -1;
        }

        if (string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 800;
        }

        if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 600;
        }

        return ScorePinyin(searchText, candidate);
    }

    private static InitialProfile GetProfile(string candidate)
        => ProfileCache.GetOrAdd(candidate, BuildProfile);

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim().ToLowerInvariant();
        return s.Replace(" ", string.Empty);
    }

    private static InitialProfile BuildProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new InitialProfile(string.Empty, []);
        }

        var normalized = value.Trim();
        var options = new List<char[]>(normalized.Length);
        var primary = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                var letter = char.ToLowerInvariant(ch);
                options.Add([letter]);
                primary.Append(letter);
                continue;
            }

            var (primaryInitial, initials) = GetInitialChoices(ch);
            if (initials.Length == 0)
            {
                continue;
            }

            options.Add(initials);
            primary.Append(primaryInitial);
        }

        return new InitialProfile(primary.ToString(), options.ToArray());
    }

    private static (char Primary, char[] All) GetInitialChoices(char ch)
    {
        var readings = WordsHelper.GetAllPinyin(ch);
        if (readings.Count == 0)
        {
            return ('\0', []);
        }

        char? primary = null;
        var initials = new HashSet<char>();
        foreach (var reading in readings)
        {
            if (string.IsNullOrWhiteSpace(reading))
            {
                continue;
            }

            var initial = char.ToLowerInvariant(reading[0]);
            if (initial is < 'a' or > 'z')
            {
                continue;
            }

            primary ??= initial;
            initials.Add(initial);
        }

        if (initials.Count == 0 || primary is null)
        {
            return ('\0', []);
        }

        return (primary.Value, initials.OrderBy(static x => x).ToArray());
    }

    private static bool IsSubsequenceMatch(string query, char[][] options)
    {
        var queryIndex = 0;
        foreach (var choices in options)
        {
            if (queryIndex >= query.Length)
            {
                break;
            }

            if (choices.Contains(query[queryIndex]))
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }
}
