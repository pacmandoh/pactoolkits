namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 更新设置内存态、保存与变更通知
/// </summary>
public interface IUpdateSettingsService
{
    UpdateOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(UpdateOptions options, CancellationToken ct = default);
    void Apply(UpdateOptions options);
    void Reload();
}
