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
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

/// <summary>
/// 模块 desired：本侧筛过允许挂载的 id 再写；依赖 PacAPI 的模块还要过 settings 门禁
/// </summary>
public sealed partial class AgentsRuntime
{
    private bool IsApiReady
        => _availability is not null
           && ConnectionView.From(_availability.Current, _availability.IsConfigured).Kind == ConnectionKind.Up;

    private void OnApiAvailabilityChanged()
        => QueueApiModuleLifecycle(connected: IsApiReady);

    private bool ModuleRequiresApi(string moduleId)
        => _projection.Modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
            is { RequiresApi: true };

    private void QueueApiModuleLifecycle(bool connected)
    {
        if (_disposed)
        {
            return;
        }

        Volatile.Write(ref _apiLifecycleWant, connected ? 1 : 0);
        _ = PumpApiModuleLifecycleAsync();
    }

    private async Task PumpApiModuleLifecycleAsync()
    {
        if (Interlocked.Exchange(ref _apiLifecycleBusy, 1) == 1)
        {
            return;
        }

        try
        {
            while (!_disposed)
            {
                var want = Interlocked.Exchange(ref _apiLifecycleWant, -1);
                if (want < 0)
                {
                    break;
                }

                try
                {
                    if (want == 1)
                    {
                        await ResumeApiModulesAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await PauseApiModulesAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(
                        "Agents",
                        "agents.db_module_lifecycle.fail",
                        "API-bound module lifecycle sync failed",
                        ex,
                        new { want });
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _apiLifecycleBusy, 0);
            if (!_disposed && Volatile.Read(ref _apiLifecycleWant) >= 0)
            {
                _ = PumpApiModuleLifecycleAsync();
            }
        }
    }

    private async Task PauseApiModulesAsync(CancellationToken ct)
    {
        await _commandGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            AgentsOptions options;
            List<string> toUnmount;
            lock (_gate)
            {
                options = AgentsOptionsModel.Clone(_options);
                toUnmount = _projection.Modules
                    .Where(m => m.RequiresApi)
                    .Select(m => m.Id)
                    .Where(id =>
                    {
                        var state = _projection.GetModuleState(id);
                        return state is AgentsRunState.Running or AgentsRunState.Starting
                               || _desired.Contains(id);
                    })
                    .ToList();

                foreach (var moduleId in toUnmount)
                {
                    _desired.Pause(moduleId);
                }
            }

            if (toUnmount.Count == 0)
            {
                return;
            }

            PublishDesired(options);
            _logger.Info(
                "Agents",
                "agents.module.pause_api",
                "Unmounted API-bound modules via desired after disconnect",
                new { moduleIds = toUnmount.ToArray() });
            RefreshState();
            RaiseChanged();
            await Task.CompletedTask.ConfigureAwait(false);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task ResumeApiModulesAsync(CancellationToken ct)
    {
        await _commandGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            AgentsOptions options;
            lock (_gate)
            {
                options = AgentsOptionsModel.Clone(_options);
            }

            var toStart = _desired.SnapshotPaused();
            if (toStart.Count == 0 || !IsHostLive(options))
            {
                return;
            }

            foreach (var moduleId in toStart)
            {
                if (!IsModuleEnabled(moduleId) || !ModuleRequiresApi(moduleId))
                {
                    _desired.Unpause(moduleId);
                    continue;
                }

                if (!IsApiReady)
                {
                    break;
                }

                var mount = await MountModuleAsync(options, moduleId, ct).ConfigureAwait(false);
                if (mount.Ok)
                {
                    _desired.Unpause(moduleId);
                    _logger.Info(
                        "Agents",
                        "agents.module.resume_api",
                        "Restarted API-bound module after reconnect",
                        new { moduleId });
                }
                else
                {
                    _logger.Warn(
                        "Agents",
                        "agents.module.resume_api.fail",
                        "Failed to restart API-bound module after reconnect",
                        null,
                        new { moduleId, mount.Message });
                }
            }

            RefreshState();
            RaiseChanged();
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
                return new AgentsCommandResult(true, $"{moduleId} 已停止", SuppressToast: true);
            }

            AgentsOptions options;
            lock (_gate)
            {
                options = AgentsOptionsModel.Clone(_options);
            }

            _projection.ClearModuleError(moduleId);

            if (!await EnsureModuleStoppedAsync(options, moduleId, ct).ConfigureAwait(false))
            {
                return SetModuleError(moduleId, $"停止失败：{moduleId} 进程仍在运行");
            }

            _projection.ClearModuleError(moduleId);
            _desired.Unpause(moduleId);

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, $"{moduleId} 已停止");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.module_stop.fail", "Module stop failed", ex, new { moduleId });
            return SetModuleError(moduleId, $"停止失败：{ex.Message}", log: false);
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
                options = AgentsOptionsModel.Clone(_options);
                modulesChanged = _projection.TryApplyCatalogFromCaches(
                    ResolveAgentsDir(options),
                    _link,
                    TimeSpan.FromMinutes(30));
                modules = _projection.Modules;
            }

            if (modulesChanged)
            {
                TryPersistNormalizedModules();
            }

            if (modules.All(m => !string.Equals(m.Id, moduleId, StringComparison.Ordinal)))
            {
                return SetModuleError(moduleId, $"未发现模块：{moduleId}");
            }

            var entryPath = AgentsDeployPaths.ResolveModuleEntry(options, moduleId);
            if (string.IsNullOrWhiteSpace(entryPath) || !File.Exists(entryPath))
            {
                return SetModuleError(moduleId, $"模块入口缺失：{entryPath ?? moduleId}");
            }

            var validate = ValidateModuleSettings(moduleId);
            if (!validate.Ok)
            {
                return SetModuleError(moduleId, validate.Message);
            }

            if (!IsHostLive(options))
            {
                var boot = await ExecuteStartOrRestartAsync(ct, mountOnly: [moduleId])
                    .ConfigureAwait(false);
                return boot.Ok
                    ? new AgentsCommandResult(true, $"{moduleId} 已启动")
                    : boot;
            }

            // Attach 会拆现有会话
            if (!_link.IsLinkConnected)
            {
                await _host.AttachLinkAsync(_link, ResolveAgentsDir(options), TimeSpan.FromSeconds(2), ct)
                    .ConfigureAwait(false);
            }

            var mount = await MountModuleAsync(options, moduleId, ct).ConfigureAwait(false);
            if (!mount.Ok)
            {
                return mount;
            }

            _desired.Unpause(moduleId);

            RefreshState();
            RaiseChanged();
            return new AgentsCommandResult(true, $"{moduleId} 已启动");
        }
        catch (Exception ex)
        {
            _logger.Error("Agents", "agents.module_start.fail", "Module start failed", ex, new { moduleId });
            return SetModuleError(moduleId, $"启动失败：{ex.Message}", log: false);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void PrepareModuleSettings(AgentsOptions options, string moduleId)
    {
        var agentsDir = ResolveAgentsDir(options)
            ?? throw new InvalidOperationException("无法解析 Agents 目录，无法准备模块 settings");
        _moduleSettings.EnsureUserSettings(moduleId, agentsDir);
    }

    private async Task<AgentsCommandResult> MountModuleAsync(
        AgentsOptions options,
        string moduleId,
        CancellationToken ct)
    {
        var admit = await TryAdmitDesiredMountAsync(moduleId, ct).ConfigureAwait(false);
        if (!admit.Ok)
        {
            return admit;
        }

        try
        {
            PrepareModuleSettings(options, moduleId);
        }
        catch (Exception ex)
        {
            return SetModuleError(moduleId, $"模块 settings 准备失败：{ex.Message}");
        }

        // 先撤 desired，Host 清 StartFailed
        if (!await EnsureModuleStoppedAsync(options, moduleId, ct).ConfigureAwait(false))
        {
            return SetModuleError(moduleId, $"重启失败：{moduleId} 进程仍在运行");
        }

        _projection.ClearModuleError(moduleId);
        _desired.Add(moduleId);
        _projection.SetModuleState(moduleId, AgentsRunState.Starting);

        RaiseChanged();
        PublishDesired(options);

        bool ready;
        try
        {
            ready = await WaitUntilModuleReadyAsync(options, moduleId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _desired.Remove(moduleId);
            PublishDesired(options);
            throw;
        }

        if (!ready)
        {
            var hostMsg = ReadHostStatus(options)?.FindModule(moduleId)?.LastError;
            return SetModuleError(
                moduleId,
                string.IsNullOrWhiteSpace(hostMsg)
                    ? $"已请求启动 {moduleId}，但未完成自检（配置错误或启动失败）"
                    : hostMsg);
        }

        var agentsDir = ResolveAgentsDir(options);
        var version = agentsDir is null ? null : AgentsDeployPaths.ReadModuleVersion(agentsDir, moduleId);
        _projection.NoteModuleLaunch(moduleId, version);

        return new AgentsCommandResult(true, $"{moduleId} 已挂载");
    }


    private void TryPersistNormalizedModules()
    {
        try
        {
            var modules = _projection.Modules;
            if (modules.Count == 0)
            {
                return;
            }

            lock (_gate)
            {
                var known = _options.Modules;
                var needsSeed = false;
                foreach (var module in modules)
                {
                    if (!known.ContainsKey(module.Id))
                    {
                        needsSeed = true;
                        break;
                    }
                }

                if (!needsSeed)
                {
                    return;
                }
            }

            _configStore.Update(root =>
            {
                root.Agents ??= new AgentsOptions();
                foreach (var module in modules)
                {
                    if (root.Agents.Modules.ContainsKey(module.Id))
                    {
                        continue;
                    }

                    root.Agents.Modules[module.Id] = new ModuleOptions { Enabled = true };
                }
            });

            lock (_gate)
            {
                var cfg = _configStore.Load();
                _options = AgentsOptionsModel.Clone(cfg.Agents);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(
                "Agents",
                "agents.modules.normalize_fail",
                "Failed to merge Agents.Modules after catalog push",
                ex);
        }
    }


    private async Task<bool> WaitUntilModuleReadyAsync(
        AgentsOptions options,
        string moduleId,
        CancellationToken ct)
    {
        // Host 写完 Failed 后再走一拍 IPC
        var end = DateTimeOffset.UtcNow + AgentsObserve.ModuleReadyTimeout + TimeSpan.FromSeconds(1);

        while (DateTimeOffset.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();

            var wire = ReadHostStatus(options)?.FindModule(moduleId);
            if (wire is not null)
            {
                if (wire.State == AgentsRunState.Running)
                {
                    return true;
                }

                if (wire.State == AgentsRunState.Failed)
                {
                    return false;
                }
            }

            if (!IsHostLive(options))
            {
                return false;
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        var last = ReadHostStatus(options)?.FindModule(moduleId);
        return last is { State: AgentsRunState.Running };
    }

    private async Task<bool> EnsureModuleStoppedAsync(AgentsOptions options, string moduleId, CancellationToken ct)
    {
        _desired.Remove(moduleId);
        _projection.ClearModuleError(moduleId);
        PublishDesired(options);

        if (!IsModuleAlive(options, moduleId))
        {
            return true;
        }

        return await WaitUntilModuleStoppedAsync(options, moduleId, AgentsObserve.ModuleReadyTimeout, ct)
            .ConfigureAwait(false);
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
            if (!IsModuleAlive(options, moduleId))
            {
                return true;
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return !IsModuleAlive(options, moduleId);
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
            // schema 是设置页能力而非运行时强制项；无 schema 模块仍须提供 JSON 对象配置
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

    private async Task<AgentsCommandResult> TryAdmitDesiredMountAsync(
        string moduleId,
        CancellationToken ct)
    {
        ModuleDescriptor? module;
        lock (_gate)
        {
            module = _projection.Modules.FirstOrDefault(
                m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));
        }

        if (module is null)
        {
            return SetModuleError(moduleId, $"未发现模块：{moduleId}");
        }

        var bound = new AgentsModuleBound(module.Id, module.MinApiContract, module.MaxApiContract);
        var outcome = await _admit.AdmitAsync(
                bound,
                IsApiReady,
                _availability?.LastContractVersion,
                ct)
            .ConfigureAwait(false);
        if (!outcome.Ok)
        {
            return SetModuleError(moduleId, outcome.Message);
        }

        return EnsureApiConfigOrError(moduleId, bound);
    }

    /// <summary>依赖 PacAPI 的模块启动前校验 Agents 独立 Key</summary>
    private AgentsCommandResult EnsureApiConfigOrError(string moduleId, AgentsModuleBound bound)
    {
        var module = _projection.Modules.FirstOrDefault(
            m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));
        if (module is not { RequiresApi: true } && !bound.RequiresApiContract)
        {
            return new AgentsCommandResult(true, "ok");
        }

        var pac = _configStore.Load().PacApi;
        if (string.IsNullOrWhiteSpace(pac.BaseUrl) || string.IsNullOrWhiteSpace(pac.AgentsApiKey))
        {
            return SetModuleError(moduleId, "请先配置 PacAPI 地址与 Agents 访问密钥");
        }

        return new AgentsCommandResult(true, "ok");
    }

    private void NoteAdmitDeniedOnColdStart(AgentsAdmitResult admit, string moduleId)
    {
        if (admit.DenyKind == AgentsAdmitDenyKind.Disconnected)
        {
            _desired.NotePaused(moduleId);
            _logger.Info(
                "Agents",
                "agents.module.skip_mount_disconnected",
                "Skipped mounting API-bound module while disconnected",
                new { moduleId });
            return;
        }

        SetModuleError(moduleId, admit.Message);
        _logger.Warn(
            "Agents",
            "agents.module.skip_mount_admit",
            "Skipped mounting API-bound module: admit gate failed",
            null,
            new { moduleId, admit.Message, admit.DenyKind });
    }

}
