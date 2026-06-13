using System;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Agent.Contracts.Events;
using PacToolkits.Agent.Contracts.Mapping;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Agent.Contracts.Validation;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

public sealed class AhkRuntimeService : IAutomationRuntimeService
{
    private readonly IAppConfigStore _configStore;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAppLogger _logger;
    private readonly IAgentEventSink _eventSink;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly Timer _pollTimer;
    private static readonly TimeSpan CommandCooldown = TimeSpan.FromMilliseconds(1200);

    private AhkToolOptions _options = new();
    private ToolRunState _state = ToolRunState.Unknown;
    private DateTimeOffset? _lastLaunchAt;
    private string? _lastError;
    private string _toolVersion = "未知";
    private bool _disposed;
    private DateTimeOffset _lastCommandAt = DateTimeOffset.MinValue;

    public event Action? StatusChanged;

    public AutomationAhkOptionsDto CurrentOptions
    {
        get
        {
            lock (_gate)
                return AutomationContractMapper.ToApplication(Clone(_options));
        }
    }

    public AutomationRunState State
    {
        get
        {
            lock (_gate)
                return AutomationContractMapper.ToApplication(_state);
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _state == ToolRunState.Running;
        }
    }

    public DateTimeOffset? LastLaunchAt
    {
        get
        {
            lock (_gate)
                return _lastLaunchAt;
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
                return _lastError;
        }
    }

    public string ToolVersion
    {
        get
        {
            lock (_gate)
                return _toolVersion;
        }
    }

    public AgentRuntimeConfig RuntimeConfig
    {
        get
        {
            lock (_gate)
            {
                return AgentContractMapping.ToRuntimeConfig(
                    Clone(_options),
                    _configStore.ConfigPath,
                    _releaseVersion.Current.AgentVersion ?? string.Empty);
            }
        }
    }

