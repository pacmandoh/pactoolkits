using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Agents.Contracts.Validation;
using PacToolkits.Application.Abstractions;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

internal readonly record struct BinaryStamp(long Length, long LastWriteUtcTicks);

internal sealed class StableBinaryChange
{
    private BinaryStamp? _accepted;
    private BinaryStamp? _candidate;
    private int _candidateObservations;

    public bool Observe(BinaryStamp? stamp, bool active, out BinaryStamp changed)
    {
        changed = default;
        if (stamp is null)
        {
            _candidate = null;
            _candidateObservations = 0;
            return false;
        }

        if (_accepted is null || !active)
        {
            _accepted = stamp;
            _candidate = null;
            _candidateObservations = 0;
            return false;
        }

        if (_accepted == stamp)
        {
            _candidate = null;
            _candidateObservations = 0;
            return false;
        }

        if (_candidate != stamp)
        {
            _candidate = stamp;
            _candidateObservations = 1;
            return false;
        }

        _candidateObservations++;
        if (_candidateObservations < 2)
        {
            return false;
        }

        changed = stamp.Value;
        return true;
    }

    public bool Accept(BinaryStamp stamp)
    {
        if (_candidate != stamp)
        {
            return false;
        }

        _accepted = stamp;
        _candidate = null;
        _candidateObservations = 0;
        return true;
    }
}

/// <summary>
/// Agents 运行时
///
/// 负责 Host/模块启停、状态观测与进程兜底；不含业务注入逻辑
/// </summary>
public sealed class AgentsRuntime : IAgentsRuntime
{
    private readonly IAppConfigStore _configStore;
    private readonly IModuleSettingsStore _moduleSettings;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Timer _pollTimer;
    // Host 连点启停冷却；模块命令由闸门串行化，批量重挂不参与冷却
    private static readonly TimeSpan HostCommandCooldown = TimeSpan.FromMilliseconds(1200);

    private AgentsOptions _options = new();
    private AgentsRunState _hostState = AgentsRunState.Unknown;
    private IReadOnlyList<ModuleDescriptor> _modules = [];
    private Dictionary<string, AgentsRunState> _moduleStates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stoppedModules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _startingModules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _moduleLastErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _moduleLastLaunchAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _moduleVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BinaryWatch> _moduleBinaryWatches = new(StringComparer.Ordinal);
    private BinaryWatch? _hostBinaryWatch;
    private DateTimeOffset? _hostLastLaunchAt;
    private string? _hostLastError;
    private string _hostVersion = "未知";
    private bool _disposed;
    private int _polling;
    private DateTimeOffset _lastHostCommandAt = DateTimeOffset.MinValue;
    private int? _lastProcessId;
    private bool _starting;
    private CancellationTokenSource? _startCts;
    private int _applyingBinaryChanges;
    private long _binaryRetryUtcTicks;

    public event Action? StatusChanged;

    public AgentsDescriptor Descriptor => AgentsDescriptors.Agents;

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

    public string MinDbSchema
        => DbSchemaCompat.NormalizeBound(
            _releaseVersion.Current.AgentsMinDbSchema,
            _releaseVersion.Current.DbSchemaVersion);

    public string MaxDbSchema
        => DbSchemaCompat.NormalizeBound(
            _releaseVersion.Current.AgentsMaxDbSchema,
            _releaseVersion.Current.DbSchemaVersion);

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

    public bool IsHostRunning
    {
        get
        {
            lock (_gate)
            {
                return _hostState == AgentsRunState.Running;
            }
        }
    }

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

