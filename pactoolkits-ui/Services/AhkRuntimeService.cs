using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace pactoolkits_ui.Services;

public enum ToolRunState
{
    Stopped,
    Running,
    Unknown
}

public readonly record struct ToolCommandResult(bool Ok, string Message, bool SuppressToast = false);

public interface IAhkRuntimeService : IDisposable
{
    event Action? StatusChanged;

    AhkToolOptions CurrentOptions { get; }
    ToolRunState State { get; }
    bool IsRunning { get; }
    DateTimeOffset? LastLaunchAt { get; }
    string? LastError { get; }
    string ToolVersion { get; }

    void Reload();
    Task SaveOptionsAsync(AhkToolOptions options, CancellationToken ct = default);
    Task<ToolCommandResult> StartOrRestartAsync(CancellationToken ct = default);
    Task<ToolCommandResult> StopAsync(CancellationToken ct = default);
}

public sealed class AhkRuntimeService : IAhkRuntimeService
{
    private readonly IAppConfigStore _configStore;
    private readonly IAppLogger _logger;
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

    public AhkToolOptions CurrentOptions
    {
        get
        {
            lock (_gate)
                return Clone(_options);
        }
    }

    public ToolRunState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public bool IsRunning => State == ToolRunState.Running;

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

    public AhkRuntimeService(IAppConfigStore configStore, IAppLogger logger)
    {
        _configStore = configStore;
        _logger = logger;
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

    public async Task SaveOptionsAsync(AhkToolOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);

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

    public async Task<ToolCommandResult> StartOrRestartAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
            return new ToolCommandResult(true, "操作进行中，请稍候", SuppressToast: true);

        AhkToolOptions options;
        try
        {
            if (IsCommandCoolingDown())
                return new ToolCommandResult(true, "操作过于频繁，已忽略", SuppressToast: true);

            lock (_gate)
                options = Clone(_options);

            if (!OperatingSystem.IsWindows())
                return SetError("当前系统不支持启动追溯码自动注入工具");

            if (string.IsNullOrWhiteSpace(options.ExecutablePath))
                return SetError("请先配置追溯码自动注入工具的可执行文件路径");

            var resolvedExePath = ResolveExecutablePath(options.ExecutablePath);
            if (resolvedExePath is null)
                return SetError($"路径无效：{options.ExecutablePath}");

            if (!File.Exists(resolvedExePath))
                return SetError($"文件不存在：{options.ExecutablePath}");

            var validate = ValidateAgentConfig();
            if (!validate.Ok)
                return SetError(validate.Message);

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
                Arguments = BuildConfigArguments(_configStore.ConfigPath),
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
            return new ToolCommandResult(true, wasRunning ? "已重启" : "已启动");
        }
        catch (Exception ex)
        {
            _logger.Error("AhkRuntime", "ahk.start_or_restart.fail", "AHK start/restart failed", ex);
            return SetError($"启动失败：{ex.Message}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ToolCommandResult> StopAsync(CancellationToken ct = default)
    {
        var entered = await _commandGate.WaitAsync(0, ct).ConfigureAwait(false);
        if (!entered)
            return new ToolCommandResult(true, "操作进行中，请稍候", SuppressToast: true);

        AhkToolOptions options;
        try
        {
            if (IsCommandCoolingDown())
                return new ToolCommandResult(true, "操作过于频繁，已忽略", SuppressToast: true);

            lock (_gate)
                options = Clone(_options);

            if (!OperatingSystem.IsWindows())
                return SetError("当前系统不支持停止追溯码自动注入工具");

            var processes = GetTargetProcesses(options).ToList();
            if (processes.Count == 0)
            {
                lock (_gate)
                    _lastError = null;
                RefreshState();
                RaiseChanged();
                return new ToolCommandResult(true, "已停止");
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
            return new ToolCommandResult(true, "已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("AhkRuntime", "ahk.stop.fail", "AHK stop failed", ex);
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

    private ToolCommandResult SetError(string message)
    {
        lock (_gate)
            _lastError = message;

        RefreshState();
        RaiseChanged();
        return new ToolCommandResult(false, message);
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
        if (cfg.SchemaVersion != 1)
            return new ToolCommandResult(false, $"配置版本不受支持：{cfg.SchemaVersion}（仅支持 schemaVersion=1）");

        var pg = cfg.Postgres;
        if (string.IsNullOrWhiteSpace(pg.Host)
            || pg.Port <= 0
            || string.IsNullOrWhiteSpace(pg.Database)
            || string.IsNullOrWhiteSpace(pg.Username))
        {
            return new ToolCommandResult(false, "统一配置校验失败：Postgres 关键字段不完整");
        }

        var agent = cfg.AutomationTools.Agent;
        if (string.IsNullOrWhiteSpace(agent.PgDriver)
            || string.IsNullOrWhiteSpace(agent.PgSsl)
            || string.IsNullOrWhiteSpace(agent.OptCls)
            || string.IsNullOrWhiteSpace(agent.IptCls)
            || string.IsNullOrWhiteSpace(agent.ClassNN))
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent 文本字段不完整");
        }

        if (agent.AppWin is null || agent.AppWin.Count == 0)
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.AppWin 不能为空");

        if (agent.ColSpecs is null || agent.ColSpecs.Count == 0)
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.ColSpecs 不能为空");

        if (agent.ConfirmTimeoutMs < 100 || agent.ConfirmTimeoutMs > 10000)
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.ConfirmTimeoutMs 超出范围（100-10000）");

        return new ToolCommandResult(true, "ok");
    }

    private static string BuildConfigArguments(string configPath)
    {
        var escaped = (configPath ?? string.Empty).Replace("\"", "\\\"");
        return $"--config \"{escaped}\"";
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
