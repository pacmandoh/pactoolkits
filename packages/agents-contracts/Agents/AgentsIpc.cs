using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Desktop 与 Host 本地 IPC 契约（命名管道与换行 JSON）
///
/// ops：desired / quit / ping / getStatus；evs：status / moduleFailed / ok / pong / error
/// host.status / host.desired 为诊断镜像（Host 写盘）；主路径是命名管道
/// </summary>
public static class AgentsIpc
{
    public const int ProtocolVersion = 1;

    public const string PipeNamePrefix = "PacToolkits.Agents.";

    /// <summary>与 Status/Desired 共用 <see cref="AgentsJson.Options"/></summary>
    public static JsonSerializerOptions JsonOptions => AgentsJson.Options;

    /// <summary>
    /// 由 Agents 部署目录派生管道名，多安装并存时互不撞
    /// </summary>
    public static string PipeName(string agentsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentsDir);
        // 同一安装目录的不同字符串写法必须得到同一管道名
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(agentsDir));
        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        return PipeNamePrefix + Convert.ToHexString(hash.AsSpan(0, 8));
    }

    public static string Serialize(AgentsIpcMessage message)
        => JsonSerializer.Serialize(message, JsonOptions);

    public static AgentsIpcMessage? TryDeserialize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            var message = JsonSerializer.Deserialize<AgentsIpcMessage>(line, JsonOptions);
            if (message is null || message.V != ProtocolVersion)
            {
                return null;
            }

            return message;
        }
        catch
        {
            return null;
        }
    }

    public static AgentsIpcMessage Desired(IEnumerable<string> modules, string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Op = AgentsIpcOps.Desired,
            Id = id,
            Modules = modules
                .Where(static m => !string.IsNullOrWhiteSpace(m))
                .Select(static m => m.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        };

    public static AgentsIpcMessage Quit(string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Op = AgentsIpcOps.Quit,
            Id = id,
        };

    public static AgentsIpcMessage Ping(string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Op = AgentsIpcOps.Ping,
            Id = id,
        };

    public static AgentsIpcMessage GetStatus(string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Op = AgentsIpcOps.GetStatus,
            Id = id,
        };

    public static AgentsIpcMessage Ok(string? id)
        => new()
        {
            V = ProtocolVersion,
            Ev = AgentsIpcEvs.Ok,
            Id = id,
        };

    public static AgentsIpcMessage Pong(string? id)
        => new()
        {
            V = ProtocolVersion,
            Ev = AgentsIpcEvs.Pong,
            Id = id,
        };

    public static AgentsIpcMessage StatusEvent(AgentsStatus status, string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Ev = AgentsIpcEvs.Status,
            Id = id,
            Status = status,
        };

    /// <summary>模块失败推送；正文与 Snapshot.LastError / state=Failed 同义</summary>
    public static AgentsIpcMessage ModuleFailedEvent(
        string moduleId,
        string? message,
        AgentsStatus? status = null,
        string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Ev = AgentsIpcEvs.ModuleFailed,
            Id = id,
            ModuleId = moduleId,
            Message = message,
            Status = status,
        };

    public static AgentsIpcMessage Error(string message, string? id = null)
        => new()
        {
            V = ProtocolVersion,
            Ev = AgentsIpcEvs.Error,
            Id = id,
            Message = message,
        };
}

/// <summary>
/// 管道上的单帧消息（op 入站 / ev 出站）
/// </summary>
public sealed class AgentsIpcMessage
{
    public int V { get; set; } = AgentsIpc.ProtocolVersion;

    public string? Op { get; set; }

    public string? Ev { get; set; }

    public string? Id { get; set; }

    public List<string>? Modules { get; set; }

    public AgentsStatus? Status { get; set; }

    public string? ModuleId { get; set; }

    public string? Message { get; set; }
}

public static class AgentsIpcOps
{
    public const string Desired = "desired";
    public const string Quit = "quit";
    public const string Ping = "ping";
    public const string GetStatus = "getStatus";
}

public static class AgentsIpcEvs
{
    public const string Status = "status";
    public const string ModuleFailed = "moduleFailed";
    public const string Ok = "ok";
    public const string Pong = "pong";
    public const string Error = "error";
}
