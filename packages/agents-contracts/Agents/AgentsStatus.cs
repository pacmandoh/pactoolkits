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

    /// <summary>比较运行态，不含 Ts</summary>
    public static bool ContentEquals(AgentsStatus? left, AgentsStatus? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.SchemaVersion != right.SchemaVersion
            || left.HostPid != right.HostPid
            || left.HostState != right.HostState
            || left.Modules.Count != right.Modules.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Modules.Count; i++)
        {
            if (!ModuleContentEquals(left.Modules[i], right.Modules[i]))
            {
                return false;
            }
        }

        return true;
    }

    public AgentsStatus Clone()
    {
        var modules = new List<AgentsStatusModule>(Modules.Count);
        foreach (var module in Modules)
        {
            modules.Add(CloneModule(module));
        }

        return new AgentsStatus
        {
            SchemaVersion = SchemaVersion,
            Ts = Ts,
            HostPid = HostPid,
            HostState = HostState,
            Modules = modules,
        };
    }

    private static bool ModuleContentEquals(AgentsStatusModule left, AgentsStatusModule right)
        => left.Pid == right.Pid
           && left.Ready == right.Ready
           && left.State == right.State
           && left.BottomStatusBar == right.BottomStatusBar
           && left.TopStatusPills == right.TopStatusPills
           && left.Order == right.Order
           && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
           && string.Equals(left.LastError, right.LastError, StringComparison.Ordinal)
           && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
           && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
           && string.Equals(left.Runtime, right.Runtime, StringComparison.Ordinal)
           && string.Equals(left.EntryWinX64, right.EntryWinX64, StringComparison.Ordinal)
           && string.Equals(left.MinApiContract, right.MinApiContract, StringComparison.Ordinal)
           && string.Equals(left.MaxApiContract, right.MaxApiContract, StringComparison.Ordinal)
           && string.Equals(left.IconActive, right.IconActive, StringComparison.Ordinal)
           && string.Equals(left.IconInactive, right.IconInactive, StringComparison.Ordinal)
           && (left.RequiredApiScopes ?? []).SequenceEqual(
               right.RequiredApiScopes ?? [],
               StringComparer.Ordinal);

    private static AgentsStatusModule CloneModule(AgentsStatusModule source)
        => new()
        {
            Id = source.Id,
            Pid = source.Pid,
            Ready = source.Ready,
            State = source.State,
            LastError = source.LastError,
            Version = source.Version,
            DisplayName = source.DisplayName,
            Runtime = source.Runtime,
            EntryWinX64 = source.EntryWinX64,
            MinApiContract = source.MinApiContract,
            MaxApiContract = source.MaxApiContract,
            RequiredApiScopes = source.RequiredApiScopes is null
                ? null
                : [.. source.RequiredApiScopes],
            IconActive = source.IconActive,
            IconInactive = source.IconInactive,
            BottomStatusBar = source.BottomStatusBar,
            TopStatusPills = source.TopStatusPills,
            Order = source.Order,
        };

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
/// 单模块监督与展示元数据（catalog 片段）
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

    public string? MinApiContract { get; set; }

    public string? MaxApiContract { get; set; }

    public List<string>? RequiredApiScopes { get; set; }

    public string? IconActive { get; set; }

    public string? IconInactive { get; set; }

    public bool BottomStatusBar { get; set; }

    public bool TopStatusPills { get; set; } = true;

    public int Order { get; set; }

    public bool ProcessAlive => Pid is int;
}
