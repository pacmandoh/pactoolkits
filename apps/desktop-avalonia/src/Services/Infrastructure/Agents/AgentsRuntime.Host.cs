using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Agents.Contracts.Validation;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>Host 进程 OS 启停、二进制热更、Host 门禁</summary>
public sealed partial class AgentsRuntime
{
    public async Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default)
    {
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

    private async Task<AgentsCommandResult> ExecuteStartOrRestartAsync(
        CancellationToken ct,
        IReadOnlyCollection<string>? mountOnly = null)
    {
        CancellationTokenSource? startCts = null;
        try
        {
            if (IsHostCommandCoolingDown())
            {
                return new AgentsCommandResult(false, "操作过于频繁，已忽略", SuppressToast: true);
            }

            NoteHostCommandIssued();

            var bundleValidation = ValidateHostBundleCompat();
            if (!bundleValidation.Ok)
            {
                return SetHostError(bundleValidation.Message);
            }

            AgentsOptions options;
            bool modulesChanged;
            lock (_gate)
            {
                options = AgentsOptionsModel.Clone(_options);
                modulesChanged = _projection.TryApplyCatalogFromCaches(
                    ResolveAgentsDir(options),
                    _link,
                    TimeSpan.FromMinutes(30));
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

            var resolvedExePath = AgentsDeployPaths.ResolveExe(options.ExecutablePath);
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

            var wasActive = IsHostLive(options);
            if (wasActive)
            {
                CancelStart();
                if (!await StopHostAsync(options, ct).ConfigureAwait(false))
                {
                    return SetHostError("重启失败：Host 仍在运行，已取消本次启动");
                }

                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            // Host 树收不干净时：按入口扫杀模块残留，避免新旧双实例（非日常监管）
            if (!ClearModuleOrphans(options))
            {
                return SetHostError("重启失败：模块进程仍在运行，已取消本次启动");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedExePath,
                Arguments = AgentsDeployPaths.BuildConfigArguments(_configStore.ConfigPath),
                WorkingDirectory = Path.GetDirectoryName(resolvedExePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
            };

            _projection.ClearModuleErrors();
            _desired.ClearAll();
            AgentsDesired.TryDelete(ResolveAgentsDir(options));
            MarkStarting();
            RaiseChanged();

            if (!_host.TryStart(startInfo, out _))
            {
                return SetHostError("启动失败：无法创建 Agents 进程");
            }

            _projection.NoteHostLaunch(DateTimeOffset.Now, AgentsDeployPaths.ReadHostVersion(_options.ExecutablePath));
            _projection.RefreshModuleVersions(ResolveAgentsDir(_options));
            RaiseChanged();

            startCts = new CancellationTokenSource();
            _startCts = startCts;

            var modules = _projection.Modules;
            var enabledIds = modules
                .Select(m => m.Id)
                .Where(IsModuleEnabled)
                .ToList();

            IReadOnlyList<string> mountIds = enabledIds;
            if (mountOnly is not null)
            {
                var allow = new HashSet<string>(mountOnly, StringComparer.OrdinalIgnoreCase);
                mountIds = enabledIds.Where(id => allow.Contains(id)).ToList();
            }

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, startCts.Token);

                var up = await WaitUntilHostUpAsync(options, TimeSpan.FromSeconds(4), linked.Token)
                    .ConfigureAwait(false);
                if (!up)
                {
                    return SetHostError("已触发启动，但未检测到 Agents 进程");
                }

                await _host.AttachLinkAsync(_link, ResolveAgentsDir(options), TimeSpan.FromSeconds(3), linked.Token)
                    .ConfigureAwait(false);

                var planned = new List<string>();
                foreach (var moduleId in mountIds)
                {
                    if (ModuleRequiresDatabase(moduleId) && !IsDatabaseConnected)
                    {
                        _desired.NotePaused(moduleId);
                        _logger.Info(
                            "Agents",
                            "agents.module.skip_mount_db",
                            "Skipped mounting database-bound module while disconnected",
                            new { moduleId });
                        continue;
                    }

                    try
                    {
                        PrepareModuleSettings(options, moduleId);
                    }
                    catch (Exception ex)
                    {
                        return FailMountWithHostAlive(moduleId, $"模块 settings 准备失败：{ex.Message}");
                    }

                    planned.Add(moduleId);
                }

                _desired.Replace(planned);
                foreach (var id in planned)
                {
                    _projection.SetModuleState(id, AgentsRunState.Starting);
                    _desired.Unpause(id);
                }

                RaiseChanged();
                PublishDesired(options);

                foreach (var moduleId in planned)
                {
                    var ready = await WaitUntilModuleReadyAsync(options, moduleId, TimeSpan.FromSeconds(15), linked.Token)
                        .ConfigureAwait(false);
                    if (!ready)
                    {
                        var hostMsg = ReadHostStatus(options)?.FindModule(moduleId)?.LastError;
                        return FailMountWithHostAlive(
                            moduleId,
                            string.IsNullOrWhiteSpace(hostMsg)
                                ? $"已请求启动 {moduleId}，但未完成自检（配置错误或启动失败）"
                                : hostMsg);
                    }

                    var agentsDir = ResolveAgentsDir(options);
                    var version = agentsDir is null
                        ? null
                        : AgentsDeployPaths.ReadModuleVersion(agentsDir, moduleId);
                    _projection.NoteModuleLaunch(moduleId, version);
                }

                if (mountOnly is not null)
                {
                    foreach (var id in mountOnly)
                    {
                        if (planned.Any(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (ModuleRequiresDatabase(id) && !IsDatabaseConnected)
                        {
                            return FailMountWithHostAlive(id, $"数据库未连接，无法启动 {id}");
                        }

                        return FailMountWithHostAlive(id, $"Host 已启动，但 {id} 未挂载");
                    }
                }

                _host.ClearLaunching();
                _projection.NoteHostReady(AgentsDeployPaths.ReadHostVersion(_options.ExecutablePath));
                _projection.RefreshModuleVersions(ResolveAgentsDir(_options));

                RefreshState();
                RaiseChanged();

                var mounted = planned.Count > 0;
                return new AgentsCommandResult(
                    true,
                    wasActive
                        ? (mounted ? "已重启" : "Agents 已重启")
                        : (mounted ? "已启动" : "Agents 已启动"));
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await StopHostAsync(options, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warn("Agents", "agents.cancel.host_stop_fail", "Failed to stop Host after cancel", ex);
                }

                _host.ClearLaunching();
                _desired.ClearAll();

                RefreshState();
                RaiseChanged();
                return new AgentsCommandResult(true, "已取消", SuppressToast: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.start_or_restart.fail", "Agents start/restart failed", ex);
            return SetHostError($"启动失败：{ex.Message}", log: false);
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
        CancelStart();

        await _commandGate.WaitAsync(ct).ConfigureAwait(false);

        AgentsOptions options;
        try
        {
            NoteHostCommandIssued();

            lock (_gate)
            {
                options = AgentsOptionsModel.Clone(_options);
            }

            _desired.ClearAll();
            AgentsDesired.TryDelete(ResolveAgentsDir(options));

            if (!OperatingSystem.IsWindows())
            {
                _projection.ClearHostError();
                _projection.ClearModuleErrors();
                _host.ClearLaunching();
                _desired.ClearAll();

                RefreshState();
                RaiseChanged();
                return new AgentsCommandResult(true, "已停止", SuppressToast: true);
            }

            if (!await StopHostAsync(options, ct).ConfigureAwait(false))
            {
                return SetHostError("停止失败：Host 仍在运行");
            }

            await Task.Delay(250, ct).ConfigureAwait(false);

            _projection.ClearHostError();
            _projection.ClearModuleErrors();
            _host.ClearPid();
            _host.ClearLaunching();
            _desired.ClearAll();

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, "已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.stop.fail", "Agents stop failed", ex);
            return SetHostError($"停止失败：{ex.Message}", log: false);
        }
        finally
        {
            _commandGate.Release();
        }
    }


    private void ScheduleStableBinaryChanges()
    {
        if (_disposed
            || DateTime.UtcNow.Ticks < Volatile.Read(ref _binaryRetryUtcTicks)
            || Interlocked.CompareExchange(ref _applyingBinaryChanges, 1, 0) != 0)
        {
            return;
        }

        AgentsBinaryStamp hostStamp;
        bool hostChanged;
        lock (_gate)
        {
            _host.SyncBinaryWatch(_options);
            hostChanged = _host.DetectBinaryChange(_projection.HostState, out hostStamp);
        }

        if (!hostChanged)
        {
            Volatile.Write(ref _applyingBinaryChanges, 0);
            return;
        }

        TaskObserve.Observe(
            ApplyHostBinaryChangeAsync(hostStamp),
            "Agents",
            "agents.binary_change.detached.fail");
    }

    private async Task ApplyHostBinaryChangeAsync(AgentsBinaryStamp stamp)
    {
        var retry = false;
        try
        {
            if (_disposed)
            {
                return;
            }

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
                _host.AcceptBinary(stamp);
            }

            _logger.Info(
                "Agents",
                "agents.host_binary.reloaded",
                "Restarted Agents after Host binary changed");
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


    private void MarkStarting(bool host = true)
    {
        CancelStart();
        _host.MarkLaunching();
        if (host)
        {
            _projection.MarkHostStarting();
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

        _host.ClearLaunching();
    }


    private async Task<bool> WaitUntilHostUpAsync(AgentsOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (IsHostLive(options))
            {
                return true;
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        return IsHostLive(options);
    }


    private async Task<bool> WaitUntilStoppedAsync(AgentsOptions options, TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsHostLive(options))
            {
                return true;
            }

            await Task.Delay(180, ct).ConfigureAwait(false);
        }

        return !IsHostLive(options);
    }

    private async Task<bool> StopHostAsync(AgentsOptions options, CancellationToken ct)
    {
        _desired.Clear();
        _projection.ClearModuleErrors();
        PublishDesired(options);

        try
        {
            _ = _link.TrySendQuit();
        }
        catch
        {
            // 未连管道时仅依赖 ForceKillHost
        }

        if (!IsHostLive(options))
        {
            ClearStoppedHostArtifacts(options);
            // Host 已不在：复核扫掉脱离树的模块残进程
            _ = ClearModuleOrphans(options);
            return true;
        }

        if (await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(6), ct).ConfigureAwait(false))
        {
            ClearStoppedHostArtifacts(options);
            _ = ClearModuleOrphans(options);
            return true;
        }

        _host.ForceKill(_link, ReadHostStatus(options));
        AgentsStatus.TryDelete(ResolveAgentsDir(options));
        _link.Disconnect();

        var down = await WaitUntilStoppedAsync(options, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
        if (down)
        {
            ClearStoppedHostArtifacts(options);
            _ = ClearModuleOrphans(options);
        }

        return down;
    }

    private void ClearStoppedHostArtifacts(AgentsOptions options)
    {
        AgentsStatus.TryDelete(ResolveAgentsDir(options));
        AgentsDesired.TryDelete(ResolveAgentsDir(options));
        _link.Disconnect();
        _host.ClearPid();
    }

    /// <summary>
    /// Host 启停闸门：清模块入口残留。catalog 空时扫盘一次（仅闸门，不进 poll）
    /// </summary>
    private bool ClearModuleOrphans(AgentsOptions options)
    {
        IReadOnlyList<ModuleDescriptor> modules;
        lock (_gate)
        {
            modules = _projection.Modules;
        }

        if (modules.Count == 0)
        {
            var agentsDir = ResolveAgentsDir(options);
            if (!string.IsNullOrWhiteSpace(agentsDir))
            {
                modules = AgentsPath.ScanModules(agentsDir);
            }
        }

        return AgentsModuleOrphans.Clear(options, modules, _logger);
    }

    /// <summary>
    /// Host 已活；仅模块挂载失败。结束冷启 launching，勿把 Host 留在 Starting
    /// </summary>
    private AgentsCommandResult FailMountWithHostAlive(string moduleId, string message)
    {
        _host.ClearLaunching();
        _projection.NoteHostReady(AgentsDeployPaths.ReadHostVersion(_options.ExecutablePath));
        return SetModuleError(moduleId, message);
    }

    private AgentsCommandResult ValidateHostLaunch()
    {
        var cfg = _configStore.Load();

        var modules = _projection.Modules;

        var enabledDbBound = modules
            .Where(m => IsModuleEnabled(m.Id) && m.RequiresDatabase)
            .ToList();

        // 仅当存在启用且依赖库的模块时才校验 Postgres 字段；Host 本身不连库
        if (enabledDbBound.Count > 0)
        {
            var host = ValidatePostgresLaunchConfig();
            if (!host.Ok)
            {
                return host;
            }
        }
        else if (cfg.SchemaVersion != 2)
        {
            return new AgentsCommandResult(
                false,
                $"配置版本不受支持：{cfg.SchemaVersion}（仅支持 SchemaVersion=2）");
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

    private AgentsCommandResult ValidatePostgresLaunchConfig()
    {
        var cfg = _configStore.Load();
        var pg = cfg.Postgres;
        return AgentsConfigValidator.ValidateForLaunch(new AgentsConfigValidator.LaunchContext(
            cfg.SchemaVersion,
            pg.Host,
            pg.Port,
            pg.Database,
            pg.Username,
            pg.Password));
    }


    private AgentsCommandResult ValidateHostBundleCompat()
        => AgentsDesktopCompat.Validate(
            _releaseVersion.Current.DesktopVersion,
            MinDesktop,
            MaxDesktop);

}
