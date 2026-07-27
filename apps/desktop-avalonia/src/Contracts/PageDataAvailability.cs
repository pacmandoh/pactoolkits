namespace PacToolkits.Desktop.Avalonia.Contracts;

/// <summary>
/// 表示页面数据可用性，独立于 Shell 数据库连接状态和区块空状态
/// </summary>
public enum PageDataAvailability
{
    NotLoaded,
    AwaitingDatabase,
    AccessBlocked,
    Loading,
    LoadFailed,
    Stale,
    Ready
}
