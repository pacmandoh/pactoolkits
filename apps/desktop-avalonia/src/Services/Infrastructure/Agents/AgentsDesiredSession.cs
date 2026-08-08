using System;
using System.Collections.Generic;
using System.Linq;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Desktop 会话 desired：**仅**已过 Desktop 策略门禁、允许 Host 挂起的模块 id
///
/// 连库 / schema / 配置在本侧滤完再写；Host 只 reconcile，不解释为何不能挂
/// desired 以管道为准；host.desired.json 为冷启动种子与诊断镜像
/// </summary>
internal sealed class AgentsDesiredSession
{
    private readonly object _gate;
    private readonly HashSet<string> _mount = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pausedForDatabase = new(StringComparer.Ordinal);

    public AgentsDesiredSession(object gate)
    {
        _gate = gate;
    }

    public bool Contains(string moduleId)
    {
        lock (_gate)
        {
            return _mount.Contains(moduleId);
        }
    }

    public void Add(string moduleId)
    {
        lock (_gate)
        {
            _mount.Add(moduleId);
        }
    }

    public void Remove(string moduleId)
    {
        lock (_gate)
        {
            _mount.Remove(moduleId);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _mount.Clear();
        }
    }

    public void Replace(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            _mount.Clear();
            foreach (var id in ids)
            {
                _mount.Add(id);
            }
        }
    }

    public string[] Snapshot()
    {
        lock (_gate)
        {
            return _mount.ToArray();
        }
    }

    public void PauseForDatabase(string moduleId)
    {
        lock (_gate)
        {
            _mount.Remove(moduleId);
            _pausedForDatabase.Add(moduleId);
        }
    }

    public void NotePaused(string moduleId)
    {
        lock (_gate)
        {
            _pausedForDatabase.Add(moduleId);
        }
    }

    public void Unpause(string moduleId)
    {
        lock (_gate)
        {
            _pausedForDatabase.Remove(moduleId);
        }
    }

    public List<string> SnapshotPaused()
    {
        lock (_gate)
        {
            return _pausedForDatabase.ToList();
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _mount.Clear();
            _pausedForDatabase.Clear();
        }
    }

    /// <summary>
    /// 写文件镜像，管道已连则发 desired 命令（IPC 失败只记日志，不以文件当备用控制）
    /// </summary>
    public void Publish(IAgentsClient client, string? agentsDir, IAppLogger logger)
    {
        var ids = Snapshot();

        if (!string.IsNullOrWhiteSpace(agentsDir))
        {
            try
            {
                AgentsDesired.Write(agentsDir, ids);
            }
            catch (Exception ex)
            {
                logger.Warn("Agents", "agents.desired.mirror_fail", "Failed to mirror host.desired.json", ex);
            }
        }

        if (!client.IsLinkConnected)
        {
            return;
        }

        try
        {
            if (!client.TrySendDesired(ids))
            {
                logger.Warn(
                    "Agents",
                    "agents.ipc.desired_fail",
                    "Failed to send desired over IPC");
            }
        }
        catch (Exception ex)
        {
            logger.Warn("Agents", "agents.ipc.desired_fail", "Failed to send desired over IPC", ex);
        }
    }
}
