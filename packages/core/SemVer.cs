namespace PacToolkits.Core;

/// <summary>
/// SemVer 2.0 核心比较（含 prerelease 序）；build metadata（+…）不参与优先级
/// PreRelease 为 null 表示正式发布，否则为 '-' 后的 prerelease 标识串（不含前导 '-'）
/// </summary>
public readonly record struct SemVerInfo(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease);

/// <summary>
/// 解析与比较 X.Y.Z / X.Y.Z-prerelease / …+build
/// </summary>
public static class SemVer
{
    public static bool TryParse(string? text, out SemVerInfo value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            s = s[..plus];
        }

        string? pre = null;
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (string.IsNullOrWhiteSpace(pre) || pre.Contains('+', StringComparison.Ordinal))
            {
                return false;
            }
        }

        var parts = s.Split('.', StringSplitOptions.None);
        if (parts.Length != 3
            || !TryParseNonNegativeInt(parts[0], out var major)
            || !TryParseNonNegativeInt(parts[1], out var minor)
            || !TryParseNonNegativeInt(parts[2], out var patch))
        {
            return false;
        }

        if (pre is not null)
        {
            foreach (var ident in pre.Split('.', StringSplitOptions.None))
            {
                if (ident.Length == 0 || !IsValidPrereleaseIdent(ident))
                {
                    return false;
                }
            }
        }

        value = new SemVerInfo(major, minor, patch, pre);
        return true;
    }

    /// <summary>
    /// 闭区间 [min, max]；比较遵循 SemVer 优先级（正式版 &gt; 同核心的 prerelease）
    /// </summary>
    public static bool IsInInclusiveRange(string? current, string? minimum, string? maximum)
    {
        if (!TryParse(current, out var cur)
            || !TryParse(minimum, out var min)
            || !TryParse(maximum, out var max)
            || Compare(min, max) > 0)
        {
            return false;
        }

        return Compare(cur, min) >= 0 && Compare(cur, max) <= 0;
    }

    public static int Compare(string? left, string? right)
    {
        if (!TryParse(left, out var l) || !TryParse(right, out var r))
        {
            throw new ArgumentException("Invalid SemVer for comparison.");
        }

        return Compare(l, r);
    }

    public static int Compare(SemVerInfo left, SemVerInfo right)
    {
        var core = left.Major.CompareTo(right.Major);
        if (core != 0)
        {
            return core;
        }

        core = left.Minor.CompareTo(right.Minor);
        if (core != 0)
        {
            return core;
        }

        core = left.Patch.CompareTo(right.Patch);
        if (core != 0)
        {
            return core;
        }

        // 同核心：无 prerelease 的正式版优先级更高
        var leftPre = left.PreRelease;
        var rightPre = right.PreRelease;
        if (leftPre is null && rightPre is null)
        {
            return 0;
        }

        if (leftPre is null)
        {
            return 1;
        }

        if (rightPre is null)
        {
            return -1;
        }

        return ComparePrerelease(leftPre, rightPre);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.', StringSplitOptions.None);
        var rightParts = right.Split('.', StringSplitOptions.None);
        var n = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < n; i++)
        {
            if (i >= leftParts.Length)
            {
                return -1;
            }

            if (i >= rightParts.Length)
            {
                return 1;
            }

            var a = leftParts[i];
            var b = rightParts[i];
            var aNum = TryParseNonNegativeInt(a, out var ai);
            var bNum = TryParseNonNegativeInt(b, out var bi);
            if (aNum && bNum)
            {
                var c = ai.CompareTo(bi);
                if (c != 0)
                {
                    return c;
                }

                continue;
            }

            // 数字标识符优先级低于非数字
            if (aNum)
            {
                return -1;
            }

            if (bNum)
            {
                return 1;
            }

            var lex = string.CompareOrdinal(a, b);
            if (lex != 0)
            {
                return lex;
            }
        }

        return 0;
    }

    private static bool TryParseNonNegativeInt(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || text[0] == '-' || text[0] == '+')
        {
            return false;
        }

        // 禁止前导零的多位数字（SemVer 数字标识）
        if (text.Length > 1 && text[0] == '0' && text.All(char.IsAsciiDigit))
        {
            return false;
        }

        return int.TryParse(text, out value) && value >= 0 && text.All(char.IsAsciiDigit);
    }

    private static bool IsValidPrereleaseIdent(string ident)
    {
        // [0-9A-Za-z-]，不允许空
        foreach (var ch in ident)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            {
                return false;
            }
        }

        // 纯数字时由 TryParseNonNegativeInt 统一禁前导零
        if (ident.All(char.IsAsciiDigit)
            && !TryParseNonNegativeInt(ident, out _))
        {
            return false;
        }

        return true;
    }
}
