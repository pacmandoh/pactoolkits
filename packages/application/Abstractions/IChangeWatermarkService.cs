namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 订阅数据变更水位，并通知相关页面更新待刷新状态
/// </summary>
public interface IChangeWatermarkService : IDisposable
{
    event Action<string>? TopicChanged;
    void Start();
}
