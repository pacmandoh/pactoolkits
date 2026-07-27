namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 模块业务配置：默认文件在 Agents/Modules/&lt;Id&gt;/；用户真相在 ConfigDir/agents/modules/&lt;Id&gt;/settings.json
/// </summary>
public interface IModuleSettingsStore
{
    /// <summary>
    /// 用户 settings 不存在时从模块默认文件整文件复制；默认文件缺失则抛错
    /// </summary>
    void EnsureUserSettings(string moduleId, string agentsDir);

    string LoadSettingsJson(string moduleId);

    Task SaveSettingsJsonAsync(string moduleId, string json, CancellationToken ct = default);

    string? TryLoadSchemaJson(string moduleId, string agentsDir);
}
