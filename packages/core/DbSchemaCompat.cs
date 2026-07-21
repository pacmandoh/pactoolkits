namespace PacToolkits.Core;

/// <summary>
/// 数据库 Schema 与 Desktop / Agents 声明范围的兼容状态
/// </summary>
public enum DbSchemaCompatibility
{
    Unknown,
    MetadataMissing,
    BelowMinimum,
    Compatible,
    AboveMaximum,
}

/// <summary>
/// Schema 兼容判定结果（含面向 UI 的说明文案）
/// </summary>
public sealed record DbSchemaCompatibilityResult(
    DbSchemaCompatibility Status,
    string CurrentVersion,
    string MinimumVersion,
    string MaximumVersion,
    string Message)
{
    public bool IsCompatible => Status == DbSchemaCompatibility.Compatible;
    public bool IsMetadataMissing => Status == DbSchemaCompatibility.MetadataMissing;
    public bool IsTooLow => Status == DbSchemaCompatibility.BelowMinimum;
}

/// <summary>
/// 按 SemVer 判定数据库 Schema 是否落在 Desktop / Agents 交集范围内
///
/// 负责：兼容枚举、门槛合并、不兼容提示文案。不访问数据库
/// </summary>
public static class DbSchemaCompat
{
    private const string IncompatibleTitle = "数据库版本不兼容";

    public static string NormalizeBound(string bound, string fallback)
        => string.Equals(bound, "unknown", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(bound)
            ? fallback
            : bound;

    public static DbSchemaCompatibilityResult Evaluate(
        string? current,
        string minimum,
        string maximum)
    {
        var currentText = (current ?? string.Empty).Trim();
        var minimumText = (minimum ?? string.Empty).Trim();
        var maximumText = (maximum ?? string.Empty).Trim();

        if (!TryParseSemVer(currentText, out var currentVersion)
            || !TryParseSemVer(minimumText, out var minimumVersion)
            || !TryParseSemVer(maximumText, out var maximumVersion)
            || CompareSemVer(minimumVersion, maximumVersion) > 0)
        {
            return new DbSchemaCompatibilityResult(
                DbSchemaCompatibility.Unknown,
                currentText,
                minimumText,
                maximumText,
                "无法确定数据库版本兼容范围");
        }

        if (CompareSemVer(currentVersion, minimumVersion) < 0)
        {
            return new DbSchemaCompatibilityResult(
                DbSchemaCompatibility.BelowMinimum,
                currentText,
                minimumText,
                maximumText,
                $"数据库版本 {currentText} 低于最低支持版本 {minimumText}");
        }

        if (CompareSemVer(currentVersion, maximumVersion) > 0)
        {
            return new DbSchemaCompatibilityResult(
                DbSchemaCompatibility.AboveMaximum,
                currentText,
                minimumText,
                maximumText,
                $"数据库版本高于当前程序支持范围：当前 {currentText}，最高支持 {maximumText}");
        }

        return new DbSchemaCompatibilityResult(
            DbSchemaCompatibility.Compatible,
            currentText,
            minimumText,
            maximumText,
            $"数据库版本 {currentText} 位于支持范围 {minimumText} - {maximumText}");
    }

    public static string GetRequiredMax(string uiMax, string agentsMax)
    {
        if (!TryParseSemVer(uiMax, out var ui) || !TryParseSemVer(agentsMax, out var agents))
        {
            return uiMax;
        }

        return CompareSemVer(ui, agents) <= 0 ? uiMax : agentsMax;
    }

    public static string GetRequiredMin(string uiMin, string agentsMin)
    {
        if (!TryParseSemVer(uiMin, out var ui) || !TryParseSemVer(agentsMin, out var agents))
        {
            return uiMin;
        }

        return CompareSemVer(ui, agents) >= 0 ? uiMin : agentsMin;
    }

    public static bool TryParseSemVer(string value, out (int major, int minor, int patch) ver)
    {
        ver = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var minor))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        ver = (major, minor, patch);
        return true;
    }

    public static int CompareSemVer((int major, int minor, int patch) left, (int major, int minor, int patch) right)
    {
        if (left.major != right.major)
        {
            return left.major.CompareTo(right.major);
        }

        if (left.minor != right.minor)
        {
            return left.minor.CompareTo(right.minor);
        }

        return left.patch.CompareTo(right.patch);
    }

    public static string BuildIncompatibleMessage(
        bool schemaOk,
        string? schemaValue,
        string? schemaReason,
        string uiMin,
        string agentsMin,
        string uiMax,
        string agentsMax,
        string? requiredMin = null,
        string? requiredMax = null)
    {
        var detail = schemaOk
            ? $"数据库版本：{schemaValue}\nDesktop 支持范围：{uiMin} - {uiMax}\nAgents 支持范围：{agentsMin} - {agentsMax}"
            : $"读取失败：{schemaReason ?? "缺少 schema_version 表或版本记录"}\nDesktop 支持范围：{uiMin} - {uiMax}\nAgents 支持范围：{agentsMin} - {agentsMax}";

        if (!string.IsNullOrWhiteSpace(requiredMin))
        {
            detail += $"\n实际最低门槛：{requiredMin}";
        }

        if (!string.IsNullOrWhiteSpace(requiredMax))
        {
            detail += $"\n实际最高门槛：{requiredMax}";
        }

        var guidance = schemaOk
                       && TryParseSemVer(schemaValue ?? string.Empty, out var current)
                       && TryParseSemVer(requiredMax ?? string.Empty, out var max)
                       && CompareSemVer(current, max) > 0
            ? "数据库版本高于当前程序支持范围，已阻断数据库业务操作，不会执行自动降级，请升级 PacToolkits"
            : "请联系维护者将数据库更新到适配版本后再连接";

        return $"检测到当前数据库版本与 PacToolkits 不兼容\n{detail}\n\n{guidance}";
    }

    public static string GetIncompatibleTitle() => IncompatibleTitle;
}
