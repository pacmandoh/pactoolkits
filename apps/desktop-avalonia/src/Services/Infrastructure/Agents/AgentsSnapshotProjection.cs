using System;
using System.Collections.Generic;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Host Snapshot → Desktop UI 投影（catalog / state / lastError / 版本）
///
/// 管道缓存优先；status 文件仅观测镜像
/// </summary>
internal sealed class AgentsSnapshotProjection
{
    private readonly object _gate;
    private AgentsRunState _hostState = AgentsRunState.Unknown;
    private IReadOnlyList<ModuleDescriptor> _modules = [];
    private Dictionary<string, AgentsRunState> _moduleStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _moduleLastErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _moduleLastLaunchAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _moduleVersions = new(StringComparer.Ordinal);
    private AgentsStatus? _lastSnapshot;
    private DateTimeOffset? _hostLastLaunchAt;
    private string? _hostLastError;
    private string _hostVersion = "未知";

    public AgentsSnapshotProjection(object gate)
    {
        _gate = gate;
    }

    public AgentsRunState HostState
    {
        get
        {
            lock (_gate)
            {
                return _hostState;
            }
        }
    }

    public bool IsHostRunning => HostState == AgentsRunState.Running;

    public DateTimeOffset? HostLastLaunchAt
    {
        get
        {
            lock (_gate)
            {
                return _hostLastLaunchAt;
            }
        }
    }

    public string? HostLastError
    {
        get
        {
            lock (_gate)
            {
                return _hostLastError;
            }
        }
    }

    public string HostVersion
    {
        get
        {
            lock (_gate)
            {
                return _hostVersion;
            }
        }
    }

