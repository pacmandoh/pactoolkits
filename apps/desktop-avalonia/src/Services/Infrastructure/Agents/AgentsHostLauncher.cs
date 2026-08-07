using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Desktop 对 Host 进程的 OS 级生命周期（CreateProcess / 超时强杀进程树 / Host.exe 热更）
///
/// 模块监管在 Host；仅 Host 启停闸门可扫入口残留（见 <see cref="AgentsModuleOrphans"/>）
/// </summary>
internal sealed class AgentsHostLauncher
{
    private readonly object _gate;
    private readonly IAppLogger _logger;
    private readonly AgentsBinaryMonitor _binaries = new();
    private int? _lastProcessId;
    private bool _launching;

    public AgentsHostLauncher(object gate, IAppLogger logger)
    {
        _gate = gate;
        _logger = logger;
    }

    public bool Launching
    {
        get
        {
            lock (_gate)
            {
                return _launching;
            }
        }
    }

    public int? LastProcessId
    {
        get
        {
            lock (_gate)
            {
                return _lastProcessId;
            }
        }
        set
        {
            lock (_gate)
            {
                _lastProcessId = value;
            }
        }
    }

    public void MarkLaunching()
    {
        lock (_gate)
        {
            _launching = true;
        }
    }

    public void ClearLaunching()
    {
        lock (_gate)
        {
            _launching = false;
        }
    }

    public bool TryStart(ProcessStartInfo startInfo, out int processId)
    {
        processId = 0;
        var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        processId = process.Id;
        lock (_gate)
        {
            _lastProcessId = processId;
        }

        return true;
    }

    public void SyncBinaryWatch(AgentsOptions options)
        => _binaries.Sync(AgentsDeployPaths.ResolveExe(options.ExecutablePath));

    public bool DetectBinaryChange(AgentsRunState hostState, out AgentsBinaryStamp stamp)
        => _binaries.Detect(hostState, out stamp);

    public void AcceptBinary(AgentsBinaryStamp stamp)
        => _binaries.Accept(stamp);

    public void NoteHostPid(int hostPid)
    {
        if (hostPid <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _lastProcessId = hostPid;
        }
    }

    public void ClearPid()
    {
        lock (_gate)
        {
            _lastProcessId = null;
        }
    }

    /// <summary>
    /// 管道 quit 超时后强杀 Host 进程树（子模块随树退出）
    /// </summary>
    public void ForceKill(IAgentsClient link, AgentsStatus? snapshotHint)
    {
        int? pid;
        lock (_gate)
        {
            pid = _lastProcessId;
        }

        if (pid is null or <= 0)
        {
            var status = snapshotHint
                         ?? link.TryGetStatus(TimeSpan.FromMinutes(5))
                         ?? link.LastSnapshot;
            if (status is { HostPid: > 0 })
            {
                pid = status.HostPid;
            }
        }

        if (pid is not int hostPid)
        {
            return;
        }

        try
        {
            using var p = Process.GetProcessById(hostPid);
            if (p.HasExited)
            {
                return;
            }

            p.Kill(entireProcessTree: true);
            p.WaitForExit(4000);
            _logger.Info("Agents", "agents.host.force_kill", "Force-killed Host process tree", new { hostPid });
        }
        catch (Exception ex)
        {
            _logger.Warn(
                "Agents",
                "agents.host.force_kill_fail",
                "Failed to force-kill Host",
                ex,
                new { hostPid = pid });
        }
    }

    public async Task AttachLinkAsync(
        IAgentsClient link,
        string? agentsDir,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return;
        }

        // 每次 Host 就绪都重连，避免沿用到已退出的 Host 会话
        var ok = await link.ConnectAsync(agentsDir, timeout, ct).ConfigureAwait(false);
        if (!ok)
        {
            _logger.Warn(
                "Agents",
                "agents.ipc.connect_fail",
                "Failed to attach Agents IPC; will retry; file mirrors only until connected");
        }
    }
}
