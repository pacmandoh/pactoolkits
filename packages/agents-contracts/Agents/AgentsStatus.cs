using System.Text.Json;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Host 发布的 StatusSnapshot（IPC / host.status.json）；Desktop 唯一运行时真相源
///
/// schema v2：HostState、模块 State/LastError、catalog 字段一并推送；事实与语义态均由 Host 合成
/// </summary>
public sealed class AgentsStatus
{
    public const int CurrentSchema = 2;

    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(2);

    public int SchemaVersion { get; set; } = CurrentSchema;

    public DateTimeOffset Ts { get; set; }

    public int HostPid { get; set; }

    public AgentsRunState HostState { get; set; } = AgentsRunState.Running;

    public List<AgentsStatusModule> Modules { get; set; } = [];

    public bool IsFresh(TimeSpan maxAge, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        return clock - Ts <= maxAge && clock - Ts >= TimeSpan.FromSeconds(-5);
    }

    public AgentsStatusModule? FindModule(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        foreach (var module in Modules)
        {
            if (string.Equals(module.Id, moduleId, StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }

    public static AgentsStatus Create(int hostPid, IEnumerable<AgentsStatusModule> modules)
        => new()
        {
            SchemaVersion = CurrentSchema,
            Ts = DateTimeOffset.UtcNow,
            HostPid = hostPid,
            HostState = AgentsRunState.Running,
            Modules = modules.ToList(),
        };

    public static void Write(string agentsDir, AgentsStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentsDir);
        ArgumentNullException.ThrowIfNull(status);

        Directory.CreateDirectory(agentsDir);
        var path = AgentsPaths.HostStatusPath(agentsDir);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(status, AgentsJson.Options);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static AgentsStatus? TryRead(string? agentsDir, TimeSpan? maxAge = null)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return null;
        }

        var path = AgentsPaths.HostStatusPath(agentsDir);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var status = JsonSerializer.Deserialize<AgentsStatus>(json, AgentsJson.Options);
            if (status is null || status.SchemaVersion != CurrentSchema)
            {
                return null;
            }

            if (maxAge is { } age && !status.IsFresh(age))
            {
                return null;
            }

            return status;
        }
        catch
        {
            return null;
        }
    }

    public static void TryDelete(string? agentsDir)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return;
        }

        try
        {
            var path = AgentsPaths.HostStatusPath(agentsDir);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var tmp = path + ".tmp";
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
        catch
        {
            // 退出路径忽略
        }
    }
}

/// <summary>
/// 单模块监督 + 展示元数据（catalog 片段）
/// </summary>
public sealed class AgentsStatusModule
{
    public string Id { get; set; } = string.Empty;

    public int? Pid { get; set; }

    public bool Ready { get; set; }

    public AgentsRunState State { get; set; } = AgentsRunState.Stopped;

    public string? LastError { get; set; }

    public string? Version { get; set; }

    public string? DisplayName { get; set; }

    public string? Runtime { get; set; }

    public string? EntryWinX64 { get; set; }

    public bool RequiresDatabase { get; set; } = true;

    public string? IconActive { get; set; }

    public string? IconInactive { get; set; }

    public bool BottomStatusBar { get; set; }

    public bool TopStatusPills { get; set; } = true;

    public int Order { get; set; }

    public bool ProcessAlive => Pid is int;
}
