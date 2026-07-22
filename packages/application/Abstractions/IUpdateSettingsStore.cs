namespace PacToolkits.Application.Abstractions;

/// <summary>更新设置持久化</summary>
public interface IUpdateSettingsStore
{
    UpdateOptions Load();
    Task SaveAsync(UpdateOptions options, CancellationToken ct = default);
}
