using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>
/// Desktop 业务用库 schema 提示句（Settings 长文、守卫/探针短句）
/// </summary>
public static class DbSchemaDesktop
{
    public static string Title => "数据库版本不兼容";

    /// <summary>设置页/弹窗用的长说明（含升级/部署指导）</summary>
    public static string Incompatible(
        bool schemaOk,
        string? schemaValue,
        string? schemaReason,
        string min,
        string max,
        DbSchemaCompatibility status)
    {
        var detail = schemaOk
            ? $"数据库版本：{schemaValue}\nPacToolkits 支持范围：{min} - {max}"
            : $"读取失败：{schemaReason ?? "缺少 schema_version 表或版本记录"}\nPacToolkits 支持范围：{min} - {max}";

        var guidance = status == DbSchemaCompatibility.AboveMaximum
            ? "数据库版本高于当前程序支持范围，已阻断数据库业务操作，不会执行自动降级，请升级 PacToolkits"
            : "请联系维护者将数据库更新到适配版本后再连接";

        return $"检测到当前数据库版本与 PacToolkits 不兼容\n{detail}\n\n{guidance}";
    }

    /// <summary>守卫 Block / 更新探针用的单行原因</summary>
    public static string GateBlock(DbSchemaCompatibilityResult result)
        => result.Status switch
        {
            DbSchemaCompatibility.BelowMinimum
                => $"数据库版本 {result.CurrentVersion} 过低（最低支持 {result.MinimumVersion}）",
            DbSchemaCompatibility.AboveMaximum
                => $"数据库版本 {result.CurrentVersion} 过高（最高支持 {result.MaximumVersion}）",
            DbSchemaCompatibility.MetadataMissing
                => "数据库版本元数据缺失，需要通过外部部署工具初始化",
            DbSchemaCompatibility.Compatible
                => $"数据库版本 {result.CurrentVersion} 位于支持范围 {result.MinimumVersion} - {result.MaximumVersion}",
            // Unknown：Message 透出读库 Reason；FromRange 空 Message 用固定短句
            _ => string.IsNullOrWhiteSpace(result.Message)
                ? "无法确定数据库版本兼容范围"
                : result.Message,
        };
}
