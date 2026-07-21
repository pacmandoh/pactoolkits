namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 订阅变更水位；TopicChanged 驱动相关页面脏刷新
/// </summary>
public interface IChangeWatermarkService : IDisposable
{
    event Action<string>? TopicChanged;
    void Start();
}
