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

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Agents 运行时
///
/// 负责 Host/模块启停、状态观测与进程兜底；不含业务注入逻辑
/// </summary>
public sealed class AgentsRuntime : IAgentsRuntime
{
    private readonly IAppConfigStore _configStore;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Timer _pollTimer;
    // 连点/并发启停冷却；与顶栏 toast debounce 同量级
    private static readonly TimeSpan CommandCooldown = TimeSpan.FromMilliseconds(1200);

    private AgentsOptions _options = new();
    private AgentsRunState _hostState = AgentsRunState.Unknown;
    private AgentsRunState _injectorState = AgentsRunState.Unknown;
    private DateTimeOffset? _hostLastLaunchAt;
    private DateTimeOffset? _injectorLastLaunchAt;
    private string? _hostLastError;
    private string? _injectorLastError;
    private string _hostVersion = "未知";
    private string _injectorVersion = "未知";
    private bool _disposed;
    private DateTimeOffset _lastCommandAt = DateTimeOffset.MinValue;
    private int? _lastProcessId;
    private bool _starting;
    private bool _injectorStopped;
    private CancellationTokenSource? _startCts;

    public event Action? StatusChanged;

    public AgentsDescriptor Descriptor => AgentsDescriptors.Agents;

    public bool IsInjectorEnabled => _configStore.Load().Agents.Injector.Enabled;

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

