using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Desktop 侧编排：链路、HostLauncher、DesiredSession、SnapshotProjection
///
/// 模块进程监管在 Host；本类不实现 IAgentsClient
/// partial：.Host 启停，.Modules 挂载/库生命周期
/// </summary>
public sealed partial class AgentsRuntime : IAgentsRuntime
{
    private readonly IAppConfigStore _configStore;
    private readonly IModuleSettingsStore _moduleSettings;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAgentsAdmitService _admit;
    private readonly IAgentsBundleService _bundle;
    private readonly IDbConnectionMonitorService? _dbMonitor;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Timer _pollTimer;
    private static readonly TimeSpan HostCommandCooldown = TimeSpan.FromMilliseconds(1200);

    private readonly AgentsLink _link = new();
    private readonly AgentsHostLauncher _host;
    private readonly AgentsDesiredSession _desired;
    private readonly AgentsSnapshotProjection _projection;

    private AgentsOptions _options = new();
    private bool _disposed;
    private int _polling;
    private int _dbLifecycleBusy;
    private int _dbLifecycleWant = -1;
    private DateTimeOffset _lastHostCommandAt = DateTimeOffset.MinValue;
    private CancellationTokenSource? _startCts;
    private int _applyingBinaryChanges;
    private long _binaryRetryUtcTicks;

    public event Action? StatusChanged;

    public AgentsDescriptor Descriptor => AgentsDescriptors.Agents;

    public IReadOnlyList<ModuleDescriptor> Modules => _projection.Modules;

    public AgentsRunState HostState => _projection.HostState;

    public bool IsHostRunning => _projection.IsHostRunning;

    public DateTimeOffset? HostLastLaunchAt => _projection.HostLastLaunchAt;

    public string? HostLastError => _projection.HostLastError;

    public string HostVersion => _projection.HostVersion;

    public AgentsRuntime(
        IAppConfigStore configStore,
        IModuleSettingsStore moduleSettings,
        IReleaseVersionService releaseVersion,
        IAppLogger logger,
        IAgentsAdmitService admit,
        IAgentsBundleService bundle,
        IDbConnectionMonitorService? dbMonitor = null)
    {
        _configStore = configStore;
        _moduleSettings = moduleSettings ?? throw new ArgumentNullException(nameof(moduleSettings));
        _releaseVersion = releaseVersion;
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        _dbMonitor = dbMonitor;
        _logger = logger;
        _host = new AgentsHostLauncher(_gate, logger);
        _desired = new AgentsDesiredSession(_gate);
        _projection = new AgentsSnapshotProjection(_gate);

        Reload();

        _pollTimer = new Timer(_ => PollStatus(), null, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1));
        _link.SnapshotChanged += OnIpcStatusArrived;
        _link.ModuleFailed += OnModuleFailed;