    public AhkRuntimeService(
        IAppConfigStore configStore,
        IReleaseVersionService releaseVersion,
        IAppLogger logger,
        IAgentEventSink eventSink)
    {
        _configStore = configStore;
        _releaseVersion = releaseVersion;
        _logger = logger;
        _eventSink = eventSink;
        Reload();

        _pollTimer = new Timer(_ => PollStatus(), null, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1));
    }

    public void Reload()
    {
        var cfg = _configStore.Load();
        var normalized = Normalize(cfg.AutomationTools.Ahk);

        bool changed;
        lock (_gate)
        {
            changed = !Same(_options, normalized);
            _options = Clone(normalized);
            _toolVersion = ReadFileVersion(_options.ExecutablePath);
        }

        var statusChanged = RefreshState();
        if (changed || statusChanged)
            RaiseChanged();
        _logger.Info("AhkRuntime", "ahk.reload", "AHK runtime config reloaded", new
        {
            normalized.ExecutablePath,
            normalized.ProcessName,
            Version = _toolVersion
        });
    }

    public async Task SaveOptionsAsync(AutomationAhkOptionsDto options, CancellationToken ct = default)
    {
        var normalized = Normalize(AutomationContractMapper.ToContract(options));

        var cfg = _configStore.Load();
        cfg.AutomationTools.Ahk = Clone(normalized);
        await _configStore.SaveAsync(cfg, ct).ConfigureAwait(false);

        bool changed;
        lock (_gate)
        {
            changed = !Same(_options, normalized);
            _options = Clone(normalized);
            _toolVersion = ReadFileVersion(_options.ExecutablePath);
            _lastError = null;
        }

        var statusChanged = RefreshState();
        if (changed || statusChanged)
            RaiseChanged();
        _logger.Info("AhkRuntime", "ahk.options.saved", "AHK runtime options saved", new
        {
            normalized.ExecutablePath,
            normalized.ProcessName,
            Version = _toolVersion
        });
    }

    public async Task<AutomationCommandResult> StartOrRestartAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
            return new AutomationCommandResult(true, "操作进行中，请稍候", SuppressToast: true);

        AhkToolOptions options;
        try
        {
            if (IsCommandCoolingDown())
                return new AutomationCommandResult(true, "操作过于频繁，已忽略", SuppressToast: true);

            lock (_gate)
                options = Clone(_options);

            if (!OperatingSystem.IsWindows())
                return SetError("当前系统不支持启动自动化套件");

            if (string.IsNullOrWhiteSpace(options.ExecutablePath))
                return SetError("请先配置自动化套件的可执行文件路径");

            var resolvedExePath = ResolveExecutablePath(options.ExecutablePath);
            if (resolvedExePath is null)
                return SetError($"路径无效：{options.ExecutablePath}");

            if (!File.Exists(resolvedExePath))
                return SetError($"文件不存在：{options.ExecutablePath}");

            var validate = ValidateAgentConfig();
            if (!validate.Ok)
            {
                PublishEvent(AgentCommandKind.Start, null, validate.Message);
                return SetError(validate.Message);
            }

            var wasRunning = DetectState(options) == ToolRunState.Running;
            if (wasRunning)
            {
                StopTargetProcesses(options);
                var stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
                if (!stopped)
                    return SetError("重启失败：检测到进程仍在运行，已取消本次启动");

                // Give shell tray time to remove icon before relaunch.
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedExePath,
                Arguments = BuildConfigArguments(_configStore.ConfigPath, _releaseVersion.Current),
                WorkingDirectory = Path.GetDirectoryName(resolvedExePath) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
            };

            Process.Start(startInfo);

            var started = await WaitUntilRunningAsync(options, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            if (!started)
                return SetError("已触发启动，但未检测到进程运行");

            lock (_gate)
            {
                _lastLaunchAt = DateTimeOffset.Now;
                _lastError = null;
                _toolVersion = ReadFileVersion(_options.ExecutablePath);
            }

            RefreshState();
            RaiseChanged();
            var successMessage = wasRunning ? "已重启" : "已启动";
            PublishEvent(AgentCommandKind.Start, ToolRunState.Running, successMessage);
            return new AutomationCommandResult(true, successMessage);
        }
        catch (Exception ex)
        {
            _logger.Error("AhkRuntime", "ahk.start_or_restart.fail", "AHK start/restart failed", ex);
            PublishEvent(AgentCommandKind.Start, null, $"启动失败：{ex.Message}", ex.Message);
            return SetError($"启动失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<AutomationCommandResult> StopAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
            return new AutomationCommandResult(true, "操作进行中，请稍候", SuppressToast: true);

        AhkToolOptions options;
        try
        {
            if (IsCommandCoolingDown())
                return new AutomationCommandResult(true, "操作过于频繁，已忽略", SuppressToast: true);

            lock (_gate)
                options = Clone(_options);

            if (!OperatingSystem.IsWindows())
                return SetError("当前系统不支持停止自动化套件");

            var processes = GetTargetProcesses(options).ToList();
            if (processes.Count == 0)
            {
                lock (_gate)
                    _lastError = null;
                RefreshState();
                RaiseChanged();
                return new AutomationCommandResult(true, "已停止");
            }

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

            var stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            if (!stopped)
            {
                StopTargetProcesses(options);
                stopped = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            }

            if (!stopped)
                return SetError("停止失败：检测到进程仍在运行");

            // Allow shell to clean tray icon cache after process exit.
            await Task.Delay(250, ct).ConfigureAwait(false);

            lock (_gate)
                _lastError = null;

            RefreshState();
            RaiseChanged();
            PublishEvent(AgentCommandKind.Stop, ToolRunState.Stopped, "已停止");
            return new AutomationCommandResult(true, "已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("AhkRuntime", "ahk.stop.fail", "AHK stop failed", ex);
            PublishEvent(AgentCommandKind.Stop, null, $"停止失败：{ex.Message}", ex.Message);
            return SetError($"停止失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void PollStatus()
    {
        if (_disposed) return;
        if (RefreshState())
            RaiseChanged();
    }

    private bool RefreshState()
    {
        AhkToolOptions options;
        lock (_gate)
            options = Clone(_options);

        var newState = DetectState(options);
        lock (_gate)
        {
            if (_state == newState)
                return false;

            _state = newState;
            return true;
        }
    }

    private static ToolRunState DetectState(AhkToolOptions options)
    {
        try
        {
            return GetTargetProcesses(options).Any() ? ToolRunState.Running : ToolRunState.Stopped;
        }
        catch
        {
            return ToolRunState.Unknown;
        }
    }

    private static IEnumerable<Process> GetTargetProcesses(AhkToolOptions options)
    {
        var processName = ResolveProcessName(options);
        if (string.IsNullOrWhiteSpace(processName))
            return Array.Empty<Process>();

        var byName = Process.GetProcessesByName(processName);
        if (byName.Length == 0)
            return byName;

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
            return byName;

        var normalizedExePath = NormalizePath(options.ExecutablePath);
        if (normalizedExePath is null)
            return byName;

        var matched = new List<Process>(byName.Length);
        foreach (var p in byName)
        {
            var keep = false;
            try
            {
                var modulePath = p.MainModule?.FileName;
                var normalizedModulePath = NormalizePath(modulePath);

                if (normalizedModulePath is not null
                    && string.Equals(normalizedModulePath, normalizedExePath, StringComparison.OrdinalIgnoreCase))
                {
                    keep = true;
                }
            }
            catch
            {
                keep = true;
            }

            if (keep)
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

    private static string ResolveProcessName(AhkToolOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProcessName))
            return Path.GetFileNameWithoutExtension(options.ProcessName.Trim());

        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
            return Path.GetFileNameWithoutExtension(options.ExecutablePath.Trim());

        return string.Empty;
    }

    private AutomationCommandResult SetError(string message)
    {
        lock (_gate)
            _lastError = message;

        RefreshState();
        RaiseChanged();
        return new AutomationCommandResult(false, message);
    }

    private bool IsCommandCoolingDown()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (now - _lastCommandAt < CommandCooldown)
                return true;

            _lastCommandAt = now;
            return false;
        }
    }

    private static async Task<bool> WaitUntilRunningAsync(AhkToolOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (DetectState(options) == ToolRunState.Running)
                return true;

            await Task.Delay(180, ct).ConfigureAwait(false);
        }

        return DetectState(options) == ToolRunState.Running;
    }

    private static async Task<bool> WaitUntilStoppedAsync(AhkToolOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (DetectState(options) != ToolRunState.Running)
                return true;

            await Task.Delay(180, ct).ConfigureAwait(false);
        }

        return DetectState(options) != ToolRunState.Running;
    }

    private void StopTargetProcesses(AhkToolOptions options)
    {
        foreach (var p in GetTargetProcesses(options))
        {
            try
            {
                TryTerminateProcess(p);
            }
            catch (System.Exception ex)
            {
                _logger.Warn("AhkRuntime", "ahk.terminate.fail", "Failed to terminate AHK process", ex);
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private void TryTerminateProcess(Process p)
    {
        if (p.HasExited)
            return;

        try
        {
            // Prefer graceful termination first so tray icon has a chance to dispose cleanly.
            if (p.CloseMainWindow())
            {
                if (p.WaitForExit(2200))
                    return;
            }
        }
        catch (System.Exception ex)
        {
            _logger.Warn("AhkRuntime", "ahk.close_window.fail", "Failed to close AHK process window gracefully", ex);
        }

        try
        {
            if (!p.HasExited)
            {
                p.Kill(false);
                p.WaitForExit(2500);
            }
        }
        catch (System.Exception ex)
        {
            _logger.Warn("AhkRuntime", "ahk.kill.fail", "Failed to kill AHK process", ex);
        }
    }

    private static AhkToolOptions Clone(AhkToolOptions src) => new()
    {
        ExecutablePath = src.ExecutablePath,
        ProcessName = src.ProcessName,
    };

    private ToolCommandResult ValidateAgentConfig()
    {
        var cfg = _configStore.Load();
        var pg = cfg.Postgres;
        return AgentConfigValidator.ValidateForLaunch(new AgentConfigValidator.LaunchContext(
            cfg.SchemaVersion,
            pg.Host,
            pg.Port,
            pg.Database,
            pg.Username,
            cfg.AutomationTools));
    }

    private void PublishEvent(
        AgentCommandKind command,
        ToolRunState? runtimeState,
        string message,
        string? detail = null)
    {
        _eventSink.Publish(new AgentExecutionEvent(
            DateTimeOffset.UtcNow,
            command,
            runtimeState,
            null,
            message,
            detail));
    }

    private static string BuildConfigArguments(string configPath, ReleaseVersionInfo releaseVersion)
    {
        static string Q(string value)
            => $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";

        var config = Q(configPath ?? string.Empty);
        var agent = Q(releaseVersion.AgentVersion ?? string.Empty);
        return $"--config {config} --agent-version {agent}";
    }

    private static AhkToolOptions Normalize(AhkToolOptions? src)
    {
        var opt = src ?? new AhkToolOptions();
        return new AhkToolOptions
        {
            ExecutablePath = (opt.ExecutablePath).Trim(),
            ProcessName = (opt.ProcessName).Trim(),
        };
    }

    private static bool Same(AhkToolOptions a, AhkToolOptions b)
        => string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.Ordinal)
           && string.Equals(a.ProcessName, b.ProcessName, StringComparison.Ordinal);

    private static string ReadFileVersion(string executablePath)
    {
        var resolvedPath = ResolveExecutablePath(executablePath);
        if (resolvedPath is null || !File.Exists(resolvedPath))
            return "未配置";

        try
        {
            var info = FileVersionInfo.GetVersionInfo(resolvedPath);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? "未知" : info.FileVersion;
        }
        catch
        {
            return "未知";
        }
    }

    private static string? NormalizePath(string? value)
    {
        return ResolveExecutablePath(value);
    }

    private static string? ResolveExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var trimmed = value.Trim();
            if (Path.IsPathRooted(trimmed))
                return Path.GetFullPath(trimmed);

            return Path.GetFullPath(trimmed, AppContext.BaseDirectory);
        }
        catch
        {
            return null;
        }
    }

    private void RaiseChanged()
    {
        try
        {
            StatusChanged?.Invoke();
        }
        catch (System.Exception ex)
        {
            _logger.Warn("AhkRuntime", "ahk.kill.fail", "Failed to terminate process", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _pollTimer.Dispose(); }
        catch (System.Exception ex)
        {
            _logger.Warn("AhkRuntime", "ahk.dispose.timer_fail", "Failed to dispose poll timer", ex);
        }

        try { _commandGate.Dispose(); }
        catch (System.Exception ex)
        {
            _logger.Warn("AhkRuntime", "ahk.dispose.gate_fail", "Failed to dispose command gate", ex);
        }
    }
}