    public AgentsRuntime(
        IAppConfigStore configStore,
        IModuleSettingsStore moduleSettings,
        IReleaseVersionService releaseVersion,
        IDbSchemaVersionService dbSchemaVersion,
        IAppLogger logger)
    {
        _configStore = configStore;
        _moduleSettings = moduleSettings ?? throw new ArgumentNullException(nameof(moduleSettings));
        _releaseVersion = releaseVersion;
        _dbSchemaVersion = dbSchemaVersion;
        _logger = logger;
        Reload();

        // 启停态先快扫 300ms，稳态再 1s，兼顾及时性与负载
        _pollTimer = new Timer(_ => PollStatus(), null, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1));
    }

    public void Reload()
    {
        var cfg = _configStore.Load();
        var resolution = AgentsPath.ResolveHost(
            ResolveConfiguredPath(cfg),
            AppContext.BaseDirectory);

        var normalized = NormalizeFromResolution(cfg, resolution);

        bool optionsChanged;
        bool modulesChanged;
        lock (_gate)
        {
            optionsChanged = !Same(_options, normalized);
            _options = Clone(normalized);
            _hostVersion = ReadHostVersion(_options.ExecutablePath);
            modulesChanged = RediscoverModulesUnlocked(_options);
            SyncBinaryWatchesUnlocked(_options);
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
            Version = _hostVersion,
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

        lock (_gate)
        {
            return _moduleStates.TryGetValue(moduleId, out var state)
                ? state
                : AgentsRunState.Stopped;
        }
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

            // 已发现但配置尚未写回时沿用新模块默认启用规则
            return _modules.Any(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));
        }
    }

    public DateTimeOffset? GetModuleLastLaunchAt(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        lock (_gate)
        {
            return _moduleLastLaunchAt.TryGetValue(moduleId, out var at) ? at : null;
        }
    }

    public string? GetModuleLastError(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        lock (_gate)
        {
            return _moduleLastErrors.TryGetValue(moduleId, out var error) ? error : null;
        }
    }

    public string GetModuleVersion(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return "未知";
        }

        lock (_gate)
        {
            return _moduleVersions.TryGetValue(moduleId, out var version)
                ? version
                : "未配置";
        }
    }

    private void RefreshModuleVersionsUnlocked(AgentsOptions options)
    {
        _moduleVersions.Clear();
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        foreach (var module in _modules)
        {
            _moduleVersions[module.Id] = ReadModuleVersion(agentsDir, module.Id);
        }
    }

    public async Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default)
    {
        // WaitAsync(0)：已有命令在跑则立刻拒绝，避免排队叠启停
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
        {
            return new AgentsCommandResult(false, "操作进行中，请稍候", SuppressToast: true);
        }

        try
        {
            return await ExecuteStartOrRestartAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<AgentsCommandResult> ExecuteStartOrRestartAsync(CancellationToken ct)
    {
        CancellationTokenSource? startCts = null;
        try
        {
            if (IsHostCommandCoolingDown())
            {
                return new AgentsCommandResult(false, "操作过于频繁，已忽略", SuppressToast: true);
            }

            NoteHostCommandIssued();

            var schemaValidation = await ValidateSchemaCompatAsync(ct).ConfigureAwait(false);
            if (!schemaValidation.Ok)
            {
                return SetHostError(schemaValidation.Message);
            }

            AgentsOptions options;
            bool modulesChanged;
            lock (_gate)
            {
                options = Clone(_options);
                // 启停前再 reconcile，避免刚落盘的模块仍卡在上一轮 poll 缓存
                modulesChanged = RediscoverModulesUnlocked(options);
            }

            if (modulesChanged)
            {
                TryPersistNormalizedModules();
            }

            if (!OperatingSystem.IsWindows())
            {
                return SetHostError("当前系统不支持启动 Agents");
            }

            if (string.IsNullOrWhiteSpace(options.ExecutablePath))
            {
                return SetHostError("请先配置 Agents 可执行文件路径");
            }

            var resolvedExePath = ResolveExecutablePath(options.ExecutablePath);
            if (resolvedExePath is null)
            {
                return SetHostError($"路径无效：{options.ExecutablePath}");
            }

            if (!File.Exists(resolvedExePath))
            {
                return SetHostError($"文件不存在：{options.ExecutablePath}");
            }

            var validate = ValidateHostLaunch();
            if (!validate.Ok)
            {
                return SetHostError(validate.Message);
            }

            // 重启判定看进程是否仍在，而非 State——Failed 时 Host 仍可能存活
            var wasActive = GetTargetProcesses(options).Any();
            if (wasActive)
            {
                CancelStart();
                WriteHostControl(options, "quit");
                ClearAllModuleReady(options);
                var stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
                if (!stopped)
                {
                    StopTargetProcesses(options);
                    stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                }

                if (!stopped)
                {
                    return SetHostError("重启失败：检测到进程仍在运行，已取消本次启动");
                }

                ClearHostControl(options);
                ClearAllModuleControl(options);

                // 给 shell 托盘一点时间移除图标，再重新拉起
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedExePath,
                Arguments = BuildConfigArguments(_configStore.ConfigPath),
                WorkingDirectory = Path.GetDirectoryName(resolvedExePath) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
            };

            ClearHostControl(options);
            ClearAllModuleControl(options);
            ClearAllModuleReady(options);
            lock (_gate)
            {
                _stoppedModules.Clear();
                _moduleLastErrors.Clear();
                _startingModules.Clear();
            }

            MarkStarting();
            RaiseChanged();

            Process? startedProcess = Process.Start(startInfo);
            if (startedProcess is null)
            {
                return SetHostError("启动失败：无法创建 Agents 进程");
            }

            var launchedAt = DateTimeOffset.Now;
            lock (_gate)
            {
                _lastProcessId = startedProcess.Id;
                _hostLastLaunchAt = launchedAt;
                _hostLastError = null;
                _hostVersion = ReadHostVersion(_options.ExecutablePath);
                RefreshModuleVersionsUnlocked(_options);
            }

            RaiseChanged();

            startCts = new CancellationTokenSource();
            _startCts = startCts;
            // 闸门保持到挂载结束：避免冷却窗口内叠启；Stop 先 CancelStart 再抢闸

            IReadOnlyList<ModuleDescriptor> modules;
            lock (_gate)
            {
                modules = _modules;
            }

            var enabledIds = modules
                .Select(m => m.Id)
                .Where(IsModuleEnabled)
                .ToList();

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, startCts.Token);

                var up = await WaitUntilHostUpAsync(options, TimeSpan.FromSeconds(4), linked.Token)
                    .ConfigureAwait(false);
                if (!up)
                {
                    return SetHostError("已触发启动，但未检测到 Agents 进程");
                }

                foreach (var moduleId in enabledIds)
                {
                    var mount = await MountModuleAsync(options, moduleId, linked.Token).ConfigureAwait(false);
                    if (!mount.Ok)
                    {
                        return mount;
                    }
                }

                lock (_gate)
                {
                    _starting = false;
                    _hostLastError = null;
                    _hostVersion = ReadHostVersion(_options.ExecutablePath);
                    RefreshModuleVersionsUnlocked(_options);
                }

                RefreshState();
                RaiseChanged();
                var mounted = enabledIds.Count > 0;
                return new AgentsCommandResult(
                    true,
                    wasActive
                        ? (mounted ? "已重启" : "Agents 已重启")
                        : (mounted ? "已启动" : "Agents 已启动"));
            }
            catch (OperationCanceledException)
            {
                StopTargetProcesses(options);
                lock (_gate)
                {
                    _starting = false;
                    _startingModules.Clear();
                }

                RefreshState();
                RaiseChanged();
                return new AgentsCommandResult(true, "已取消", SuppressToast: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.start_or_restart.fail", "Agents start/restart failed", ex);
            return SetHostError($"启动失败：{ex.Message}");
        }
        finally
        {
            if (startCts is not null)
            {
                if (ReferenceEquals(_startCts, startCts))
                {
                    _startCts = null;
                }

                startCts.Dispose();
            }

        }
    }

    public async Task<AgentsCommandResult> StopAsync(CancellationToken ct = default)
    {
        // 先取消在途启动，避免 Start 占闸导致 Stop 无法进入
        CancelStart();

        await _commandGate.WaitAsync(ct).ConfigureAwait(false);

        AgentsOptions options;
        try
        {
            // Stop 在取消在途启动后必须落地，不能再被启动命令的冷却窗口吞掉
            NoteHostCommandIssued();

            lock (_gate)
            {
                options = Clone(_options);
            }

            if (!OperatingSystem.IsWindows())
            {
                return SetHostError("当前系统不支持停止 Agents");
            }

            var processes = GetTargetProcesses(options).ToList();
            if (processes.Count == 0)
            {
                lock (_gate)
                {
                    _hostLastError = null;
                    _moduleLastErrors.Clear();
                    _lastProcessId = null;
                    _starting = false;
                    _stoppedModules.Clear();
                    _startingModules.Clear();
                }
                RefreshState();
                RaiseChanged();
                return new AgentsCommandResult(true, "已停止");
            }

            // 请常驻 Host 卸载全部模块并退出；kill 仍作兜底
            WriteHostControl(options, "quit");
            ClearAllModuleReady(options);

            var stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            if (!stopped)
            {
                foreach (var p in processes)
                {
                    try
                    {
                        TryTerminateProcess(p);
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                if (!stopped)
                {
                    StopTargetProcesses(options);
                    stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                }
            }
            else
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }

            if (!stopped)
            {
                return SetHostError("停止失败：检测到进程仍在运行");
            }

            ClearHostControl(options);
            ClearAllModuleControl(options);

            // 进程退出后给 shell 清理托盘图标缓存的时间
            await Task.Delay(250, ct).ConfigureAwait(false);

            lock (_gate)
            {
                _hostLastError = null;
                _moduleLastErrors.Clear();
                _lastProcessId = null;
                _starting = false;
                _stoppedModules.Clear();
                _startingModules.Clear();
            }

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, "已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.stop.fail", "Agents stop failed", ex);
            return SetHostError($"停止失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<AgentsCommandResult> StopModuleAsync(string moduleId, CancellationToken ct = default)
    {
        if (!AgentsPath.IsValidModuleId(moduleId))
        {
            return new AgentsCommandResult(false, "模块 id 无效");
        }

        await _commandGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return SetModuleError(moduleId, $"当前系统不支持停止 {moduleId}");
            }

            AgentsOptions options;
            lock (_gate)
            {
                options = Clone(_options);
                _moduleLastErrors.Remove(moduleId);
            }

            if (!await EnsureModuleStoppedAsync(options, moduleId, ct).ConfigureAwait(false))
            {
                return SetModuleError(moduleId, $"停止失败：{moduleId} 进程仍在运行");
            }

            lock (_gate)
            {
                // Host 仍在但尚无 module.ready 时，先不要标成 Starting
                _stoppedModules.Add(moduleId);
                _startingModules.Remove(moduleId);
                _moduleLastErrors.Remove(moduleId);
            }

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, $"{moduleId} 已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.module_stop.fail", "Module stop failed", ex, new { moduleId });
            return SetModuleError(moduleId, $"停止失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<AgentsCommandResult> StartModuleAsync(string moduleId, CancellationToken ct = default)
    {
        if (!AgentsPath.IsValidModuleId(moduleId))
        {
            return new AgentsCommandResult(false, "模块 id 无效");
        }

        await _commandGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return SetModuleError(moduleId, $"当前系统不支持启动 {moduleId}");
            }

            if (!IsModuleEnabled(moduleId))
            {
                return SetModuleError(moduleId, $"请先启用 {moduleId}");
            }

            AgentsOptions options;
            IReadOnlyList<ModuleDescriptor> modules;
            bool modulesChanged;
            lock (_gate)
            {
                options = Clone(_options);
                modulesChanged = RediscoverModulesUnlocked(options);
                modules = _modules;
            }

            if (modulesChanged)
            {
                TryPersistNormalizedModules();
            }

            if (modules.All(m => !string.Equals(m.Id, moduleId, StringComparison.Ordinal)))
            {
                return SetModuleError(moduleId, $"未发现模块：{moduleId}");
            }

            var entryPath = ResolveModuleEntryPath(options, moduleId);
            if (string.IsNullOrWhiteSpace(entryPath) || !File.Exists(entryPath))
            {
                return SetModuleError(moduleId, $"模块入口缺失：{entryPath ?? moduleId}");
            }

            var validate = ValidateModuleSettings(moduleId);
            if (!validate.Ok)
            {
                return SetModuleError(moduleId, validate.Message);
            }

            // Host 未挂时在同一闸门内完成全量启动，避免释放再抢占造成启停穿插
            if (!GetTargetProcesses(options).Any())
            {
                return await ExecuteStartOrRestartAsync(ct).ConfigureAwait(false);
            }

            var mount = await MountModuleAsync(options, moduleId, ct).ConfigureAwait(false);
            if (!mount.Ok)
            {
                return mount;
            }

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, $"{moduleId} 已启动");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.module_start.fail", "Module start failed", ex, new { moduleId });
            return SetModuleError(moduleId, $"启动失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    /// <summary>
    /// 在 Host 已运行前提下挂载模块（先 stop 再 start，等 ready）
    /// </summary>
    private async Task<AgentsCommandResult> MountModuleAsync(
        AgentsOptions options,
        string moduleId,
        CancellationToken ct)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return SetModuleError(moduleId, "无法解析 Agents 目录，无法准备模块 settings");
        }

        try
        {
            _moduleSettings.EnsureUserSettings(moduleId, agentsDir);
        }
        catch (Exception ex)
        {
            return SetModuleError(moduleId, $"模块 settings 准备失败：{ex.Message}");
        }

        if (!await EnsureModuleStoppedAsync(options, moduleId, ct).ConfigureAwait(false))
        {
            return SetModuleError(moduleId, $"重启失败：{moduleId} 进程仍在运行");
        }

        lock (_gate)
        {
            _stoppedModules.Remove(moduleId);
            _moduleLastErrors.Remove(moduleId);
            _startingModules.Add(moduleId);
            _moduleStates[moduleId] = AgentsRunState.Starting;
        }

        RaiseChanged();
        WriteModuleControl(options, moduleId, "start");

        bool ready;
        try
        {
            ready = await WaitUntilModuleReadyAsync(options, moduleId, TimeSpan.FromSeconds(8), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await EnsureModuleStoppedAsync(options, moduleId, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                _startingModules.Remove(moduleId);
            }

            throw;
        }

        if (!ready)
        {
            await EnsureModuleStoppedAsync(options, moduleId, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                _startingModules.Remove(moduleId);
            }

            return SetModuleError(moduleId, $"已请求启动 {moduleId}，但未完成自检（配置错误或启动失败）");
        }

        lock (_gate)
        {
            _startingModules.Remove(moduleId);
            _moduleLastErrors.Remove(moduleId);
            _moduleLastLaunchAt[moduleId] = DateTimeOffset.Now;
            _moduleVersions[moduleId] = ReadModuleVersion(agentsDir, moduleId);
        }

        return new AgentsCommandResult(true, $"{moduleId} 已挂载");
    }

    private void PollStatus()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
        {
            return;
        }

        try
        {
            // 与观测同周期 reconcile 磁盘 Modules；清单变化才写回配置 Normalized 开关
            bool modulesChanged;
            lock (_gate)
            {
                modulesChanged = RediscoverModulesUnlocked(_options);
            }

            if (modulesChanged)
            {
                TryPersistNormalizedModules();
            }

            var stateChanged = RefreshState();
            if (modulesChanged || stateChanged)
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

    private void TryPersistNormalizedModules()
    {
        try
        {
            // Load → NormalizeModules → PersistIfChanged：稳定非空清单才增补新 id 并清理孤儿
            _ = _configStore.Load();
        }
        catch (Exception ex)
        {
            _logger.Warn(
                "Agents",
                "agents.modules.normalize_fail",
                "Failed to normalize Agents.Modules after rediscovery",
                ex);
        }
    }

    private bool RediscoverModulesUnlocked(AgentsOptions options)
    {
        var agentsDir = ResolveAgentsDir(options);
        IReadOnlyList<ModuleDescriptor> scanned = agentsDir is null
            ? []
            : AgentsPath.ScanModules(agentsDir);

        if (AgentsPath.CatalogEquals(_modules, scanned))
        {
            return false;
        }

        _modules = scanned;
        RefreshModuleVersionsUnlocked(options);
        _logger.Info("Agents", "agents.modules.rediscover", "Agents modules catalog changed", new
        {
            ModuleCount = _modules.Count,
            ModuleIds = _modules.Select(m => m.Id).ToArray(),
        });
        return true;
    }

    private void ScheduleStableBinaryChanges()
    {
        if (_disposed
            || DateTime.UtcNow.Ticks < Volatile.Read(ref _binaryRetryUtcTicks)
            || Interlocked.CompareExchange(ref _applyingBinaryChanges, 1, 0) != 0)
        {
            return;
        }

        BinaryChangeBatch? batch;
        lock (_gate)
        {
            SyncBinaryWatchesUnlocked(_options);
            batch = DetectStableBinaryChangesUnlocked();
        }

        if (batch is null)
        {
            Volatile.Write(ref _applyingBinaryChanges, 0);
            return;
        }

        TaskObserve.Observe(
            ApplyBinaryChangesAsync(batch),
            "Agents",
            "agents.binary_change.detached.fail");
    }

    private BinaryChangeBatch? DetectStableBinaryChangesUnlocked()
    {
        BinaryStamp? host = null;
        if (_hostBinaryWatch is not null
            && _hostBinaryWatch.Change.Observe(
                TryReadBinaryStamp(_hostBinaryWatch.Path),
                IsBinaryReloadActive(_hostState),
                out var hostChanged))
        {
            host = hostChanged;
        }

        var modules = new Dictionary<string, BinaryStamp>(StringComparer.Ordinal);
        foreach (var (moduleId, watch) in _moduleBinaryWatches)
        {
            var active = _moduleStates.TryGetValue(moduleId, out var state)
                         && IsBinaryReloadActive(state);
            if (watch.Change.Observe(TryReadBinaryStamp(watch.Path), active, out var changed))
            {
                modules[moduleId] = changed;
            }
        }

        return host is null && modules.Count == 0
            ? null
            : new BinaryChangeBatch(host, modules);
    }

    internal static bool IsBinaryReloadActive(AgentsRunState state)
        => state is AgentsRunState.Running or AgentsRunState.Starting;

    private async Task ApplyBinaryChangesAsync(BinaryChangeBatch batch)
    {
        var retry = false;
        try
        {
            if (_disposed)
            {
                return;
            }

            if (batch.Host is BinaryStamp hostStamp)
            {
                var result = await StartOrRestartAsync().ConfigureAwait(false);
                if (!result.Ok)
                {
                    retry = true;
                    _logger.Warn(
                        "Agents",
                        "agents.host_binary.reload_fail",
                        "Failed to restart Agents after Host binary changed",
                        context: new { result.Message });
                    return;
                }

                lock (_gate)
                {
                    _hostBinaryWatch?.Change.Accept(hostStamp);
                    foreach (var (moduleId, stamp) in batch.Modules)
                    {
                        if (_moduleBinaryWatches.TryGetValue(moduleId, out var watch))
                        {
                            watch.Change.Accept(stamp);
                        }
                    }
                }

                _logger.Info(
                    "Agents",
                    "agents.host_binary.reloaded",
                    "Restarted Agents after Host binary changed");
                return;
            }

            var changedModules = batch.Modules.ToList();
            for (var index = 0; index < changedModules.Count; index++)
            {
                if (_disposed)
                {
                    return;
                }

                var (moduleId, stamp) = changedModules[index];
                if (GetModuleState(moduleId) != AgentsRunState.Running)
                {
                    continue;
                }

                var result = await StartModuleAsync(moduleId).ConfigureAwait(false);
                if (!result.Ok)
                {
                    retry = true;
                    _logger.Warn(
                        "Agents",
                        "agents.module_binary.reload_fail",
                        "Failed to reload module after binary changed",
                        context: new { moduleId, result.Message });
                    continue;
                }

                lock (_gate)
                {
                    if (_moduleBinaryWatches.TryGetValue(moduleId, out var watch))
                    {
                        watch.Change.Accept(stamp);
                    }
                }

                _logger.Info(
                    "Agents",
                    "agents.module_binary.reloaded",
                    "Reloaded module after binary changed",
                    new { moduleId });

            }
        }
        finally
        {
            if (retry)
            {
                Volatile.Write(ref _binaryRetryUtcTicks, DateTime.UtcNow.AddSeconds(3).Ticks);
            }

            Volatile.Write(ref _applyingBinaryChanges, 0);
        }
    }

    private void SyncBinaryWatchesUnlocked(AgentsOptions options)
    {
        var hostPath = ResolveExecutablePath(options.ExecutablePath);
        _hostBinaryWatch = SyncBinaryWatch(_hostBinaryWatch, hostPath);

        var wantedIds = new HashSet<string>(_modules.Select(module => module.Id), StringComparer.Ordinal);
        foreach (var staleId in _moduleBinaryWatches.Keys.Where(id => !wantedIds.Contains(id)).ToList())
        {
            _moduleBinaryWatches.Remove(staleId);
        }

        foreach (var module in _modules)
        {
            var path = ResolveModuleEntryPath(options, module.Id);
            _moduleBinaryWatches.TryGetValue(module.Id, out var existing);
            var watch = SyncBinaryWatch(existing, path);
            if (watch is null)
            {
                _moduleBinaryWatches.Remove(module.Id);
            }
            else
            {
                _moduleBinaryWatches[module.Id] = watch;
            }
        }
    }

    private static BinaryWatch? SyncBinaryWatch(BinaryWatch? existing, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = Path.GetFullPath(path);
        if (existing is not null && string.Equals(existing.Path, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        var change = new StableBinaryChange();
        change.Observe(TryReadBinaryStamp(normalized), active: false, out _);
        return new BinaryWatch(normalized, change);
    }

    private static BinaryStamp? TryReadBinaryStamp(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new BinaryStamp(file.Length, file.LastWriteTimeUtc.Ticks)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private bool RefreshState()
    {
        AgentsOptions options;
        IReadOnlyList<ModuleDescriptor> modules;
        lock (_gate)
        {
            options = Clone(_options);
            modules = _modules;
        }

        var hostState = DetectHostState(options);
        var nextModules = new Dictionary<string, AgentsRunState>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            nextModules[module.Id] = DetectModuleState(module.Id, options, hostState);
        }

        lock (_gate)
        {
            if (_hostState == hostState && ModuleStatesEqual(_moduleStates, nextModules))
            {
                return false;
            }

            _hostState = hostState;
            _moduleStates = nextModules;
            if (!hostState.IsActive())
            {
                _lastProcessId = null;
            }

            return true;
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

    private AgentsRunState DetectHostState(AgentsOptions options)
    {
        try
        {
            var hasProcess = GetTargetProcesses(options).Any();

            if (hasProcess)
            {
                return AgentsRunState.Running;
            }

            lock (_gate)
            {
                if (_starting)
                {
                    return AgentsRunState.Starting;
                }

                if (_hostState == AgentsRunState.Failed
                    && !string.IsNullOrWhiteSpace(_hostLastError))
                {
                    return AgentsRunState.Failed;
                }
            }

            return AgentsRunState.Stopped;
        }
        catch
        {
            return AgentsRunState.Unknown;
        }
    }

    private AgentsRunState DetectModuleState(string moduleId, AgentsOptions options, AgentsRunState hostState)
    {
        try
        {
            lock (_gate)
            {
                if (_stoppedModules.Contains(moduleId))
                {
                    return AgentsRunState.Stopped;
                }
            }

            if (hostState is AgentsRunState.Stopped or AgentsRunState.Unknown)
            {
                return AgentsRunState.Stopped;
            }

            if (hostState == AgentsRunState.Failed)
            {
                lock (_gate)
                {
                    return _moduleLastErrors.TryGetValue(moduleId, out var error)
                           && !string.IsNullOrWhiteSpace(error)
                        ? AgentsRunState.Failed
                        : AgentsRunState.Stopped;
                }
            }

            var alive = AnyModuleProcess(options, moduleId);
            var ready = ModuleReadyExists(options, moduleId);
            if (ready && !alive)
            {
                // Host 常驻下 crash/kill 后可能残留过期 marker
                ClearModuleReady(options, moduleId);
                ready = false;
            }

            if (hostState == AgentsRunState.Running && ready && alive)
            {
                lock (_gate)
                {
                    _moduleLastErrors.Remove(moduleId);
                    _startingModules.Remove(moduleId);
                }

                return AgentsRunState.Running;
            }

            lock (_gate)
            {
                if (_moduleStates.TryGetValue(moduleId, out var previous)
                    && previous == AgentsRunState.Failed
                    && _moduleLastErrors.TryGetValue(moduleId, out var error)
                    && !string.IsNullOrWhiteSpace(error))
                {
                    return AgentsRunState.Failed;
                }

                // 仅在 start/remount 进行中，或进程已起但尚未 ready 时标 Starting
                if (_startingModules.Contains(moduleId) || (alive && !ready))
                {
                    return AgentsRunState.Starting;
                }
            }

            return AgentsRunState.Stopped;
        }
        catch
        {
            return AgentsRunState.Unknown;
        }
    }

    private IEnumerable<Process> GetTargetProcesses(AgentsOptions options)
    {
        int? trackedPid;
        lock (_gate)
        {
            trackedPid = _lastProcessId;
        }

        var normalizedExePath = NormalizePath(options.ExecutablePath);
        var candidates = ResolveProcessNameCandidates(options);
        if (candidates.Count == 0)
        {
            return Array.Empty<Process>();
        }

        var seen = new HashSet<int>();
        var matched = new List<Process>();

        foreach (var processName in candidates)
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                if (!seen.Add(p.Id))
                {
                    p.Dispose();
                    continue;
                }

                if (trackedPid is int pid && p.Id == pid)
                {
                    if (MatchesExe(p, normalizedExePath, permissiveOnAccessDenied: false))
                    {
                        matched.Add(p);
                    }
                    else
                    {
                        ClearTrackedProcessId(pid);
                        p.Dispose();
                    }

                    continue;
                }

                if (normalizedExePath is null)
                {
                    matched.Add(p);
                    continue;
                }

                if (MatchesExe(p, normalizedExePath, permissiveOnAccessDenied: true))
                {
                    matched.Add(p);
                }
                else
                {
                    p.Dispose();
                }
            }
        }

        return matched;
    }

    private void ClearTrackedProcessId(int processId)
    {
        lock (_gate)
        {
            if (_lastProcessId == processId)
            {
                _lastProcessId = null;
            }
        }
    }

    internal static bool MatchesExe(
        Process process,
        string? normalizedExePath,
        bool permissiveOnAccessDenied)
    {
        if (normalizedExePath is null)
        {
            return true;
        }

        try
        {
            if (process.HasExited)
            {
                return false;
            }

            var normalizedModulePath = NormalizePath(process.MainModule?.FileName);
            if (normalizedModulePath is null)
            {
                return false;
            }

            if (string.Equals(normalizedModulePath, normalizedExePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return permissiveOnAccessDenied;
        }
    }

    private static IReadOnlyList<string> ResolveProcessNameCandidates(AgentsOptions options)
    {
        var names = new List<string>(3);

        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var name = Path.GetFileNameWithoutExtension(raw.Trim());
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (!names.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(name);
            }
        }

        Add(options.ProcessName);
        Add(options.ExecutablePath);
        Add(AgentsPaths.HostProcessName);

        return names;
    }

    private AgentsCommandResult SetHostError(string message)
    {
        lock (_gate)
        {
            _hostLastError = message;
            _starting = false;
            _startingModules.Clear();
            _hostState = AgentsRunState.Failed;
        }

        RefreshState();
        RaiseChanged();
        return new AgentsCommandResult(false, message);
    }

    private AgentsCommandResult SetModuleError(string moduleId, string message)
    {
        lock (_gate)
        {
            _moduleLastErrors[moduleId] = message;
            _starting = false;
            // 全量启动失败时 sibling 也不得留在 Starting
            _startingModules.Clear();
            _moduleStates[moduleId] = AgentsRunState.Failed;
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
            // 与顶栏 toast debounce 同窗口，防连点叠启停
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

    private void MarkStarting(bool host = true)
    {
        CancelStart();
        lock (_gate)
        {
            _starting = true;
            if (host)
            {
                _hostState = AgentsRunState.Starting;
                _hostLastError = null;
            }

            // 模块 Starting 由 MountModuleAsync 在真正发 start 时写入，避免 Host 未起就显示「模块启动中」
        }
    }

    private void CancelStart()
    {
        var cts = _startCts;
        _startCts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        lock (_gate)
        {
            _starting = false;
            _startingModules.Clear();
        }
    }

    private async Task<bool> WaitUntilHostUpAsync(AgentsOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (GetTargetProcesses(options).Any())
            {
                return true;
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        return GetTargetProcesses(options).Any();
    }

    private async Task<bool> WaitUntilModuleReadyAsync(
        AgentsOptions options,
        string moduleId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (ModuleReadyExists(options, moduleId))
            {
                return true;
            }

            if (!GetTargetProcesses(options).Any())
            {
                return false;
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        return ModuleReadyExists(options, moduleId);
    }

    private async Task<bool> WaitUntilStoppedAsync(AgentsOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (!GetTargetProcesses(options).Any())
            {
                return true;
            }

            await Task.Delay(180, ct).ConfigureAwait(false);
        }

        return !GetTargetProcesses(options).Any();
    }

    private void StopTargetProcesses(AgentsOptions options)
    {
        foreach (var p in GetTargetProcesses(options))
        {
            try
            {
                TryTerminateProcess(p);
            }
            catch (System.Exception ex)
            {
                _logger.Warn("Agents", "agents.terminate.fail", "Failed to terminate Agents process", ex);
            }
            finally
            {
                p.Dispose();
            }
        }

        ClearAllModuleReady(options);
    }

    private static string? ResolveModuleReadyPath(AgentsOptions options, string moduleId)
    {
        var agentsDir = ResolveAgentsDir(options);
        return agentsDir is null
            ? null
            : AgentsPaths.ModuleReadyPath(agentsDir, moduleId);
    }

    private static string? ResolveAgentsDir(AgentsOptions options)
    {
        // 与 AppConfigStore.NormalizeModules 同一 ResolveHost，避免扫描目录与开关写回漂移
        var resolution = AgentsPath.ResolveHost(options.ExecutablePath, AppContext.BaseDirectory);
        if (resolution.ResolvedPath is null)
        {
            return null;
        }

        var agentsDir = Path.GetDirectoryName(resolution.ResolvedPath);
        return string.IsNullOrWhiteSpace(agentsDir) ? null : agentsDir;
    }

    private static string? ResolveModuleEntryPath(AgentsOptions options, string moduleId)
    {
        var agentsDir = ResolveAgentsDir(options);
        return agentsDir is null
            ? null
            : AgentsPath.TryResolveModuleEntryPath(agentsDir, moduleId);
    }

    private static void WriteHostControl(AgentsOptions options, string command)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        var path = AgentsPaths.HostControlPath(agentsDir);
        try
        {
            Directory.CreateDirectory(agentsDir);
            File.WriteAllText(path, command + Environment.NewLine);
        }
        catch
        {
            // Host 仍可能走进程 kill 兜底停止
        }
    }

    private static void ClearHostControl(AgentsOptions options)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        TryDeleteFile(AgentsPaths.HostControlPath(agentsDir));
    }

    private static void WriteModuleControl(AgentsOptions options, string moduleId, string command)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        var path = AgentsPaths.ModuleControlPath(agentsDir, moduleId);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, command + Environment.NewLine);
        }
        catch
        {
            // Host 仍可能走进程 kill 兜底停止
        }
    }

    private void ClearAllModuleControl(AgentsOptions options)
    {
        IReadOnlyList<ModuleDescriptor> modules;
        lock (_gate)
        {
            modules = _modules;
        }

        foreach (var module in modules)
        {
            ClearModuleControl(options, module.Id);
        }
    }

    private static void ClearModuleControl(AgentsOptions options, string moduleId)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        TryDeleteFile(AgentsPaths.ModuleControlPath(agentsDir, moduleId));
    }

    private IEnumerable<Process> GetModuleProcesses(AgentsOptions options, string moduleId)
    {
        var entryPath = ResolveModuleEntryPath(options, moduleId);
        if (entryPath is null || !File.Exists(entryPath))
        {
            return Array.Empty<Process>();
        }

        var processName = Path.GetFileNameWithoutExtension(entryPath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Array.Empty<Process>();
        }

        var normalizedEntry = NormalizePath(entryPath);
        var matched = new List<Process>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (MatchesExe(p, normalizedEntry, permissiveOnAccessDenied: true))
            {
                matched.Add(p);
            }
            else
            {
                p.Dispose();
            }
        }

        return matched;
    }

    private void StopModuleProcesses(AgentsOptions options, string moduleId)
    {
        foreach (var p in GetModuleProcesses(options, moduleId))
        {
            try
            {
                TryTerminateProcess(p);
            }
            catch (Exception ex)
            {
                _logger.Warn("Agents", "agents.module_terminate.fail", "Failed to terminate module process", ex, new { moduleId });
            }
            finally
            {
                p.Dispose();
            }
        }

        ClearModuleReady(options, moduleId);
    }

    private async Task<bool> EnsureModuleStoppedAsync(AgentsOptions options, string moduleId, CancellationToken ct)
    {
        ClearModuleReady(options, moduleId);
        if (!AnyModuleProcess(options, moduleId))
        {
            ClearModuleControl(options, moduleId);
            return true;
        }

        WriteModuleControl(options, moduleId, "stop");
        var stopped = await WaitUntilModuleStoppedAsync(options, moduleId, TimeSpan.FromSeconds(4), ct)
            .ConfigureAwait(false);
        if (!stopped)
        {
            StopModuleProcesses(options, moduleId);
            stopped = await WaitUntilModuleStoppedAsync(options, moduleId, TimeSpan.FromSeconds(3), ct)
                .ConfigureAwait(false);
        }

        ClearModuleControl(options, moduleId);
        ClearModuleReady(options, moduleId);
        return stopped;
    }

    private async Task<bool> WaitUntilModuleStoppedAsync(
        AgentsOptions options,
        string moduleId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            if (!AnyModuleProcess(options, moduleId))
            {
                return true;
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return !AnyModuleProcess(options, moduleId);
    }

    private bool AnyModuleProcess(AgentsOptions options, string moduleId)
    {
        var list = GetModuleProcesses(options, moduleId).ToList();
        try
        {
            return list.Count > 0;
        }
        finally
        {
            foreach (var p in list)
            {
                p.Dispose();
            }
        }
    }

    private static bool ModuleReadyExists(AgentsOptions options, string moduleId)
    {
        var path = ResolveModuleReadyPath(options, moduleId);
        return path is not null && File.Exists(path);
    }

    private void ClearAllModuleReady(AgentsOptions options)
    {
        IReadOnlyList<ModuleDescriptor> modules;
        lock (_gate)
        {
            modules = _modules;
        }

        foreach (var module in modules)
        {
            ClearModuleReady(options, module.Id);
        }
    }

    private static void ClearModuleReady(AgentsOptions options, string moduleId)
    {
        var path = ResolveModuleReadyPath(options, moduleId);
        if (path is null || !File.Exists(path))
        {
            return;
        }

        TryDeleteFile(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 就绪标记清理失败不阻断停止兜底
        }
    }

    private void TryTerminateProcess(Process p)
    {
        if (p.HasExited)
        {
            return;
        }

        try
        {
            // 优先优雅结束，让托盘图标有机会干净 dispose
            if (p.CloseMainWindow())
            {
                if (p.WaitForExit(2200))
                {
                    return;
                }
            }
        }
        catch (System.Exception ex)
        {
            _logger.Warn("Agents", "agents.close_window.fail", "Failed to close Agents process window gracefully", ex);
        }

        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2500);
            }
        }
        catch (System.Exception ex)
        {
            _logger.Warn("Agents", "agents.kill.fail", "Failed to kill Agents process", ex);
        }
    }

    private static AgentsOptions Clone(AgentsOptions src) => new()
    {
        ExecutablePath = src.ExecutablePath,
        ProcessName = src.ProcessName,
        Modules = src.Modules.ToDictionary(
            static pair => pair.Key,
            static pair => new ModuleOptions { Enabled = pair.Value.Enabled },
            StringComparer.Ordinal),
    };

    private static string? ResolveConfiguredPath(AppConfigRoot cfg)
        => cfg.Agents.ExecutablePath;

    private static AgentsOptions NormalizeFromResolution(AppConfigRoot cfg, HostExecutableResolution resolution)
    {
        var storedPath = string.IsNullOrWhiteSpace(resolution.StoredPath)
            ? AgentsPaths.HostExecutable
            : resolution.StoredPath.Trim();

        var processName = cfg.Agents.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = AgentsPaths.HostProcessName;
        }

        return Normalize(new AgentsOptions
        {
            ExecutablePath = storedPath,
            ProcessName = processName ?? string.Empty,
            Modules = cfg.Agents.Modules,
        });
    }

    private AgentsCommandResult ValidateHostLaunch()
    {
        var cfg = _configStore.Load();
        var pg = cfg.Postgres;
        var host = AgentsConfigValidator.ValidateForLaunch(new AgentsConfigValidator.LaunchContext(
            cfg.SchemaVersion,
            pg.Host,
            pg.Port,
            pg.Database,
            pg.Username,
            pg.Password));
        if (!host.Ok)
        {
            return host;
        }

        IReadOnlyList<ModuleDescriptor> modules;
        lock (_gate)
        {
            modules = _modules;
        }

        foreach (var module in modules)
        {
            if (!IsModuleEnabled(module.Id))
            {
                continue;
            }

            var moduleValidate = ValidateModuleSettings(module.Id);
            if (!moduleValidate.Ok)
            {
                return moduleValidate;
            }
        }

        return new AgentsCommandResult(true, "ok");
    }

    private AgentsCommandResult ValidateModuleSettings(string moduleId)
    {
        var cfg = _configStore.Load();
        var options = cfg.Agents;
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return new AgentsCommandResult(false, "无法解析 Agents 目录，无法加载模块 settings");
        }

        try
        {
            _moduleSettings.EnsureUserSettings(moduleId, agentsDir);
        }
        catch (Exception ex)
        {
            return new AgentsCommandResult(false, $"{moduleId} settings 准备失败：{ex.Message}");
        }

        string json;
        try
        {
            json = _moduleSettings.LoadSettingsJson(moduleId);
        }
        catch (Exception ex)
        {
            return new AgentsCommandResult(false, $"读取 {moduleId} settings 失败：{ex.Message}");
        }

        var schemaJson = _moduleSettings.TryLoadSchemaJson(moduleId, agentsDir);
        if (schemaJson is null)
        {
            // 无 schema 的模块：只要求合法 JSON 对象
            try
            {
                if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject)
                {
                    return new AgentsCommandResult(false, $"{moduleId} settings 根节点必须是 JSON 对象");
                }
            }
            catch (Exception ex)
            {
                return new AgentsCommandResult(false, $"{moduleId} settings 不是合法 JSON：{ex.Message}");
            }

            return new AgentsCommandResult(true, "ok");
        }

        var validated = ModuleSettingsValidator.Validate(schemaJson, json);
        if (!validated.Ok)
        {
            return new AgentsCommandResult(false, $"{moduleId}：{validated.Message}");
        }

        return validated;
    }

    private async Task<AgentsCommandResult> ValidateSchemaCompatAsync(CancellationToken ct)
    {
        var version = _releaseVersion.Current;
        var minimum = MinDbSchema;
        var maximum = MaxDbSchema;
        var schema = await _dbSchemaVersion.TryReadSchemaVersionAsync(ct).ConfigureAwait(false);
        var compatibility = schema.Ok
            ? DbSchemaCompat.Evaluate(schema.Value, minimum, maximum)
            : schema.IsMetadataMissing
                ? new DbSchemaCompatibilityResult(
                    DbSchemaCompatibility.MetadataMissing,
                    schema.Value ?? string.Empty,
                    minimum,
                    maximum,
                    schema.Reason ?? "数据库元数据缺失，需要初始化")
                : new DbSchemaCompatibilityResult(
                    DbSchemaCompatibility.Unknown,
                    schema.Value ?? string.Empty,
                    minimum,
                    maximum,
                    schema.Reason ?? "读取失败");

        if (!compatibility.IsCompatible)
        {
            return new AgentsCommandResult(false, compatibility.Message);
        }

        return new AgentsCommandResult(true, "数据库版本兼容");
    }

    private static string BuildConfigArguments(string configPath)
    {
        static string Q(string value)
            => $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";

        return $"--config {Q(configPath ?? string.Empty)}";
    }

    private static AgentsOptions Normalize(AgentsOptions? src)
    {
        var opt = src ?? new AgentsOptions();
        return new AgentsOptions
        {
            ExecutablePath = (opt.ExecutablePath).Trim(),
            ProcessName = (opt.ProcessName).Trim(),
            Modules = opt.Modules.ToDictionary(
                static pair => pair.Key,
                static pair => new ModuleOptions { Enabled = pair.Value.Enabled },
                StringComparer.Ordinal),
        };
    }

    private static bool Same(AgentsOptions a, AgentsOptions b)
        => string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.Ordinal)
           && string.Equals(a.ProcessName, b.ProcessName, StringComparison.Ordinal)
           && string.Equals(
               JsonSerializer.Serialize(a.Modules),
               JsonSerializer.Serialize(b.Modules),
               StringComparison.Ordinal);

    private static string ReadHostVersion(string executablePath)
    {
        var resolvedPath = ResolveExecutablePath(executablePath);
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return "未配置";
        }

        var agentsDir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(agentsDir))
        {
            var fromManifest = AgentsPath.TryReadHostVersion(
                Path.Combine(agentsDir, "ReleaseManifest.json"));
            if (!string.IsNullOrWhiteSpace(fromManifest))
            {
                return fromManifest;
            }
        }

        return ReadFileVersion(resolvedPath);
    }

    private static string ReadFileVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? "未知" : info.FileVersion;
        }
        catch
        {
            return "未知";
        }
    }

    private static string ReadModuleVersion(string agentsDir, string moduleId)
    {
        var manifestPath = AgentsPaths.ModuleManifestPath(agentsDir, moduleId);
        if (!File.Exists(manifestPath))
        {
            return "未配置";
        }

        return AgentsPath.TryReadModuleVersion(manifestPath) ?? "未知";
    }

    private static string? NormalizePath(string? value)
    {
        return ResolveExecutablePath(value);
    }

    private static string? ResolveExecutablePath(string? value)
        => AgentsPath.ResolvePath(value, AppContext.BaseDirectory);

    private sealed record BinaryWatch(string Path, StableBinaryChange Change);

    private sealed record BinaryChangeBatch(
        BinaryStamp? Host,
        IReadOnlyDictionary<string, BinaryStamp> Modules);

    private void RaiseChanged()
    {
        try
        {
            StatusChanged?.Invoke();
        }
        catch (System.Exception ex)
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
        CancelStart();

        try { _pollTimer.Dispose(); }
        catch (System.Exception ex)
        {
            _logger.Warn("Agents", "agents.dispose.timer_fail", "Failed to dispose poll timer", ex);
        }

        // 二进制变更重挂是脱离 Timer 回调执行的；进程退出时保留托管闸门，避免在途任务访问已释放对象
    }
}