    public AgentsRunState InjectorState
    {
        get
        {
            lock (_gate)
            {
                return _injectorState;
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

    public bool IsInjectorRunning
    {
        get
        {
            lock (_gate)
            {
                return _injectorState == AgentsRunState.Running;
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

    public DateTimeOffset? InjectorLastLaunchAt
    {
        get
        {
            lock (_gate)
            {
                return _injectorLastLaunchAt;
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

    public string? InjectorLastError
    {
        get
        {
            lock (_gate)
            {
                return _injectorLastError;
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

    public string InjectorVersion
    {
        get
        {
            lock (_gate)
            {
                return _injectorVersion;
            }
        }
    }

    public AgentsRuntime(
        IAppConfigStore configStore,
        IReleaseVersionService releaseVersion,
        IDbSchemaVersionService dbSchemaVersion,
        IAppLogger logger)
    {
        _configStore = configStore;
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

        bool changed;
        lock (_gate)
        {
            changed = !Same(_options, normalized);
            _options = Clone(normalized);
            _hostVersion = ReadHostVersion(_options.ExecutablePath);
            _injectorVersion = ReadInjectorVersion(_options.ExecutablePath);
        }

        var statusChanged = RefreshState();
        if (changed || statusChanged)
        {
            RaiseChanged();
        }

        _logger.Info("Agents", "agents.reload", "Agents runtime config reloaded", new
        {
            resolution.Source,
            normalized.ExecutablePath,
            normalized.ProcessName,
            Version = _hostVersion,
            OptionsChanged = changed
        });
    }

    public async Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default)
    {
        // WaitAsync(0)：已有命令在跑则立刻拒绝，避免排队叠启停
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
        {
            return new AgentsCommandResult(false, "操作进行中，请稍候", SuppressToast: true);
        }

        var gateHeld = true;
        CancellationTokenSource? startCts = null;
        try
        {
            if (IsCommandCoolingDown())
            {
                return new AgentsCommandResult(false, "操作过于频繁，已忽略", SuppressToast: true);
            }

            var schemaValidation = await ValidateSchemaCompatAsync(ct).ConfigureAwait(false);
            if (!schemaValidation.Ok)
            {
                return SetHostError(schemaValidation.Message);
            }

            AgentsOptions options;
            lock (_gate)
            {
                options = Clone(_options);
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

            var validate = ValidateAgentsConfig();
            if (!validate.Ok)
            {
                return SetHostError(validate.Message);
            }

            // 重启判定看进程是否仍在，而非 State——Failed 时 Host 仍可能存活
            var wasActive = GetTargetProcesses(options).Any();
            if (wasActive)
            {
                CancelStart();
                WriteModuleControl(options, "quit");
                ClearModuleReady(options);
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

                ClearModuleControl(options);

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

            ClearModuleControl(options);
            ClearModuleReady(options);
            lock (_gate)
            {
                _injectorStopped = false;
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
                _injectorLastError = null;
                _hostVersion = ReadHostVersion(_options.ExecutablePath);
                _injectorVersion = ReadInjectorVersion(_options.ExecutablePath);
            }

            RaiseChanged();

            startCts = new CancellationTokenSource();
            _startCts = startCts;
            _commandGate.Release();
            gateHeld = false;

            var mountInjector = IsInjectorEnabled;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, startCts.Token);

                var up = await WaitUntilHostUpAsync(options, TimeSpan.FromSeconds(4), linked.Token)
                    .ConfigureAwait(false);
                if (!up)
                {
                    return SetHostError("已触发启动，但未检测到 Agents 进程");
                }

                if (mountInjector)
                {
                    if (!await EnsureInjectorStoppedAsync(options, linked.Token).ConfigureAwait(false))
                    {
                        return SetInjectorError("重启失败：Injector 进程仍在运行");
                    }

                    WriteModuleControl(options, "start");
                    var ready = await WaitUntilModuleReadyAsync(options, TimeSpan.FromSeconds(8), linked.Token)
                        .ConfigureAwait(false);
                    if (!ready)
                    {
                        // Host 可能仍在；失败的是模块自检
                        return SetInjectorError("已触发启动，但 Injector 未完成自检（配置错误或启动失败）");
                    }

                    lock (_gate)
                    {
                        _starting = false;
                        _injectorLastLaunchAt = DateTimeOffset.Now;
                        _injectorLastError = null;
                        _hostLastError = null;
                        _hostVersion = ReadHostVersion(_options.ExecutablePath);
                        _injectorVersion = ReadInjectorVersion(_options.ExecutablePath);
                    }
                }
                else
                {
                    lock (_gate)
                    {
                        _starting = false;
                        _hostLastError = null;
                        _injectorLastError = null;
                    }
                }

                RefreshState();
                RaiseChanged();
                return new AgentsCommandResult(
                    true,
                    wasActive
                        ? (mountInjector ? "已重启" : "Agents 已重启")
                        : (mountInjector ? "已启动" : "Agents 已启动"));
            }
            catch (OperationCanceledException)
            {
                StopTargetProcesses(options);
                lock (_gate)
                {
                    _starting = false;
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

            if (gateHeld)
            {
                _commandGate.Release();
            }
        }
    }

    public async Task<AgentsCommandResult> StopAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
        {
            return new AgentsCommandResult(false, "操作进行中，请稍候", SuppressToast: true);
        }

        AgentsOptions options;
        try
        {
            if (IsCommandCoolingDown())
            {
                return new AgentsCommandResult(false, "操作过于频繁，已忽略", SuppressToast: true);
            }

            CancelStart();

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
                    _injectorLastError = null;
                    _lastProcessId = null;
                    _starting = false;
                    _injectorStopped = false;
                }
                RefreshState();
                RaiseChanged();
                return new AgentsCommandResult(true, "已停止");
            }

            // 请常驻 Host 卸载模块并退出；kill 仍作兜底
            WriteModuleControl(options, "quit");
            ClearModuleReady(options);

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

            ClearModuleControl(options);

            // 进程退出后给 shell 清理托盘图标缓存的时间
            await Task.Delay(250, ct).ConfigureAwait(false);

            lock (_gate)
            {
                _hostLastError = null;
                _injectorLastError = null;
                _lastProcessId = null;
                _starting = false;
                _injectorStopped = false;
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

    public async Task<AgentsCommandResult> StopInjectorAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
        {
            return new AgentsCommandResult(false, "操作进行中，请稍候", SuppressToast: true);
        }

        try
        {
            if (IsCommandCoolingDown())
            {
                return new AgentsCommandResult(false, "操作过于频繁，已忽略", SuppressToast: true);
            }

            if (!OperatingSystem.IsWindows())
            {
                return SetInjectorError("当前系统不支持停止 Injector");
            }

            AgentsOptions options;
            lock (_gate)
            {
                options = Clone(_options);
                _injectorLastError = null;
            }

            if (!await EnsureInjectorStoppedAsync(options, ct).ConfigureAwait(false))
            {
                return SetInjectorError("停止失败：Injector 进程仍在运行");
            }

            lock (_gate)
            {
                // Host 仍在但尚无 module.ready 时，先不要标成 Starting
                _injectorStopped = true;
                _injectorLastError = null;
            }

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, "Injector 已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.injector_stop.fail", "Injector stop failed", ex);
            return SetInjectorError($"停止失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<AgentsCommandResult> StartInjectorAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
        {
            return new AgentsCommandResult(false, "操作进行中，请稍候", SuppressToast: true);
        }

        var gateHeld = true;
        try
        {
            if (IsCommandCoolingDown())
            {
                return new AgentsCommandResult(false, "操作过于频繁，已忽略", SuppressToast: true);
            }

            if (!OperatingSystem.IsWindows())
            {
                return SetInjectorError("当前系统不支持启动 Injector");
            }

            if (!IsInjectorEnabled)
            {
                return SetInjectorError("请先启用 Injector");
            }

            AgentsOptions options;
            lock (_gate)
            {
                options = Clone(_options);
            }

            var validate = ValidateAgentsConfig();
            if (!validate.Ok)
            {
                return SetInjectorError(validate.Message);
            }

            // Host 已挂 → 完整启动 Agents（Desktop 等 Host 起来后再挂 Injector）
            if (!GetTargetProcesses(options).Any())
            {
                _commandGate.Release();
                gateHeld = false;
                return await StartOrRestartAsync(ct).ConfigureAwait(false);
            }

            // 模块进程仍在时 Host 会忽略 start——需先 stop 再 remount
            if (!await EnsureInjectorStoppedAsync(options, ct).ConfigureAwait(false))
            {
                return SetInjectorError("重启失败：Injector 进程仍在运行");
            }

            lock (_gate)
            {
                _injectorStopped = false;
                _injectorLastError = null;
            }

            MarkStarting(host: false);
            RaiseChanged();
            WriteModuleControl(options, "start");

            var ready = await WaitUntilModuleReadyAsync(options, TimeSpan.FromSeconds(8), ct)
                .ConfigureAwait(false);
            if (!ready)
            {
                return SetInjectorError("已请求启动 Injector，但未完成自检（配置错误或启动失败）");
            }

            lock (_gate)
            {
                _starting = false;
                _injectorLastLaunchAt = DateTimeOffset.Now;
                _injectorLastError = null;
                _injectorVersion = ReadInjectorVersion(_options.ExecutablePath);
            }

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, "Injector 已启动");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.injector_start.fail", "Injector start failed", ex);
            return SetInjectorError($"启动失败：{ex.Message}");
        }
        finally
        {
            if (gateHeld)
            {
                _commandGate.Release();
            }
        }
    }

    private void PollStatus()
    {
        if (_disposed)
        {
            return;
        }

        if (RefreshState())
        {
            RaiseChanged();
        }
    }

    private bool RefreshState()
    {
        AgentsOptions options;
        lock (_gate)
        {
            options = Clone(_options);
        }

        var hostState = DetectHostState(options);
        var injectorState = DetectInjectorState(options, hostState);
        lock (_gate)
        {
            if (_hostState == hostState && _injectorState == injectorState)
            {
                return false;
            }

            _hostState = hostState;
            _injectorState = injectorState;
            if (!hostState.IsActive())
            {
                _lastProcessId = null;
            }

            return true;
        }
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

    private AgentsRunState DetectInjectorState(AgentsOptions options, AgentsRunState hostState)
    {
        try
        {
            lock (_gate)
            {
                if (_injectorStopped)
                {
                    return AgentsRunState.Stopped;
                }
            }

            if (!IsInjectorEnabled)
            {
                return AgentsRunState.Stopped;
            }

            if (hostState is AgentsRunState.Stopped or AgentsRunState.Unknown)
            {
                return AgentsRunState.Stopped;
            }

            if (hostState == AgentsRunState.Failed)
            {
                lock (_gate)
                {
                    return string.IsNullOrWhiteSpace(_injectorLastError)
                        ? AgentsRunState.Stopped
                        : AgentsRunState.Failed;
                }
            }

            var alive = AnyInjectorProcess(options);
            var ready = ModuleReadyExists(options);
            if (ready && !alive)
            {
                // Host 常驻下 crash/kill 后可能残留过期 marker
                ClearModuleReady(options);
                ready = false;
            }

            if (hostState == AgentsRunState.Running && ready && alive)
            {
                lock (_gate)
                {
                    _injectorLastError = null;
                }

                return AgentsRunState.Running;
            }

            lock (_gate)
            {
                if (_injectorState == AgentsRunState.Failed
                    && !string.IsNullOrWhiteSpace(_injectorLastError))
                {
                    return AgentsRunState.Failed;
                }

                // 仅在 start/remount 进行中，或进程已起但尚未 ready 时标 Starting
                if (_starting || (alive && !ready))
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

            // 仅 Legacy：配置迁到 Host 后仍需停掉 Tools\pacinjector.exe
            return AgentsPath.IsLegacyToolsStoredPath(normalizedModulePath);
        }
        catch
        {
            return permissiveOnAccessDenied;
        }
    }

    private static IReadOnlyList<string> ResolveProcessNameCandidates(AgentsOptions options)
    {
        var names = new List<string>(AgentsPaths.HostProcessNameCandidates.Length + 2);

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
        foreach (var candidate in AgentsPaths.HostProcessNameCandidates)
        {
            Add(candidate);
        }

        return names;
    }

    private AgentsCommandResult SetHostError(string message)
    {
        lock (_gate)
        {
            _hostLastError = message;
            _starting = false;
            _hostState = AgentsRunState.Failed;
        }

        RefreshState();
        RaiseChanged();
        return new AgentsCommandResult(false, message);
    }

    private AgentsCommandResult SetInjectorError(string message)
    {
        lock (_gate)
        {
            _injectorLastError = message;
            _starting = false;
            _injectorState = AgentsRunState.Failed;
        }

        RefreshState();
        RaiseChanged();
        return new AgentsCommandResult(false, message);
    }

    private bool IsCommandCoolingDown()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            // 与顶栏 toast debounce 同窗口，防连点叠启停
            if (now - _lastCommandAt < CommandCooldown)
            {
                return true;
            }

            _lastCommandAt = now;
            return false;
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

            if (IsInjectorEnabled)
            {
                _injectorState = AgentsRunState.Starting;
                _injectorLastError = null;
            }
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

    private async Task<bool> WaitUntilModuleReadyAsync(AgentsOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (ModuleReadyExists(options))
            {
                return true;
            }

            if (!GetTargetProcesses(options).Any())
            {
                return false;
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        return ModuleReadyExists(options);
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

        ClearModuleReady(options);
    }

    private static string? ResolveModuleReadyPath(AgentsOptions options)
    {
        var agentsDir = ResolveAgentsDir(options);
        return agentsDir is null
            ? null
            : AgentsPaths.ModuleReadyPath(agentsDir, AgentsPaths.InjectorModuleId);
    }

    private static string? ResolveAgentsDir(AgentsOptions options)
    {
        var hostPath = ResolveExecutablePath(options.ExecutablePath);
        if (hostPath is null)
        {
            return null;
        }

        var agentsDir = Path.GetDirectoryName(hostPath);
        return string.IsNullOrWhiteSpace(agentsDir) ? null : agentsDir;
    }

    private static string? ResolveInjectorEntryPath(AgentsOptions options)
    {
        var agentsDir = ResolveAgentsDir(options);
        return agentsDir is null
            ? null
            : AgentsPath.TryResolveModuleEntryPath(agentsDir, AgentsPaths.InjectorModuleId);
    }

    private static void WriteModuleControl(AgentsOptions options, string command)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        var path = AgentsPaths.ModuleControlPath(agentsDir, AgentsPaths.InjectorModuleId);
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

    private static void ClearModuleControl(AgentsOptions options)
    {
        var agentsDir = ResolveAgentsDir(options);
        if (agentsDir is null)
        {
            return;
        }

        var path = AgentsPaths.ModuleControlPath(agentsDir, AgentsPaths.InjectorModuleId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort；下次 start/write 前 Desktop 会再清一次
        }
    }

    private IEnumerable<Process> GetInjectorProcesses(AgentsOptions options)
    {
        var entryPath = ResolveInjectorEntryPath(options);
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

    private void StopInjectorProcesses(AgentsOptions options)
    {
        foreach (var p in GetInjectorProcesses(options))
        {
            try
            {
                TryTerminateProcess(p);
            }
            catch (Exception ex)
            {
                _logger.Warn("Agents", "agents.injector_terminate.fail", "Failed to terminate Injector process", ex);
            }
            finally
            {
                p.Dispose();
            }
        }

        ClearModuleReady(options);
    }

    private async Task<bool> EnsureInjectorStoppedAsync(AgentsOptions options, CancellationToken ct)
    {
        ClearModuleReady(options);
        if (!AnyInjectorProcess(options))
        {
            ClearModuleControl(options);
            return true;
        }

        WriteModuleControl(options, "stop");
        var stopped = await WaitUntilInjectorStoppedAsync(options, TimeSpan.FromSeconds(4), ct)
            .ConfigureAwait(false);
        if (!stopped)
        {
            StopInjectorProcesses(options);
            stopped = await WaitUntilInjectorStoppedAsync(options, TimeSpan.FromSeconds(3), ct)
                .ConfigureAwait(false);
        }

        ClearModuleControl(options);
        ClearModuleReady(options);
        return stopped;
    }

    private async Task<bool> WaitUntilInjectorStoppedAsync(
        AgentsOptions options,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            if (!AnyInjectorProcess(options))
            {
                return true;
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return !AnyInjectorProcess(options);
    }

    private bool AnyInjectorProcess(AgentsOptions options)
    {
        var list = GetInjectorProcesses(options).ToList();
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

    private static bool ModuleReadyExists(AgentsOptions options)
    {
        var path = ResolveModuleReadyPath(options);
        return path is not null && File.Exists(path);
    }

    private static void ClearModuleReady(AgentsOptions options)
    {
        var path = ResolveModuleReadyPath(options);
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // best-effort；下次 Injector 启动时会再清过期 marker
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
        Injector = JsonSerializer.Deserialize<InjectorOptions>(
                       JsonSerializer.Serialize(src.Injector))
                   ?? new InjectorOptions(),
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
            Injector = cfg.Agents.Injector,
        });
    }

    private AgentsCommandResult ValidateAgentsConfig()
    {
        var cfg = _configStore.Load();
        var pg = cfg.Postgres;
        return AgentsConfigValidator.ValidateForLaunch(new AgentsConfigValidator.LaunchContext(
            cfg.SchemaVersion,
            pg.Host,
            pg.Port,
            pg.Database,
            pg.Username,
            pg.Password,
            cfg.Agents));
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
            Injector = opt.Injector,
        };
    }

    private static bool Same(AgentsOptions a, AgentsOptions b)
        => string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.Ordinal)
           && string.Equals(a.ProcessName, b.ProcessName, StringComparison.Ordinal)
           && string.Equals(
               JsonSerializer.Serialize(a.Injector),
               JsonSerializer.Serialize(b.Injector),
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

    private static string ReadInjectorVersion(string hostExecutablePath)
    {
        var hostPath = ResolveExecutablePath(hostExecutablePath);
        if (hostPath is null)
        {
            return "未配置";
        }

        var agentsDir = Path.GetDirectoryName(hostPath);
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return "未配置";
        }

        var manifestPath = AgentsPaths.ModuleManifestPath(agentsDir, AgentsPaths.InjectorModuleId);
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

        try { _commandGate.Dispose(); }
        catch (System.Exception ex)
        {
            _logger.Warn("Agents", "agents.dispose.gate_fail", "Failed to dispose command gate", ex);
        }
    }
}
