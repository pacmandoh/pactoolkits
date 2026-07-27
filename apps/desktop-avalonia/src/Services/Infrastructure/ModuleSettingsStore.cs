using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>
/// 管理模块默认配置与用户配置的复制和持久化，不修改安装目录中的默认配置
/// </summary>
public sealed class ModuleSettingsStore : IModuleSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _configDir;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public ModuleSettingsStore(IAppConfigStore configStore)
    {
        ArgumentNullException.ThrowIfNull(configStore);
        _configDir = Path.GetDirectoryName(configStore.ConfigPath)
            ?? throw new InvalidOperationException("Config directory is missing");
    }

    public void EnsureUserSettings(string moduleId, string agentsDir)
    {
        var id = RequireId(moduleId);
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            throw new ArgumentException("agentsDir is required", nameof(agentsDir));
        }

        _ioGate.Wait();
        try
        {
            var userPath = UserSettingsPath(id);
            if (File.Exists(userPath))
            {
                return;
            }

            var defaultPath = AgentsPaths.ModuleDefaultSettingsPath(agentsDir, id);
            AtomicFile.CopyNew(defaultPath, userPath);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public string LoadSettingsJson(string moduleId)
    {
        _ioGate.Wait();
        try
        {
            var path = UserSettingsPath(RequireId(moduleId));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"User module settings missing (call EnsureUserSettings first): {path}",
                    path);
            }

            return File.ReadAllText(path);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveSettingsJsonAsync(string moduleId, string json, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        var id = RequireId(moduleId);
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Settings root must be a JSON object");
        var content = node.ToJsonString(JsonOptions);

        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteAllTextAsync(UserSettingsPath(id), content, ct).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public string? TryLoadSchemaJson(string moduleId, string agentsDir)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return null;
        }

        var path = AgentsPaths.ModuleSettingsSchemaPath(agentsDir, RequireId(moduleId));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private string UserSettingsPath(string moduleId)
        => AgentsPaths.ModuleSettingsPath(_configDir, moduleId);

    private static string RequireId(string moduleId)
    {
        if (!AgentsPath.IsValidModuleId(moduleId))
        {
            throw new ArgumentException("moduleId is invalid", nameof(moduleId));
        }

        return moduleId.Trim();
    }
}
