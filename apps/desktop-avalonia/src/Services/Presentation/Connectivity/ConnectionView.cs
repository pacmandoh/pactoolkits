using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

/// <summary>
/// 用户可见连接：Unknown / NotConfigured / Up / Down / Blocked
///
/// 未配置与已配置断开分开；探测枚举只进日志
/// </summary>
public enum ConnectionKind
{
    Unknown,
    NotConfigured,
    Up,
    Down,
    Blocked,
}

/// <summary>本机配置与 PacApi 探测快照映射为用户可见连接</summary>
public sealed record ConnectionView(ConnectionKind Kind, string Title, string Message)
{
    /// <summary>未配置单独一种；已配置 Down/Blocked 用探测 Detail</summary>
    public static ConnectionView From(ApiAvailabilitySnapshot snap, bool isConfigured = true)
    {
        if (!isConfigured)
        {
            return NotConfiguredView;
        }

        // 首检未完成：不进横幅
        if (!snap.FirstCheckCompleted)
        {
            return Unchecked;
        }

        return snap.State switch
        {
            ApiAvailabilityState.Ready => new(ConnectionKind.Up, string.Empty, string.Empty),
            ApiAvailabilityState.ContractBlocked => new(
                ConnectionKind.Blocked,
                "PacApi 服务协议不兼容",
                snap.Detail ?? "与 PacApi 服务协议版本不兼容，业务功能已阻断"),
            ApiAvailabilityState.SchemaBlocked => new(
                ConnectionKind.Blocked,
                "PacApi 服务数据库结构不兼容",
                snap.Detail ?? "服务端数据库结构不兼容，业务功能已阻断"),
            ApiAvailabilityState.ServerDatabaseBlocked => new(
                ConnectionKind.Down,
                "PacApi 服务数据库不可用",
                snap.Detail ?? "PacApi 服务已连接，但服务端数据库不可用，业务功能无法使用"),
            _ => new(
                ConnectionKind.Down,
                "PacApi 服务不可用",
                string.IsNullOrWhiteSpace(snap.Detail)
                    ? "无法连接 PacApi 服务，业务功能无法使用"
                    : snap.Detail),
        };
    }

    public static bool IsReady(ApiAvailabilitySnapshot snap, bool isConfigured = true)
        => From(snap, isConfigured).Kind == ConnectionKind.Up;

    public static bool IsBlocked(ApiAvailabilitySnapshot snap, bool isConfigured = true)
        => From(snap, isConfigured).Kind == ConnectionKind.Blocked;

    private static readonly ConnectionView NotConfiguredView = new(
        ConnectionKind.NotConfigured,
        "未配置 PacApi 服务",
        "请填写服务地址与密钥后再使用");

    private static readonly ConnectionView Unchecked = new(
        ConnectionKind.Unknown,
        string.Empty,
        string.Empty);
}
