namespace PacToolkits.Desktop.Avalonia.Contracts.Presentation;

/// <summary>
/// 页面数据可用性，独立于 Shell 连接呈现和区块空态
/// </summary>
public enum PageDataAvailability
{
    NotLoaded,
    AwaitingService,
    AccessBlocked,
    Loading,
    LoadFailed,
    Stale,
    Ready
}
