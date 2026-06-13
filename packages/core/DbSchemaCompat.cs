namespace PacToolkits.Core;

public static class DbSchemaCompat
{
    private const string IncompatibleTitle = "数据库版本不兼容";

    public static string NormalizeBound(string bound, string fallback)
        => string.Equals(bound, "unknown", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(bound)
            ? fallback
            : bound;

    public static bool IsSemVerAtLeast(string value, string min)
    {
        if (!TryParseSemVer(value, out var v) || !TryParseSemVer(min, out var minV))
            return false;
        return CompareSemVer(v, minV) >= 0;
    }

    public static string GetRequiredMin(string uiMin, string agentMin)
    {
        if (!TryParseSemVer(uiMin, out var ui) || !TryParseSemVer(agentMin, out var agent))
            return uiMin;
        return CompareSemVer(ui, agent) >= 0 ? uiMin : agentMin;
    }

    public static bool TryParseSemVer(string value, out (int major, int minor, int patch) ver)
    {
        ver = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        if (!int.TryParse(parts[2], out var patch)) return false;
        ver = (major, minor, patch);
        return true;
    }

    public static int CompareSemVer((int major, int minor, int patch) left, (int major, int minor, int patch) right)
    {
        if (left.major != right.major) return left.major.CompareTo(right.major);
        if (left.minor != right.minor) return left.minor.CompareTo(right.minor);
        return left.patch.CompareTo(right.patch);
    }

    public static string BuildIncompatibleMessage(
        bool schemaOk,
        string? schemaValue,
        string? schemaReason,
        string uiMin,
        string agentMin,
        string? requiredMin = null)
    {
        var detail = schemaOk
            ? $"数据库版本：{schemaValue}\nUI 最低要求：{uiMin}\nAgent 最低要求：{agentMin}"
            : $"读取失败：{schemaReason ?? "缺少 schema_version 表或版本记录"}\nUI 最低要求：{uiMin}\nAgent 最低要求：{agentMin}";

        if (!string.IsNullOrWhiteSpace(requiredMin))
            detail += $"\n实际最低门槛：{requiredMin}";

        return $"检测到当前数据库版本与 PacToolkits 不兼容\n{detail}\n\n请联系维护者将数据库更新到适配版本后再连接";
    }

    public static string GetIncompatibleTitle() => IncompatibleTitle;
}