        if (_dbMonitor is not null)
        {
            _dbMonitor.Disconnected += OnDatabaseDisconnected;
            _dbMonitor.Reconnected += OnDatabaseReconnected;
        }
    }


    public void Reload()
    {
        var cfg = _configStore.Load();
        var resolution = AgentsPath.ResolveHost(
            cfg.Agents.ExecutablePath,
            AppContext.BaseDirectory);

        var normalized = AgentsOptionsModel.FromResolution(cfg.Agents, resolution);

        bool optionsChanged;
        bool modulesChanged;
        lock (_gate)
        {
            optionsChanged = !AgentsOptionsModel.Same(_options, normalized);
            _options = AgentsOptionsModel.Clone(normalized);
            _projection.SetHostVersion(AgentsDeployPaths.ReadHostVersion(_options.ExecutablePath));
            modulesChanged = _projection.TryApplyCatalogFromCaches(
                ResolveAgentsDir(_options),
                _link,
                TimeSpan.FromMinutes(30));
            _host.SyncBinaryWatch(_options);
        }

        if (modulesChanged)
        {
            TryPersistNormalizedModules();
        }

        var statusChanged = RefreshState();
        if (optionsChanged || modulesChanged || statusChanged)
        {
            RaiseChanged();
        }

        _logger.Info("Agents", "agents.reload", "Agents runtime config reloaded", new
        {
            resolution.Source,
            normalized.ExecutablePath,
            normalized.ProcessName,
            Version = _projection.HostVersion,
            ModuleCount = Modules.Count,
            OptionsChanged = optionsChanged,
            ModulesChanged = modulesChanged
        });
    }

    public AgentsRunState GetModuleState(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return AgentsRunState.Unknown;
        }

        var state = _projection.GetModuleState(moduleId);
        return state == AgentsRunState.Unknown ? AgentsRunState.Stopped : state;
    }

    public bool IsModuleEnabled(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_options.Modules.TryGetValue(moduleId, out var module))
            {
                return module.Enabled;
            }

            return _projection.Modules.Any(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));
        }
    }

    public DateTimeOffset? GetModuleLastLaunchAt(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        return _projection.GetModuleLastLaunchAt(moduleId);
    }

    public string? GetModuleLastError(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        lock (_gate)
        {
            var host = _projection.LastSnapshot?.FindModule(moduleId)?.LastError;
            if (!string.IsNullOrWhiteSpace(host))
            {
                return host;
            }

            return _projection.GetModuleLastError(moduleId);
        }
    }

    public string GetModuleVersion(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return "未知";
        }

        return _projection.GetModuleVersion(moduleId);
    }


    private void PublishDesired(AgentsOptions options)
        => _desired.Publish(_link, ResolveAgentsDir(options), _logger);

    private void OnIpcStatusArrived()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (RefreshState())
            {
                RaiseChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Agents", "agents.ipc.status_apply_fail", "Failed to apply IPC status", ex);
        }
    }

    private void OnModuleFailed(string moduleId, string? message)
    {
        if (_disposed)
        {
            return;
        }

        _projection.SetModuleErrorText(moduleId, message);

        try
        {
            if (RefreshState())
            {
                RaiseChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Agents", "agents.ipc.module_failed_apply_fail", "Failed to apply ModuleFailed", ex);
        }
    }

    private void PollStatus()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
        {
            return;
        }

        try
        {
            var stateChanged = RefreshState();
            if (stateChanged)
            {
                RaiseChanged();
            }

            ScheduleStableBinaryChanges();
        }
        catch (Exception ex)
        {
            _logger.Warn("Agents", "agents.poll.fail", "Agents status poll failed", ex);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }


    private bool RefreshState()
    {
        AgentsOptions options;
        lock (_gate)
        {
            options = AgentsOptionsModel.Clone(_options);
        }

        var changed = _projection.Refresh(options, _link, _host, _desired, out var catalogChanged);
        if (catalogChanged)
        {
            TryPersistNormalizedModules();
        }

        return changed;
    }

    private AgentsStatus? ReadHostStatus(AgentsOptions options)
        => _projection.ReadStatus(_link, ResolveAgentsDir(options));

    private bool IsHostLive(AgentsOptions options)
        => _projection.IsHostLive(_link, ResolveAgentsDir(options));

    private bool IsModuleAlive(AgentsOptions options, string moduleId)
        => _projection.IsModuleAlive(_link, ResolveAgentsDir(options), moduleId);

    private AgentsCommandResult SetHostError(string message, bool log = true)
    {
        _projection.SetHostError(message);
        _host.ClearLaunching();

        // 本地 Host 门禁/启失败：无管道时丢弃 status 文件镜像，避免下一帧又读成 Running
        if (!_link.IsLinkConnected)
        {
            AgentsOptions options;
            lock (_gate)
            {
                options = AgentsOptionsModel.Clone(_options);
            }

            AgentsStatus.TryDelete(ResolveAgentsDir(options));
            _link.Disconnect();
        }

        if (log)
        {
            _logger.Warn("Agents", "agents.host.fail", message);
        }

        RefreshState();
        RaiseChanged();
        return new AgentsCommandResult(false, message);
    }

    private AgentsCommandResult SetModuleError(string moduleId, string message, bool log = true)
    {
        // 模块失败不改 Host launching：避免模块门禁失败抹掉 Host 启动中态
        _projection.SetModuleError(moduleId, message);

        if (log)
        {
            _logger.Warn("Agents", "agents.module.fail", message, context: new { moduleId });
        }

        RefreshState();
        RaiseChanged();
        return new AgentsCommandResult(false, message);
    }

    private bool IsHostCommandCoolingDown()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            return now - _lastHostCommandAt < HostCommandCooldown;
        }
    }

    private void NoteHostCommandIssued()
    {
        lock (_gate)
        {
            _lastHostCommandAt = DateTimeOffset.UtcNow;
        }
    }


    private static string? ResolveAgentsDir(AgentsOptions options)
        => AgentsDeployPaths.ResolveAgentsDir(options);

    private void RaiseChanged()
    {
        try
        {
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warn("Agents", "agents.status_changed.fail", "Agents status change handler failed", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Volatile.Write(ref _dbLifecycleWant, -1);
        CancelStart();

        if (_dbMonitor is not null)
        {
            _dbMonitor.Disconnected -= OnDatabaseDisconnected;
            _dbMonitor.Reconnected -= OnDatabaseReconnected;
        }

        try
        {
            _link.SnapshotChanged -= OnIpcStatusArrived;
            _link.ModuleFailed -= OnModuleFailed;
            _link.Dispose();
            _pollTimer.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warn("Agents", "agents.dispose.timer_fail", "Failed to dispose poll timer", ex);
        }
    }
}