    public AgentsStatus? LastSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _lastSnapshot;
            }
        }
    }

    public IReadOnlyList<ModuleDescriptor> Modules
    {
        get
        {
            lock (_gate)
            {
                return _modules;
            }
        }
    }

    public void SetHostError(string message)
    {
        lock (_gate)
        {
            _hostLastError = message;
            _hostState = AgentsRunState.Failed;
        }
    }

    public void SetModuleError(string moduleId, string message)
    {
        lock (_gate)
        {
            _moduleLastErrors[moduleId] = message;
            _moduleStates[moduleId] = AgentsRunState.Failed;
        }
    }

    public void SetModuleState(string moduleId, AgentsRunState state)
    {
        lock (_gate)
        {
            _moduleStates[moduleId] = state;
        }
    }

    public void ClearModuleError(string moduleId)
    {
        lock (_gate)
        {
            _moduleLastErrors.Remove(moduleId);
        }
    }

    public void ClearModuleErrors()
    {
        lock (_gate)
        {
            _moduleLastErrors.Clear();
        }
    }

    public void NoteModuleLaunch(string moduleId, string? version)
    {
        lock (_gate)
        {
            _moduleLastErrors.Remove(moduleId);
            _moduleLastLaunchAt[moduleId] = DateTimeOffset.Now;
            if (!string.IsNullOrWhiteSpace(version))
            {
                _moduleVersions[moduleId] = version;
            }
        }
    }

    public void NoteHostLaunch(DateTimeOffset at, string hostVersion)
    {
        lock (_gate)
        {
            _hostLastLaunchAt = at;
            _hostLastError = null;
            _hostVersion = hostVersion;
        }
    }

    public void NoteHostReady(string hostVersion)
    {
        lock (_gate)
        {
            _hostLastError = null;
            _hostVersion = hostVersion;
        }
    }

    public void SetHostVersion(string hostVersion)
    {
        lock (_gate)
        {
            _hostVersion = hostVersion;
        }
    }

    public void MarkHostStarting()
    {
        lock (_gate)
        {
            _hostState = AgentsRunState.Starting;
            _hostLastError = null;
        }
    }

    public void ClearHostError()
    {
        lock (_gate)
        {
            _hostLastError = null;
        }
    }

    public AgentsRunState GetModuleState(string moduleId)
    {
        lock (_gate)
        {
            return _moduleStates.TryGetValue(moduleId, out var state)
                ? state
                : AgentsRunState.Unknown;
        }
    }

    public DateTimeOffset? GetModuleLastLaunchAt(string moduleId)
    {
        lock (_gate)
        {
            return _moduleLastLaunchAt.TryGetValue(moduleId, out var at) ? at : null;
        }
    }

    public string? GetModuleLastError(string moduleId)
    {
        lock (_gate)
        {
            return _moduleLastErrors.TryGetValue(moduleId, out var error) ? error : null;
        }
    }

    public string GetModuleVersion(string moduleId)
    {
        lock (_gate)
        {
            return _moduleVersions.TryGetValue(moduleId, out var v)
                ? v
                : "未配置";
        }
    }

    public void SetModuleErrorText(string moduleId, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_gate)
        {
            _moduleLastErrors[moduleId] = message;
        }
    }

    public void RefreshModuleVersions(string? agentsDir)
    {
        lock (_gate)
        {
            RefreshModuleVersionsUnlocked(agentsDir);
        }
    }

    public void RefreshModuleVersionsUnlocked(string? agentsDir)
    {
        _moduleVersions.Clear();
        if (agentsDir is null)
        {
            return;
        }

        foreach (var module in _modules)
        {
            _moduleVersions[module.Id] = AgentsDeployPaths.ReadModuleVersion(agentsDir, module.Id);
        }
    }

    /// <summary>
    /// 应用 Snapshot catalog；返回是否变更。调用方须已持锁或本方法内加锁
    /// </summary>
    public bool ApplyCatalogUnlocked(AgentsStatus status, string agentsDir)
    {
        if (status.Modules.Count == 0)
        {
            return false;
        }

        var scanned = AgentsStatusCatalog.ToDescriptors(status, agentsDir);
        if (AgentsPath.CatalogEquals(_modules, scanned))
        {
            return false;
        }

        _modules = scanned;
        return true;
    }

    /// <summary>
    /// 从链路 + 可选文件镜像解析 catalog（长 maxAge，仅 Reload / 启动前）
    /// </summary>
    public bool TryApplyCatalogFromCaches(
        string? agentsDir,
        IAgentsClient link,
        TimeSpan fileMaxAge)
    {
        if (agentsDir is null)
        {
            return false;
        }

        AgentsStatus? status;
        lock (_gate)
        {
            status = _lastSnapshot;
        }

        status ??= link.LastSnapshot
                   ?? AgentsStatus.TryRead(agentsDir, maxAge: fileMaxAge);
        if (status is null || status.Modules.Count == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (!ApplyCatalogUnlocked(status, agentsDir))
            {
                return false;
            }

            RefreshModuleVersionsUnlocked(agentsDir);
            return true;
        }
    }

    public AgentsStatus? ReadStatus(IAgentsClient link, string? agentsDir)
    {
        var fromLink = link.TryGetStatus(AgentsStatus.DefaultMaxAge);
        if (fromLink is not null)
        {
            return fromLink;
        }

        // 文件镜像：未连管道时的观测，不当控制面
        return AgentsStatus.TryRead(agentsDir, AgentsStatus.DefaultMaxAge);
    }

    public bool IsHostLive(IAgentsClient link, string? agentsDir)
        => ReadStatus(link, agentsDir) is not null || link.IsLinkConnected;

    public bool IsModuleAlive(IAgentsClient link, string? agentsDir, string moduleId)
        => ReadStatus(link, agentsDir)?.FindModule(moduleId) is { ProcessAlive: true };

    /// <returns>投影是否相对上次有可见变化</returns>
    public bool Refresh(
        AgentsOptions options,
        IAgentsClient link,
        AgentsHostLauncher host,
        AgentsDesiredSession desired,
        out bool catalogChanged)
    {
        catalogChanged = false;
        var agentsDir = AgentsDeployPaths.ResolveAgentsDir(options);
        var status = ReadStatus(link, agentsDir);

        if (status is not null && agentsDir is not null)
        {
            lock (_gate)
            {
                if (ApplyCatalogUnlocked(status, agentsDir))
                {
                    RefreshModuleVersionsUnlocked(agentsDir);
                    catalogChanged = true;
                }
            }
        }

        var hostState = DetectHostState(link, host, status);
        IReadOnlyList<ModuleDescriptor> modules;
        lock (_gate)
        {
            modules = _modules;
            _lastSnapshot = status;
            if (status is not null)
            {
                host.NoteHostPid(status.HostPid);
            }
        }

        var nextModules = new Dictionary<string, AgentsRunState>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            nextModules[module.Id] = DetectModuleState(module.Id, hostState, status, desired);
        }

        lock (_gate)
        {
            var errorsChanged = false;
            if (status is not null)
            {
                foreach (var module in modules)
                {
                    var err = status.FindModule(module.Id)?.LastError;
                    if (string.IsNullOrWhiteSpace(err))
                    {
                        continue;
                    }

                    if (!_moduleLastErrors.TryGetValue(module.Id, out var prev)
                        || !string.Equals(prev, err, StringComparison.Ordinal))
                    {
                        errorsChanged = true;
                    }

                    _moduleLastErrors[module.Id] = err;
                }
            }

            var equal = _hostState == hostState
                        && !catalogChanged
                        && !errorsChanged
                        && ModuleStatesEqual(_moduleStates, nextModules);
            if (equal)
            {
                return false;
            }

            _hostState = hostState;
            _moduleStates = nextModules;

            if (!hostState.IsActive())
            {
                host.ClearPid();
            }

            return true;
        }
    }

    private AgentsRunState DetectHostState(
        IAgentsClient link,
        AgentsHostLauncher host,
        AgentsStatus? status)
    {
        try
        {
            bool launching;
            bool localFail;
            lock (_gate)
            {
                launching = host.Launching;
                // 本地门禁/CreateProcess/Wait 失败；成功路径 NoteHostLaunch/ClearHostError 会清掉
                localFail = !string.IsNullOrWhiteSpace(_hostLastError) && !launching;
            }

            // 本地失败优先：status 文件镜像不得伪装 Running
            // 仅管道已连且 Host 自报 Running 时保留 wire（如停机失败但进程仍在）
            if (localFail)
            {
                if (link.IsLinkConnected
                    && status is { HostState: AgentsRunState.Running })
                {
                    return AgentsRunState.Running;
                }

                return AgentsRunState.Failed;
            }

            if (status is not null && status.HostState != AgentsRunState.Unknown)
            {
                // 仅有效 Snapshot 才清掉冷启 launching，避免 Unknown 帧抹掉 Starting
                host.ClearLaunching();
                return status.HostState;
            }

            var hostAlive = status is not null || link.IsLinkConnected;
            if (hostAlive)
            {
                return AgentsRunState.Running;
            }

            return launching ? AgentsRunState.Starting : AgentsRunState.Stopped;
        }
        catch
        {
            return AgentsRunState.Unknown;
        }
    }

    private AgentsRunState DetectModuleState(
        string moduleId,
        AgentsRunState hostState,
        AgentsStatus? status,
        AgentsDesiredSession desired)
    {
        try
        {
            var wire = status?.FindModule(moduleId);
            if (wire is not null)
            {
                if (wire.State == AgentsRunState.Running)
                {
                    ClearModuleError(moduleId);
                }

                return wire.State;
            }

            lock (_gate)
            {
                if (_moduleLastErrors.ContainsKey(moduleId))
                {
                    return AgentsRunState.Failed;
                }
            }

            if (desired.Contains(moduleId) && hostState.IsActive())
            {
                return AgentsRunState.Starting;
            }

            return AgentsRunState.Stopped;
        }
        catch
        {
            return AgentsRunState.Unknown;
        }
    }

    private static bool ModuleStatesEqual(
        Dictionary<string, AgentsRunState> left,
        Dictionary<string, AgentsRunState> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (id, state) in left)
        {
            if (!right.TryGetValue(id, out var other) || other != state)
            {
                return false;
            }
        }

        return true;
    }
}
