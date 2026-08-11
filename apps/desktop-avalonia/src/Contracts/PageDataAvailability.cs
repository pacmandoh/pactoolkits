namespace PacToolkits.Desktop.Avalonia.Contracts;

/// <summary>
/// 表示页面数据可用性，独立于 Shell 数据库连接状态和区块空状态
/// </summary>
public enum PageDataAvailability
{
    NotLoaded,
    AwaitingDatabase,
    // 等 API 等远端服务，别和本机库断开的 AwaitingDatabase 混用
    AwaitingService,
    AccessBlocked,
    Loading,
    LoadFailed,
    Stale,
    Ready
}
