using System;
using System.Collections.Concurrent;
using System.Text;

namespace pactoolkits_ui.Common;

public static class PinyinInitialMatcher
{
    private static readonly ConcurrentDictionary<string, string> InitialsCache =
        new(StringComparer.Ordinal);

    private static readonly int[] CodeBoundaries =
    {
        -20319, -20284, -19776, -19219, -18711, -18527, -18240, -17923,
        -17418, -16475, -16213, -15641, -15166, -14923, -14915, -14631,
        -14150, -14091, -13319, -12839, -12557, -11848, -11056, -10247
    };

    private static readonly char[] InitialChars =
    {
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h',
        'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q',
        'r', 's', 't', 'w', 'x', 'y', 'z'
    };

    private static readonly Encoding? Gb2312Encoding;

    static PinyinInitialMatcher()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gb2312Encoding = Encoding.GetEncoding("GB2312");
        }
        catch
        {
            Gb2312Encoding = null;
        }
    }

    public static bool IsMatch(string? searchText, string? candidate)
    {
        var query = NormalizeSearch(searchText);
        if (query.Length == 0)
            return true;

        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var initials = InitialsCache.GetOrAdd(candidate, BuildInitials);
        return initials.Length > 0 &&
               initials.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var s = value.Trim().ToLowerInvariant();
        return s.Replace(" ", string.Empty);
    }

    private static string BuildInitials(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (normalized.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
                continue;

            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }

            var initial = ToPinyinInitial(ch);
            if (initial is not null)
                sb.Append(initial.Value);
        }

        return sb.ToString();
    }

    private static char? ToPinyinInitial(char c)
    {
        if (Gb2312Encoding is null)
            return null;

        byte[] bytes;
        try
        {
            bytes = Gb2312Encoding.GetBytes(c.ToString());
        }
        catch
        {
            return null;
        }

        if (bytes.Length != 2)
            return null;

        var code = (short)bytes[0] * 256 + (short)bytes[1] - 65536;
        var rangeCount = Math.Min(InitialChars.Length, CodeBoundaries.Length);
        if (rangeCount == 0)
            return null;

        for (var i = 0; i < rangeCount; i++)
        {
            var start = CodeBoundaries[i];
            var end = i == rangeCount - 1
                ? int.MaxValue
                : CodeBoundaries[i + 1];
            if (code >= start && code < end)
                return InitialChars[i];
        }

        return null;
    }
}
