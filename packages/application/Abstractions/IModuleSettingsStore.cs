namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 管理模块默认配置、用户配置和设置 schema 的存储边界
/// </summary>
public interface IModuleSettingsStore
{
    /// <summary>仅在用户配置不存在时复制模块默认配置</summary>
    void EnsureUserSettings(string moduleId, string agentsDir);

    string LoadSettingsJson(string moduleId);

    Task SaveSettingsJsonAsync(string moduleId, string json, CancellationToken ct = default);

    string? TryLoadSchemaJson(string moduleId, string agentsDir);
}
