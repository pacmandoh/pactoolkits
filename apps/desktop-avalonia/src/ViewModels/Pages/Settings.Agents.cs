using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    private readonly record struct SaveOptionsResult(
        bool Saved,
        bool HostChanged,
        IReadOnlyList<string> ChangedModuleIds);

    private readonly record struct RuntimeApplyResult(
        bool Applied,
        bool HostRestarted,
        int ReloadedModuleCount);

    private readonly IAgentsRuntime _agents;
    private readonly IAgentsConfigService _agentsConfig;
    private readonly IModuleSettingsStore _moduleSettings;
    private readonly object _snapshotGate = new();
    private readonly object _moduleAutoSaveSync = new();
    private readonly SemaphoreSlim _moduleSaveGate = new(1, 1);
    private readonly Dictionary<string, CancellationTokenSource> _moduleAutoSaveCts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<ModuleSettingsFieldViewModel>> _moduleAutoSaveFields = new(StringComparer.Ordinal);
    private AgentsEditorSnapshot? _savedSnapshot;
    private bool _hasPendingChanges;
    private bool _baselineReady;
    private bool _suppressPendingRecalc;
    private bool _suppressModuleAutoSave;
    private bool _agentsDisposed;
    private string _modulesSyncKey = string.Empty;
    // 编辑或控制操作期间延迟表单重建，避免模块发现覆盖尚未提交的 UI 状态
    private bool _moduleEditorsStale;

    public ObservableCollection<ModuleRunRow> ModuleRunRows { get; } = new();
    public ObservableCollection<ModuleSettingsEditor> ModuleEditors { get; } = new();
    public ObservableCollection<ModuleSettingsLoadIssue> ModuleSettingsLoadIssues { get; } = new();

    private bool _syncingFromRuntime;

    [ObservableProperty] private bool _isHostRunningSwitch;
    [ObservableProperty] private bool _isAgentsToggling;
    [ObservableProperty] private bool _isSavingSettings;
    [ObservableProperty] private ModuleSettingsEditor? _selectedModuleEditor;
    [ObservableProperty] private string _agentsExecutablePath = string.Empty;
    [ObservableProperty] private string _agentsProcessName = string.Empty;
    [ObservableProperty] private string _hostStatusText = "检测中";
    [ObservableProperty] private string _hostStatusDetail = "等待 Agents 状态刷新";
    [ObservableProperty] private string _hostVersionText = "未知";
    [ObservableProperty] private string _hostLastLaunchText = "-";
    [ObservableProperty] private string _hostLastErrorText = "-";

    public bool IsHostStatusRunning => _agents.HostState == AgentsRunState.Running;
    public bool IsHostStatusStarting => _agents.HostState == AgentsRunState.Starting;
    public bool IsHostStatusFailed => _agents.HostState == AgentsRunState.Failed;
    public bool IsHostStatusStopped => _agents.HostState == AgentsRunState.Stopped;
    public bool IsHostStatusUnknown => _agents.HostState == AgentsRunState.Unknown;

    private bool CanRestartAgents() => !IsAgentsToggling && IsHostStatusRunning;

    public bool CanSaveAgentsSettings => !IsSavingSettings && !IsAgentsToggling;

    partial void OnIsAgentsTogglingChanged(bool value)
    {
        RestartAgentsCommand.NotifyCanExecuteChanged();
        SaveAgentsSettingsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveAgentsSettings));
        foreach (var row in ModuleRunRows)
        {
            row.CanToggle = !value;
        }

        if (!value)
        {
            TryFlushStaleModuleEditors();
        }
    }

    partial void OnIsSavingSettingsChanged(bool value)
    {
        SaveAgentsSettingsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveAgentsSettings));
    }

    private void InitializeAgents()
    {
        HostVersionText = _agents.HostVersion;
        ApplyRuntimeSnapshot();
        SyncAgentsConfig(syncHost: true, syncModules: true);
        RefreshPendingChanges();

        _agents.StatusChanged += OnAgentsRuntimeChanged;
    }

    private void ReloadAgentsRuntime()
    {
        _agents.Reload();
        ApplyRuntimeSnapshot();
        if (HasModuleAutoSaves)
        {
            RefreshModuleRunRows();
            _moduleEditorsStale = true;
            return;
        }

        SyncAgentsConfig(syncHost: true, syncModules: ModulesSyncKeyChanged());
    }

    partial void OnAgentsExecutablePathChanged(string value) => RefreshPendingChanges();
    partial void OnAgentsProcessNameChanged(string value) => RefreshPendingChanges();

    public bool HasPendingChanges
        => _baselineReady && _hasPendingChanges;

    private void OnAgentsRuntimeChanged()
    {
        // 启停期间保留用户刚设置的开关值，命令结束后再以运行时状态校准
        // 存在编辑任务时只更新运行状态，不更新同步键，确保后续仍会重建模块表单
        Dispatcher.UIThread.Post(() =>
        {
            var syncRunSwitches = !IsAgentsToggling;
            ApplyRuntimeSnapshot(syncRunSwitches: syncRunSwitches);
            if (!ModulesSyncKeyChanged())
            {
                return;
            }

            if (!IsAgentsToggling && !HasPendingChanges && !HasModuleAutoSaves)
            {
                SyncAgentsConfig(syncHost: true, syncModules: true);
                return;
            }

            RefreshModuleRunRows(syncRunSwitches);
            _moduleEditorsStale = true;
        });
    }

    partial void OnIsHostRunningSwitchChanged(bool value)
    {
        if (_syncingFromRuntime)
            return;

        ObserveDetached(ToggleHostRunningAsync(value), "settings.agents.host_run.detached.fail");
    }

    partial void OnHostStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsHostStatusRunning));
        OnPropertyChanged(nameof(IsHostStatusStarting));
        OnPropertyChanged(nameof(IsHostStatusFailed));
        OnPropertyChanged(nameof(IsHostStatusStopped));
        OnPropertyChanged(nameof(IsHostStatusUnknown));
        RestartAgentsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveAgentsSettings))]
    private async Task SaveAgentsSettingsAsync()
    {
        await ApplyAgentsSettingsAsync(showSuccessToast: true);
    }

    private async Task<bool> ApplyAgentsSettingsAsync(bool showSuccessToast)
    {
        if (SkipTrigger())
        {
            return !HasPendingChanges;
        }

        IsSavingSettings = true;
        var togglingForRestart = false;
        try
        {
            var result = await SaveOptionsAsync(showToastOnError: true).ConfigureAwait(false);
            if (!result.Saved && !result.HostChanged && result.ChangedModuleIds.Count == 0)
            {
                return false;
            }

            var requiresRuntimeApply = result.HostChanged && _agents.IsHostRunning
                || result.ChangedModuleIds.Any(moduleId =>
                    _agents.GetModuleState(moduleId) is AgentsRunState.Running or AgentsRunState.Starting);
            if (requiresRuntimeApply)
            {
                togglingForRestart = true;
                await RunOnUiAsync(() => IsAgentsToggling = true);
            }

            var runtimeApply = await ApplyPersistedRuntimeChangesAsync(result).ConfigureAwait(false);
            if (!runtimeApply.Applied)
            {
                return false;
            }

            if (result.Saved && showSuccessToast)
            {
                var message = runtimeApply.HostRestarted
                    ? "配置已保存，Agents 已重启"
                    : runtimeApply.ReloadedModuleCount > 0
                        ? $"配置已保存，已重载 {runtimeApply.ReloadedModuleCount} 个模块"
                        : "配置已保存";
                _toast.Success("自动化集成", message);
            }

            // 保存流程不保留同步上下文，绑定状态和命令通知必须返回 UI 线程
            await RunOnUiAsync(() =>
            {
                ApplyRuntimeSnapshot();
                if (result.Saved)
                {
                    SyncAgentsConfig(syncHost: true, syncModules: _moduleEditorsStale);
                }
            });
            return result.Saved && !HasPendingChanges;
        }
        catch (Exception ex)
        {
            LogError("settings.agents.save.fail", "Failed to save Agents settings", ex);
            _toast.Error("自动化集成", $"保存失败：{ex.Message}");
            return false;
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                if (togglingForRestart)
                {
                    IsAgentsToggling = false;
                }

                IsSavingSettings = false;
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestartAgents))]
    private async Task RestartAgentsAsync()
    {
        if (IsAgentsToggling)
        {
            return;
        }

        if (SkipTrigger("settings.agents.restart"))
        {
            return;
        }

        if (!_agents.IsHostRunning)
        {
            return;
        }

        IsAgentsToggling = true;
        try
        {
            var saved = await SaveCurrentOptionsSilentlyAsync(applyRuntimeChanges: false).ConfigureAwait(false);
            if (!saved)
            {
                return;
            }

            var result = await ExecuteRuntimeCommandAsync(() => _agents.StartOrRestartAsync())
                .ConfigureAwait(false);
            if (!result.Ok && !result.SuppressToast)
            {
                _toast.Error("自动化集成", result.Message);
            }
        }
        catch (Exception ex)
        {
            LogError("settings.agents.restart.fail", "Failed to restart Agents runtime", ex);
            _toast.Error("自动化集成", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsAgentsToggling = false;
                ApplyRuntimeSnapshot();
            });
        }
    }

    private async Task SaveModuleEnabledAsync(string moduleId, bool enabled)
    {
        if (IsAgentsToggling)
        {
            ApplyRuntimeSnapshot();
            return;
        }

        if (SkipTrigger(
                enabled ? $"settings.agents.module.enable:{moduleId}" : $"settings.agents.module.disable:{moduleId}"))
        {
            ApplyRuntimeSnapshot();
            return;
        }

        try
        {
            await _agentsConfig
                .SetModuleEnabledAsync(moduleId, enabled, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError(
                "settings.agents.module_enable.fail",
                "Failed to save module enabled",
                ex,
                new { moduleId, enabled });
            _toast.Error("自动化集成", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => ApplyRuntimeSnapshot());
        }
    }

    private async Task ToggleHostRunningAsync(bool running)
    {
        if (IsAgentsToggling)
        {
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        if (SkipTrigger(running ? "settings.agents.host.start" : "settings.agents.host.stop"))
        {
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        IsAgentsToggling = true;
        try
        {
            if (running)
            {
                var saved = await SaveCurrentOptionsSilentlyAsync(applyRuntimeChanges: false).ConfigureAwait(false);
                if (!saved)
                {
                    return;
                }

                var result = await ExecuteRuntimeCommandAsync(() => _agents.StartOrRestartAsync())
                    .ConfigureAwait(false);
                if (!result.Ok && !result.SuppressToast)
                {
                    _toast.Error("自动化集成", result.Message);
                }
            }
            else
            {
                var result = await _agents.StopAsync().ConfigureAwait(false);
                if (!result.Ok && !result.SuppressToast)
                {
                    _toast.Error("自动化集成", result.Message);
                }
            }
        }
        catch (Exception ex)
        {
            LogError("settings.agents.host_run.fail", "Failed to toggle Host running", ex, new { running });
            _toast.Error("自动化集成", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 启用前先同步运行时状态，避免 ToggleSwitch 的旧绑定值覆盖本次操作
                ApplyRuntimeSnapshot(syncRunSwitches: true);
                IsAgentsToggling = false;
            });
        }
    }

    private async Task ToggleModuleRunningAsync(string moduleId, bool running)
    {
        if (IsAgentsToggling)
        {
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        if (SkipTrigger(running ? $"settings.agents.module.start:{moduleId}" : $"settings.agents.module.stop:{moduleId}"))
        {
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        if (running && !_agents.IsModuleEnabled(moduleId))
        {
            _toast.Error("自动化集成", $"请先勾选启用 {moduleId}");
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        IsAgentsToggling = true;
        try
        {
            if (running)
            {
                var saved = await SaveCurrentOptionsSilentlyAsync(
                        applyRuntimeChanges: true,
                        skippedModuleId: moduleId)
                    .ConfigureAwait(false);
                if (!saved)
                {
                    return;
                }

                var result = _agents.GetModuleState(moduleId) == AgentsRunState.Running
                    ? new AgentsCommandResult(true, $"{moduleId} 已启动")
                    : await ExecuteRuntimeCommandAsync(() => _agents.StartModuleAsync(moduleId))
                        .ConfigureAwait(false);
                if (!result.Ok && !result.SuppressToast)
                {
                    _toast.Error("自动化集成", result.Message);
                }
            }
            else
            {
                var result = await _agents.StopModuleAsync(moduleId).ConfigureAwait(false);
                if (!result.Ok && !result.SuppressToast)
                {
                    _toast.Error("自动化集成", result.Message);
                }
            }
        }
        catch (Exception ex)
        {
            LogError(
                "settings.agents.module_run.fail",
                "Failed to toggle module running",
                ex,
                new { moduleId, running });
            _toast.Error("自动化集成", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyRuntimeSnapshot(syncRunSwitches: true);
                IsAgentsToggling = false;
            });
        }
    }

    private async Task RevertRunSwitchesAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => ApplyRuntimeSnapshot(syncRunSwitches: true));
    }

    private async Task<bool> SaveCurrentOptionsSilentlyAsync(
        bool applyRuntimeChanges,
        string? skippedModuleId = null)
    {
        try
        {
            var result = await SaveOptionsAsync(showToastOnError: true).ConfigureAwait(false);
            if (applyRuntimeChanges
                && (result.Saved || result.HostChanged || result.ChangedModuleIds.Count > 0))
            {
                var applied = await ApplyPersistedRuntimeChangesAsync(result, skippedModuleId)
                    .ConfigureAwait(false);
                if (!applied.Applied)
                {
                    return false;
                }
            }

            return result.Saved;
        }
        catch (Exception ex)
        {
            LogError("settings.agents.save_options.silent_fail", "Silent save options failed", ex);
            _toast.Error("自动化集成", $"配置保存失败：{ex.Message}");
            return false;
        }
    }

    private async Task<RuntimeApplyResult> ApplyPersistedRuntimeChangesAsync(
        SaveOptionsResult result,
        string? skippedModuleId = null)
    {
        if (result.HostChanged && _agents.IsHostRunning)
        {
            var restart = await ExecuteRuntimeCommandAsync(() => _agents.StartOrRestartAsync())
                .ConfigureAwait(false);
            if (!restart.Ok)
            {
                _toast.Error("自动化集成", $"配置已保存，但 Agents 重启失败：{restart.Message}");
                return new RuntimeApplyResult(false, false, 0);
            }

            return new RuntimeApplyResult(true, true, 0);
        }

        var reloadedModuleCount = 0;
        foreach (var moduleId in result.ChangedModuleIds)
        {
            if (string.Equals(moduleId, skippedModuleId, StringComparison.Ordinal))
            {
                continue;
            }

            var state = _agents.GetModuleState(moduleId);
            if (state is not (AgentsRunState.Running or AgentsRunState.Starting))
            {
                continue;
            }

            var reload = await ExecuteRuntimeCommandAsync(() => _agents.StartModuleAsync(moduleId))
                .ConfigureAwait(false);
            if (!reload.Ok)
            {
                _toast.Error("自动化集成", $"配置已保存，但模块重载失败：{reload.Message}");
                return new RuntimeApplyResult(false, false, reloadedModuleCount);
            }

            reloadedModuleCount++;
        }

        return new RuntimeApplyResult(true, false, reloadedModuleCount);
    }

    private static async Task<AgentsCommandResult> ExecuteRuntimeCommandAsync(
        Func<Task<AgentsCommandResult>> command)
    {
        const int maxAttempts = 9;
        AgentsCommandResult result = default;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            result = await command().ConfigureAwait(false);
            if (result.Ok || !result.SuppressToast)
            {
                return result;
            }

            if (attempt == maxAttempts - 1)
            {
                break;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<SaveOptionsResult> SaveOptionsAsync(bool showToastOnError)
    {
        CancelModuleAutoSaves();
        await _moduleSaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await PersistOptionsAsync(showToastOnError).ConfigureAwait(false);
        }
        finally
        {
            _moduleSaveGate.Release();
        }
    }

    private async Task<SaveOptionsResult> PersistOptionsAsync(bool showToastOnError)
    {
        foreach (var editor in ModuleEditors)
        {
            var error = editor.Validate();
            if (error is not null)
            {
                _toast.Error("自动化集成", error);
                return new SaveOptionsResult(false, false, []);
            }
        }

        var currentSnapshot = BuildCurrentSnapshot();
        if (currentSnapshot is null)
        {
            if (showToastOnError)
            {
                _toast.Error("自动化集成", "无法读取当前模块配置");
            }

            return new SaveOptionsResult(false, false, []);
        }

        var persistedModuleIds = new List<string>();
        var hostPersisted = false;
        var hostChanged = false;
        var desiredHost = new AgentsConfigDto
        {
            ExecutablePath = currentSnapshot.AgentsExecutablePath,
            ProcessName = currentSnapshot.AgentsProcessName,
        };
        try
        {
            var cfg = _agentsConfig.Load();
            hostChanged = !SameAgentsHost(cfg, desiredHost);

            var changedModules = new List<(string ModuleId, string Json)>();
            foreach (var (moduleId, json) in currentSnapshot.ModuleSettingsJson)
            {
                var savedJson = _moduleSettings.LoadSettingsJson(moduleId);
                if (!SameJson(savedJson, json))
                {
                    changedModules.Add((moduleId, json));
                }
            }

            var moduleChanged = changedModules.Count > 0;

            if (!hostChanged && !moduleChanged)
            {
                await CommitSavedSnapshotAsync(currentSnapshot).ConfigureAwait(false);
                return new SaveOptionsResult(true, false, []);
            }

            if (moduleChanged)
            {
                foreach (var (moduleId, json) in changedModules)
                {
                    await _moduleSettings
                        .SaveSettingsJsonAsync(moduleId, json, CancellationToken.None)
                        .ConfigureAwait(false);
                    persistedModuleIds.Add(moduleId);
                }
            }

            if (hostChanged)
            {
                var next = new AgentsConfigDto
                {
                    ExecutablePath = currentSnapshot.AgentsExecutablePath,
                    ProcessName = currentSnapshot.AgentsProcessName,
                    Modules = cfg.Modules,
                };
                await _agentsConfig.SaveAsync(next, CancellationToken.None).ConfigureAwait(false);
                hostPersisted = true;
            }

            await CommitSavedSnapshotAsync(currentSnapshot).ConfigureAwait(false);
            return new SaveOptionsResult(
                true,
                hostChanged,
                persistedModuleIds);
        }
        catch (Exception ex)
        {
            if (hostChanged && !hostPersisted)
            {
                try
                {
                    hostPersisted = SameAgentsHost(_agentsConfig.Load(), desiredHost);
                }
                catch (Exception verifyEx)
                {
                    LogError(
                        "settings.agents.save_options.verify_fail",
                        "Failed to verify Agents host config after save failure",
                        verifyEx);
                }
            }

            LogError("settings.agents.save_options.fail", "Failed to save Agents options to config", ex);
            if (showToastOnError)
            {
                _toast.Error("自动化集成", $"配置保存失败：{ex.Message}");
            }

            return new SaveOptionsResult(false, hostPersisted, persistedModuleIds);
        }
    }

    private Task CommitSavedSnapshotAsync(AgentsEditorSnapshot snapshot)
        => RunOnUiAsync(() =>
        {
            _savedSnapshot = snapshot;
            _baselineReady = true;
            RefreshPendingChanges();
        });

    private static bool SameAgentsHost(AgentsConfigDto left, AgentsConfigDto right)
        => string.Equals(left.ExecutablePath?.Trim(), right.ExecutablePath?.Trim(), StringComparison.Ordinal)
           && string.Equals(left.ProcessName?.Trim(), right.ProcessName?.Trim(), StringComparison.Ordinal);

    private static bool SameJson(string left, string right)
    {
        try
        {
            var a = JsonNode.Parse(string.IsNullOrWhiteSpace(left) ? "{}" : left);
            var b = JsonNode.Parse(string.IsNullOrWhiteSpace(right) ? "{}" : right);
            return JsonNode.DeepEquals(a, b);
        }
        catch
        {
            // 无法解析时仍比较规范化文本，避免错误地将不同配置视为相同
            return string.Equals(
                (left ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
                (right ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
                StringComparison.Ordinal);
        }
    }

    private void ApplyRuntimeSnapshot(bool syncRunSwitches = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyRuntimeSnapshot(syncRunSwitches));
            return;
        }

        _syncingFromRuntime = true;
        try
        {
            // 运行开关仅表示已完成启动，Starting 和 Failed 通过独立状态组件展示
            if (syncRunSwitches)
            {
                IsHostRunningSwitch = _agents.HostState == AgentsRunState.Running;
            }

            HostVersionText = string.IsNullOrWhiteSpace(_agents.HostVersion) ? "未知" : _agents.HostVersion;
            HostLastLaunchText = _agents.HostLastLaunchAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            HostLastErrorText = string.IsNullOrWhiteSpace(_agents.HostLastError) ? "-" : _agents.HostLastError!;

            var hostState = _agents.HostState;
            HostStatusText = hostState switch
            {
                AgentsRunState.Running => "运行中",
                AgentsRunState.Starting => "启动中",
                AgentsRunState.Failed => "启动失败",
                AgentsRunState.Stopped => "未启动",
                _ => "未知",
            };
            HostStatusDetail = hostState switch
            {
                AgentsRunState.Running => "Agents 进程已运行，可在本页或标题栏启停",
                AgentsRunState.Starting => "正在拉起 Agents 进程",
                AgentsRunState.Failed => string.IsNullOrWhiteSpace(_agents.HostLastError)
                    ? "Agents 启动失败，请检查可执行路径后重试"
                    : _agents.HostLastError!,
                AgentsRunState.Stopped => "Agents 未运行，打开下方开关即可启动",
                _ => "Agents 状态检测异常，请检查进程名和可执行路径",
            };

            foreach (var row in ModuleRunRows)
            {
                ApplyModuleRunRow(row, hostState, syncRunSwitches);
            }
        }
        finally
        {
            _syncingFromRuntime = false;
        }
    }

    private void ApplyModuleRunRow(ModuleRunRow row, AgentsRunState hostState, bool syncRunSwitches)
    {
        var enabled = _agents.IsModuleEnabled(row.Id);
        var state = _agents.GetModuleState(row.Id);
        var lastError = _agents.GetModuleLastError(row.Id);
        var version = _agents.GetModuleVersion(row.Id);

        row.IsEnabled = enabled;
        if (syncRunSwitches)
        {
            row.IsRunningSwitch = state == AgentsRunState.Running;
        }

        row.CanToggle = !IsAgentsToggling;
        row.VersionText = string.IsNullOrWhiteSpace(version) ? "未知" : version;
        row.LastLaunchText = _agents.GetModuleLastLaunchAt(row.Id)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        row.LastErrorText = string.IsNullOrWhiteSpace(lastError) ? "-" : lastError!;

        row.StatusText = state switch
        {
            AgentsRunState.Running => "运行中",
            AgentsRunState.Starting => "启动中",
            AgentsRunState.Failed => "启动失败",
            AgentsRunState.Stopped => "未启动",
            _ => "未知",
        };
        row.StatusDetail = state switch
        {
            AgentsRunState.Running => $"{row.DisplayName} 已就绪",
            AgentsRunState.Starting => $"正在等待 {row.DisplayName} 自检完成",
            AgentsRunState.Failed => string.IsNullOrWhiteSpace(lastError)
                ? $"{row.DisplayName} 启动未完成，请检查模块配置后重试"
                : lastError!,
            AgentsRunState.Stopped => !enabled
                ? $"{row.DisplayName} 未启用"
                : hostState is not AgentsRunState.Running
                    ? "Agents 未运行，模块随 Agents 停止"
                    : $"{row.DisplayName} 未运行",
            _ => $"{row.DisplayName} 状态检测异常",
        };

        row.IsStatusUnknown = state == AgentsRunState.Unknown;
        row.IsStatusStarting = state == AgentsRunState.Starting;
        row.IsStatusRunning = state == AgentsRunState.Running;
        row.IsStatusFailed = state == AgentsRunState.Failed;
        row.IsStatusStopped = state == AgentsRunState.Stopped;
    }

    private void SyncAgentsConfig(bool syncHost, bool syncModules)
    {
        var cfg = _agentsConfig.Load();

        void Apply()
        {
            lock (_snapshotGate)
            {
                _suppressPendingRecalc = true;
                if (syncHost)
                {
                    AgentsExecutablePath = cfg.ExecutablePath;
                    AgentsProcessName = cfg.ProcessName;
                }

                var agentsDir = TryResolveAgentsDir();
                RefreshModuleRunRows();
                _modulesSyncKey = BuildModulesSyncKey(agentsDir);

                if (syncModules)
                {
                    var selectedModuleId = SelectedModuleEditor?.ModuleId;
                    UnwireModuleEditors();
                    ModuleEditors.Clear();
                    ModuleSettingsLoadIssues.Clear();

                    if (agentsDir is not null)
                    {
                        foreach (var module in _agents.Modules)
                        {
                            try
                            {
                                var schemaJson = _moduleSettings.TryLoadSchemaJson(module.Id, agentsDir);
                                var schema = ModuleSettingsEditor.ParseSchema(schemaJson);
                                if (schema is null)
                                {
                                    ModuleSettingsLoadIssues.Add(new ModuleSettingsLoadIssue(
                                        module.DisplayName,
                                        "settings.schema.json 无效或缺失"));
                                    continue;
                                }

                                _moduleSettings.EnsureUserSettings(module.Id, agentsDir);
                                var settingsJson = _moduleSettings.LoadSettingsJson(module.Id);
                                var settings = ModuleSettingsEditor.ParseSettings(settingsJson);
                                var editor = new ModuleSettingsEditor(
                                    module.Id,
                                    module.DisplayName,
                                    schema,
                                    settings);
                                ModuleEditors.Add(editor);
                                WireModuleEditor(editor);
                            }
                            catch (Exception ex)
                            {
                                ModuleSettingsLoadIssues.Add(new ModuleSettingsLoadIssue(
                                    module.DisplayName,
                                    $"设置加载失败：{ex.Message}"));
                                LogWarn(
                                    "settings.agents.module_editor.load_fail",
                                    "Failed to load module settings editor",
                                    ex,
                                    new { moduleId = module.Id });
                            }
                        }
                    }

                    SelectedModuleEditor = ModuleEditors.FirstOrDefault(editor =>
                                               string.Equals(editor.ModuleId, selectedModuleId, StringComparison.Ordinal))
                                           ?? ModuleEditors.FirstOrDefault();
                    _moduleEditorsStale = false;
                    ApplyPendingModuleSelection(discardIfMissing: true);
                }

                var current = BuildCurrentSnapshot();
                if (current is not null)
                {
                    var baseline = _savedSnapshot ?? current;
                    _savedSnapshot = new AgentsEditorSnapshot(
                        syncHost ? current.AgentsExecutablePath : baseline.AgentsExecutablePath,
                        syncHost ? current.AgentsProcessName : baseline.AgentsProcessName,
                        syncModules ? current.ModuleSettingsJson : baseline.ModuleSettingsJson);
                    _baselineReady = true;
                }
                _suppressPendingRecalc = false;
                RefreshPendingChanges();
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private string? TryResolveAgentsDir()
    {
        var resolution = AgentsPath.ResolveHost(AgentsExecutablePath, AppContext.BaseDirectory);
        var exe = resolution.ResolvedPath;
        return exe is null ? null : Path.GetDirectoryName(exe);
    }

    private bool ModulesSyncKeyChanged()
    {
        var cfg = _agentsConfig.Load();
        var resolution = AgentsPath.ResolveHost(cfg.ExecutablePath, AppContext.BaseDirectory);
        var agentsDir = resolution.ResolvedPath is null
            ? null
            : Path.GetDirectoryName(resolution.ResolvedPath);
        return !string.Equals(
            _modulesSyncKey,
            BuildModulesSyncKey(agentsDir, cfg.ExecutablePath, cfg.ProcessName),
            StringComparison.Ordinal);
    }

    private string BuildModulesSyncKey(string? agentsDir)
    {
        var cfg = _agentsConfig.Load();
        return BuildModulesSyncKey(agentsDir, cfg.ExecutablePath, cfg.ProcessName);
    }

    private string BuildModulesSyncKey(string? agentsDir, string executablePath, string processName)
    {
        var configDir = Path.GetDirectoryName(_appConfigStore.ConfigPath);
        var catalog = string.Join(
            '|',
            _agents.Modules
                .Select(module =>
                {
                    var schemaPath = agentsDir is null
                        ? null
                        : AgentsPaths.ModuleSettingsSchemaPath(agentsDir, module.Id);
                    var settingsPath = configDir is null
                        ? null
                        : AgentsPaths.ModuleSettingsPath(configDir, module.Id);
                    return string.Join(
                        ':',
                        module.Id,
                        module.Version,
                        module.Runtime,
                        module.DisplayName,
                        module.EntryWinX64,
                        module.Desktop.Order,
                        GetFileSyncToken(schemaPath),
                        GetFileSyncToken(settingsPath));
                })
                .OrderBy(part => part, StringComparer.Ordinal));
        // 同步键包含磁盘 Host 配置，确保外部配置更新也能触发表单重建
        return string.Join(
            '\n',
            catalog,
            agentsDir ?? string.Empty,
            (executablePath ?? string.Empty).Trim(),
            (processName ?? string.Empty).Trim());
    }

    private static string GetFileSyncToken(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "missing";
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? $"{info.Length}@{info.LastWriteTimeUtc.Ticks}"
                : "missing";
        }
        catch (IOException)
        {
            return "unavailable";
        }
        catch (UnauthorizedAccessException)
        {
            return "unavailable";
        }
    }

    private void TryFlushStaleModuleEditors()
    {
        if (_agentsDisposed
            || !_moduleEditorsStale
            || IsAgentsToggling
            || HasPendingChanges
            || HasModuleAutoSaves)
        {
            return;
        }

        SyncAgentsConfig(syncHost: true, syncModules: true);
    }

    private void RefreshModuleRunRows(bool syncRunSwitches = true)
    {
        var modules = _agents.Modules;
        var hostState = _agents.HostState;

        void ApplyList()
        {
            var byId = ModuleRunRows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var nextIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var module in modules)
            {
                nextIds.Add(module.Id);
                if (!byId.TryGetValue(module.Id, out var row))
                {
                    row = new ModuleRunRow(module.Id, module.DisplayName);
                    WireModuleRunRow(row);
                    ModuleRunRows.Add(row);
                }
                else if (!string.Equals(row.DisplayName, module.DisplayName, StringComparison.Ordinal))
                {
                    row.DisplayName = module.DisplayName;
                }

                _syncingFromRuntime = true;
                try
                {
                    ApplyModuleRunRow(row, hostState, syncRunSwitches);
                }
                finally
                {
                    _syncingFromRuntime = false;
                }
            }

            for (var i = ModuleRunRows.Count - 1; i >= 0; i--)
            {
                var row = ModuleRunRows[i];
                if (nextIds.Contains(row.Id))
                {
                    continue;
                }

                UnwireModuleRunRow(row);
                ModuleRunRows.RemoveAt(i);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyList();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyList);
        }
    }

    private void WireModuleRunRow(ModuleRunRow row)
        => row.PropertyChanged += OnModuleRunRowChanged;

    private void UnwireModuleRunRow(ModuleRunRow row)
        => row.PropertyChanged -= OnModuleRunRowChanged;

    private void UnwireModuleRunRows()
    {
        foreach (var row in ModuleRunRows)
        {
            UnwireModuleRunRow(row);
        }
    }

    private void OnModuleRunRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingFromRuntime || sender is not ModuleRunRow row)
        {
            return;
        }

        if (e.PropertyName == nameof(ModuleRunRow.IsEnabled))
        {
            ObserveDetached(
                SaveModuleEnabledAsync(row.Id, row.IsEnabled),
                "settings.agents.module_enable.detached.fail");
            return;
        }

        if (e.PropertyName == nameof(ModuleRunRow.IsRunningSwitch))
        {
            ObserveDetached(
                ToggleModuleRunningAsync(row.Id, row.IsRunningSwitch),
                "settings.agents.module_run.detached.fail");
        }
    }

    private void WireModuleEditor(ModuleSettingsEditor editor)
    {
        foreach (var section in editor.Sections)
        {
            foreach (var field in section.Fields)
            {
                field.PropertyChanged += OnModuleFieldChanged;
                field.ListItems.CollectionChanged += OnModuleFieldListChanged;
                foreach (var item in field.ListItems)
                {
                    item.PropertyChanged += OnModuleFieldListItemChanged;
                }
            }
        }
    }

    private void UnwireModuleEditors()
    {
        foreach (var editor in ModuleEditors)
        {
            foreach (var section in editor.Sections)
            {
                foreach (var field in section.Fields)
                {
                    field.PropertyChanged -= OnModuleFieldChanged;
                    field.ListItems.CollectionChanged -= OnModuleFieldListChanged;
                    foreach (var item in field.ListItems)
                    {
                        item.PropertyChanged -= OnModuleFieldListItemChanged;
                    }
                }
            }
        }
    }

    private void OnModuleFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressModuleAutoSave || sender is not ModuleSettingsFieldViewModel field)
        {
            RefreshPendingChanges();
            return;
        }

        var isAutoSaveChange = field.IsBool && e.PropertyName == nameof(ModuleSettingsFieldViewModel.BoolValue)
            || field.IsEnum && e.PropertyName == nameof(ModuleSettingsFieldViewModel.SelectedOption);
        if (!isAutoSaveChange)
        {
            RefreshPendingChanges();
            return;
        }

        var editor = ModuleEditors.FirstOrDefault(candidate =>
            candidate.Sections.Any(section => section.Fields.Contains(field)));
        if (editor is not null)
        {
            AcceptModuleFieldValue(editor, field);
            ScheduleModuleFieldSave(editor, field);
        }
    }

    private void AcceptModuleFieldValue(
        ModuleSettingsEditor editor,
        ModuleSettingsFieldViewModel field)
    {
        if (_savedSnapshot is null
            || !_savedSnapshot.ModuleSettingsJson.TryGetValue(editor.ModuleId, out var savedJson))
        {
            RefreshPendingChanges();
            return;
        }

        try
        {
            SetSavedModuleJson(editor.ModuleId, editor.ApplyField(savedJson, field));
        }
        catch
        {
            RefreshPendingChanges();
            return;
        }

        RefreshPendingChanges();
    }

    private void ScheduleModuleFieldSave(
        ModuleSettingsEditor editor,
        ModuleSettingsFieldViewModel field)
    {
        CancellationTokenSource cts;
        lock (_moduleAutoSaveSync)
        {
            if (_moduleAutoSaveCts.Remove(editor.ModuleId, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            if (!_moduleAutoSaveFields.TryGetValue(editor.ModuleId, out var fields))
            {
                fields = [];
                _moduleAutoSaveFields[editor.ModuleId] = fields;
            }

            fields.Add(field);
            cts = new CancellationTokenSource();
            _moduleAutoSaveCts[editor.ModuleId] = cts;
        }

        ObserveDetached(
            SaveModuleFieldsAfterDelayAsync(editor, cts),
            "settings.agents.module_field.autosave.detached.fail");
    }

    private bool HasModuleAutoSaves
    {
        get
        {
            lock (_moduleAutoSaveSync)
            {
                return _moduleAutoSaveCts.Count > 0;
            }
        }
    }

    private async Task SaveModuleFieldsAfterDelayAsync(
        ModuleSettingsEditor editor,
        CancellationTokenSource autoSaveCts)
    {
        var ct = autoSaveCts.Token;
        var gateEntered = false;
        string? savedJson = null;
        var persisted = false;
        ModuleSettingsFieldViewModel[] fields = [];
        try
        {
            await Task.Delay(400, ct).ConfigureAwait(false);
            lock (_moduleAutoSaveSync)
            {
                if (!_moduleAutoSaveCts.TryGetValue(editor.ModuleId, out var current)
                    || !ReferenceEquals(current, autoSaveCts)
                    || !_moduleAutoSaveFields.TryGetValue(editor.ModuleId, out var pendingFields))
                {
                    return;
                }

                fields = [.. pendingFields];
            }

            await _moduleSaveGate.WaitAsync(ct).ConfigureAwait(false);
            gateEntered = true;
            savedJson = _moduleSettings.LoadSettingsJson(editor.ModuleId);
            var nextJson = fields.Aggregate(savedJson, editor.ApplyField);
            if (!SameJson(savedJson, nextJson))
            {
                await _moduleSettings
                    .SaveSettingsJsonAsync(editor.ModuleId, nextJson, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            persisted = true;
            _moduleSaveGate.Release();
            gateEntered = false;
            if (_agentsDisposed || !IsCurrentModuleAutoSave(editor.ModuleId, autoSaveCts))
            {
                return;
            }

            var moduleState = _agents.GetModuleState(editor.ModuleId);
            if (moduleState is AgentsRunState.Running or AgentsRunState.Starting)
            {
                var result = await ExecuteRuntimeCommandAsync(
                        () => _agents.StartModuleAsync(editor.ModuleId, CancellationToken.None))
                    .ConfigureAwait(false);
                if (!result.Ok && IsCurrentModuleAutoSave(editor.ModuleId, autoSaveCts))
                {
                    _toast.Error("自动化集成", $"配置已保存，但模块重载失败：{result.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogError(
                "settings.agents.module_field.autosave.fail",
                "Failed to auto-save module fields",
                ex,
                new { editor.ModuleId, FieldKeys = fields.Select(field => field.Key).ToArray() });
            if (persisted && IsCurrentModuleAutoSave(editor.ModuleId, autoSaveCts))
            {
                _toast.Error("自动化集成", $"配置已保存，但模块重载失败：{ex.Message}");
            }
            else if (IsCurrentModuleAutoSave(editor.ModuleId, autoSaveCts))
            {
                await RestoreModuleFieldsAsync(editor, fields, savedJson, ex.Message).ConfigureAwait(false);
            }
        }
        finally
        {
            if (gateEntered)
            {
                _moduleSaveGate.Release();
            }

            var removed = false;
            lock (_moduleAutoSaveSync)
            {
                if (_moduleAutoSaveCts.TryGetValue(editor.ModuleId, out var current)
                    && ReferenceEquals(current, autoSaveCts))
                {
                    _moduleAutoSaveCts.Remove(editor.ModuleId);
                    _moduleAutoSaveFields.Remove(editor.ModuleId);
                    autoSaveCts.Dispose();
                    removed = true;
                }
            }

            if (removed)
            {
                await RunOnUiAsync(TryFlushStaleModuleEditors);
            }
        }
    }

    private bool IsCurrentModuleAutoSave(string moduleId, CancellationTokenSource autoSaveCts)
    {
        lock (_moduleAutoSaveSync)
        {
            return _moduleAutoSaveCts.TryGetValue(moduleId, out var current)
                   && ReferenceEquals(current, autoSaveCts);
        }
    }

    private void SetSavedModuleJson(string moduleId, string json)
    {
        if (_savedSnapshot is null)
        {
            return;
        }

        var moduleJson = new Dictionary<string, string>(_savedSnapshot.ModuleSettingsJson, StringComparer.Ordinal)
        {
            [moduleId] = json,
        };
        _savedSnapshot = _savedSnapshot with { ModuleSettingsJson = moduleJson };
    }

    private Task RestoreModuleFieldsAsync(
        ModuleSettingsEditor editor,
        IReadOnlyList<ModuleSettingsFieldViewModel> fields,
        string? savedJson,
        string error)
        => RunOnUiAsync(() =>
        {
            try
            {
                var json = savedJson ?? _moduleSettings.LoadSettingsJson(editor.ModuleId);
                _suppressModuleAutoSave = true;
                _suppressPendingRecalc = true;
                SetSavedModuleJson(editor.ModuleId, json);
                foreach (var field in fields)
                {
                    editor.RestoreField(json, field);
                }
            }
            catch (Exception restoreEx)
            {
                LogError(
                    "settings.agents.module_field.restore.fail",
                    "Failed to restore module fields after auto-save failure",
                    restoreEx,
                    new { editor.ModuleId, FieldKeys = fields.Select(field => field.Key).ToArray() });
            }
            finally
            {
                _suppressModuleAutoSave = false;
                _suppressPendingRecalc = false;
                RefreshPendingChanges();
            }

            _toast.Error("自动化集成", $"自动保存失败：{error}");
        });

    private void CancelModuleAutoSaves()
    {
        lock (_moduleAutoSaveSync)
        {
            foreach (var cts in _moduleAutoSaveCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _moduleAutoSaveCts.Clear();
            _moduleAutoSaveFields.Clear();
        }
    }

    private void FlushModuleAutoSaves()
    {
        _moduleSaveGate.Wait();
        try
        {
            List<(ModuleSettingsEditor Editor, ModuleSettingsFieldViewModel[] Fields)> pending = [];
            lock (_moduleAutoSaveSync)
            {
                foreach (var (moduleId, fields) in _moduleAutoSaveFields)
                {
                    var editor = ModuleEditors.FirstOrDefault(candidate =>
                        string.Equals(candidate.ModuleId, moduleId, StringComparison.Ordinal));
                    if (editor is not null && fields.Count > 0)
                    {
                        pending.Add((editor, [.. fields]));
                    }
                }

                foreach (var cts in _moduleAutoSaveCts.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                _moduleAutoSaveCts.Clear();
                _moduleAutoSaveFields.Clear();
            }

            foreach (var (editor, fields) in pending)
            {
                try
                {
                    var savedJson = _moduleSettings.LoadSettingsJson(editor.ModuleId);
                    var nextJson = fields.Aggregate(savedJson, editor.ApplyField);
                    if (!SameJson(savedJson, nextJson))
                    {
                        _moduleSettings
                            .SaveSettingsJsonAsync(editor.ModuleId, nextJson, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                    }
                }
                catch (Exception ex)
                {
                    LogError(
                        "settings.agents.module_field.flush_fail",
                        "Failed to flush module fields while disposing settings",
                        ex,
                        new { editor.ModuleId, FieldKeys = fields.Select(field => field.Key).ToArray() });
                }
            }
        }
        finally
        {
            _moduleSaveGate.Release();
        }
    }

    private void OnModuleFieldListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is SettingsLineItem line)
                {
                    line.PropertyChanged -= OnModuleFieldListItemChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is SettingsLineItem line)
                {
                    line.PropertyChanged += OnModuleFieldListItemChanged;
                }
            }
        }

        RefreshPendingChanges();
    }

    private void OnModuleFieldListItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsLineItem.Value) or null or "")
        {
            RefreshPendingChanges();
        }
    }

    private void RefreshPendingChanges()
    {
        void Apply()
        {
            if (_suppressPendingRecalc)
            {
                return;
            }

            if (!_baselineReady || _savedSnapshot is null)
            {
                if (_hasPendingChanges)
                {
                    _hasPendingChanges = false;
                    OnPropertyChanged(nameof(HasPendingChanges));
                    RefreshUnsaved();
                }

                return;
            }

            var current = BuildCurrentSnapshot();
            var pending = current is null || !SnapshotEquals(_savedSnapshot, current);
            if (_hasPendingChanges != pending)
            {
                _hasPendingChanges = pending;
                OnPropertyChanged(nameof(HasPendingChanges));
            }

            RefreshUnsaved();

            if (!pending)
            {
                TryFlushStaleModuleEditors();
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return;
        }

        Dispatcher.UIThread.Post(Apply);
    }

    private AgentsEditorSnapshot? BuildCurrentSnapshot()
    {
        try
        {
            var moduleJson = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var editor in ModuleEditors)
            {
                moduleJson[editor.ModuleId] = editor.ToJsonString();
            }

            return new AgentsEditorSnapshot(
                AgentsExecutablePath.Trim(),
                AgentsProcessName.Trim(),
                moduleJson);
        }
        catch
        {
            return null;
        }
    }

    private bool IsAgentsHostDirty()
    {
        if (!_baselineReady || _savedSnapshot is null)
        {
            return false;
        }

        return !string.Equals(
                   AgentsExecutablePath.Trim(),
                   _savedSnapshot.AgentsExecutablePath,
                   StringComparison.Ordinal)
               || !string.Equals(
                   AgentsProcessName.Trim(),
                   _savedSnapshot.AgentsProcessName,
                   StringComparison.Ordinal);
    }

    private bool IsModuleSettingsDirty()
    {
        if (!_baselineReady || _savedSnapshot is null)
        {
            return false;
        }

        var current = BuildCurrentSnapshot();
        return current is null || !ModuleSettingsEqual(_savedSnapshot, current);
    }

    private static bool ModuleSettingsEqual(AgentsEditorSnapshot left, AgentsEditorSnapshot right)
    {
        if (left.ModuleSettingsJson.Count != right.ModuleSettingsJson.Count)
        {
            return false;
        }

        foreach (var (moduleId, json) in left.ModuleSettingsJson)
        {
            if (!right.ModuleSettingsJson.TryGetValue(moduleId, out var otherJson)
                || !SameJson(json, otherJson))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SnapshotEquals(AgentsEditorSnapshot left, AgentsEditorSnapshot right)
    {
        if (!string.Equals(left.AgentsExecutablePath, right.AgentsExecutablePath, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.AgentsProcessName, right.AgentsProcessName, StringComparison.Ordinal))
        {
            return false;
        }

        return ModuleSettingsEqual(left, right);
    }

    private void DisposeAgents()
    {
        _agentsDisposed = true;
        FlushModuleAutoSaves();
        try { _agents.StatusChanged -= OnAgentsRuntimeChanged; }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.runtime_unsub_fail", "Failed to unsubscribe runtime status", ex);
        }

        try { UnwireModuleRunRows(); }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.module_run_unsub_fail", "Failed to unsubscribe module run rows", ex);
        }

        try { UnwireModuleEditors(); }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.module_editors_unsub_fail", "Failed to unsubscribe module editors", ex);
        }
    }

    private sealed record AgentsEditorSnapshot(
        string AgentsExecutablePath,
        string AgentsProcessName,
        IReadOnlyDictionary<string, string> ModuleSettingsJson);
}
