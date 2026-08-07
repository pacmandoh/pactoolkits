using System.Text.Json;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Desktop 声明的期望挂载集合（host.desired.json / 管道 desired）；仅含已过 Desktop 门的 id；Host 只 reconcile
/// </summary>
public sealed class AgentsDesired
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;

    public DateTimeOffset Ts { get; set; }

    public List<string> Modules { get; set; } = [];

    public static AgentsDesired Create(IEnumerable<string> moduleIds)
        => new()
        {
            SchemaVersion = CurrentSchema,
            Ts = DateTimeOffset.UtcNow,
            Modules = moduleIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToList(),
        };

    public static void Write(string agentsDir, IEnumerable<string> moduleIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentsDir);
        var status = Create(moduleIds);
        Directory.CreateDirectory(agentsDir);
        var path = AgentsPaths.HostDesiredPath(agentsDir);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(status, AgentsJson.Options);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static HashSet<string>? TryReadIds(string? agentsDir)
    {
        var desired = TryRead(agentsDir);
        if (desired is null)
        {
            return null;
        }

        return new HashSet<string>(desired.Modules, StringComparer.Ordinal);
    }

    public static AgentsDesired? TryRead(string? agentsDir)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return null;
        }

        var path = AgentsPaths.HostDesiredPath(agentsDir);
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

            var desired = JsonSerializer.Deserialize<AgentsDesired>(json, AgentsJson.Options);
            if (desired is null || desired.SchemaVersion != CurrentSchema)
            {
                return null;
            }

            return desired;
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
            var path = AgentsPaths.HostDesiredPath(agentsDir);
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
            // 退出路径忽略清理失败
        }
    }
}
