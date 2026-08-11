using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Files;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

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
            var defaultPath = AgentsPaths.ModuleDefaultSettingsPath(agentsDir, id);
            if (!File.Exists(userPath))
            {
                AtomicFile.CopyNew(defaultPath, userPath);
                return;
            }

            // schema 新增键时补齐用户配置，避免校验失败
            MergeMissingFromDefault(userPath, defaultPath);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private static void MergeMissingFromDefault(string userPath, string defaultPath)
    {
        if (!File.Exists(defaultPath))
        {
            return;
        }

        try
        {
            var user = JsonNode.Parse(File.ReadAllText(userPath)) as JsonObject;
            var defaults = JsonNode.Parse(File.ReadAllText(defaultPath)) as JsonObject;
            if (user is null || defaults is null)
            {
                return;
            }

            var changed = false;
            foreach (var kv in defaults)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || user.ContainsKey(kv.Key))
                {
                    continue;
                }

                user[kv.Key] = kv.Value?.DeepClone();
                changed = true;
            }

            if (changed)
            {
                AtomicFile.WriteAllText(userPath, user.ToJsonString(JsonOptions));
            }
        }
        catch
        {
            // 合并失败不阻断启动；校验阶段仍会表面问题
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
