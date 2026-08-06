using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>标题胶囊 + 底栏 chrome：DB 探测展示、Agents 控制、状态入口</summary>
public partial class MainWindowViewModel
{
    private static readonly TimeSpan ChromeActionDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    private readonly Dictionary<string, ModuleChrome> _moduleChromeById = new(StringComparer.Ordinal);
    private DateTimeOffset _lastAgentsTopToastAt = DateTimeOffset.MinValue;
    private bool _syncingHostMenu;

    private IAgentsRuntime Agents => _agentsManager.GetRequired(AgentsIds.Agents);

    public ObservableCollection<ModuleChrome> TopStatusPills { get; } = new();

    /// <summary>底栏 Agents 菜单的模块列表（desktop.order）</summary>
    public ObservableCollection<ModuleChrome> AgentsMenuModules { get; } = new();

    public event Action? AgentsChromeUpdated;

    [ObservableProperty] private bool _isAgentsActionRunning;
    [ObservableProperty] private bool _isTopModulesExpanded;
    [ObservableProperty] private bool _isHostMenuChecked;

    public RuntimeVisualState DbVisualState
        => IsDbProbeRunning
            ? RuntimeVisualState.Transitioning
            : IsDbConnected
                ? RuntimeVisualState.Active
                : RuntimeVisualState.Inactive;

    /// <summary>标题栏/底栏 ToolTip（连接态文案）</summary>
    public string DbItemText
        => IsDbProbeRunning ? "检测中…"
        : IsDbConnected ? "已连接"
        : "未连接";

    public bool IsDbStatusConnected => !IsDbProbeRunning && IsDbConnected;

    public bool IsDbStatusDisconnected => !IsDbProbeRunning && !IsDbConnected;

    private bool CanProbeDb() => !IsDbProbeRunning;

    public bool CanControlAgents
        => !IsAgentsActionRunning && Agents.HostState != AgentsRunState.Starting;

    /// <summary>底栏菜单模块 Running/Starting 数（AgentsMenuModules.IsRunning）</summary>
    private int RunningBottomModuleCount
        => AgentsMenuModules.Count(m => m.IsRunning);

    /// <summary>底栏运行模块数图标（0→CircleSlash2，1–6→Dice，≥7→Dices）</summary>
    public string RunningModuleCountIcon
        => MapCountIcon(RunningBottomModuleCount);

    /// <summary>底栏/标题 Host tip：主机态 + 底栏模块数</summary>
    public string AgentsBarTip
    {
        get
        {
            var host = Agents.HostState switch
            {
                AgentsRunState.Running => "Agents 运行中",
                AgentsRunState.Starting => "Agents 启动中",
                _ => "Agents 未启动",
            };
            var n = RunningBottomModuleCount;
            var modules = n == 0 ? "无模块运行" : $"{n} 模块运行中";
            return $"{host} · {modules}";
        }
    }

    public string TopModulesExpandIcon => IsTopModulesExpanded ? "ChevronLeft" : "ChevronRight";

    public string TopModulesExpandTip => IsTopModulesExpanded ? "收起模块" : "展开模块";

    public bool HasTopModules => TopStatusPills.Count > 0;

    public RuntimeVisualState HostVisualState
        => Agents.HostState switch
        {
            AgentsRunState.Running => RuntimeVisualState.Active,
            AgentsRunState.Starting => RuntimeVisualState.Transitioning,
            _ => RuntimeVisualState.Inactive,
        };

    public bool ShowAccessGuardItem => _accessGuard.IsBlocked;

    public string AccessGuardItemText
        => string.IsNullOrWhiteSpace(_accessGuard.BlockReason)
            ? "配置未完成"
            : _accessGuard.BlockReason!;

    public bool IsSettingsPageActive => ActivePage is ISettingsPage;

    private void AttachChromeHooks()
    {
        Agents.StatusChanged += OnAgentsStatusChanged;
        RaiseAgentsStateChanged();
    }

    private void DetachChromeHooks()
        => Agents.StatusChanged -= OnAgentsStatusChanged;

    partial void OnIsDbProbeRunningChanged(bool value)
    {
        TryReconnectDbCommand.NotifyCanExecuteChanged();
        // 统一走 RaiseDbStateChanged，避免 probe 与 dedupe 字段双写
        RaiseDbStateChanged();
    }

    partial void OnIsAgentsActionRunningChanged(bool value)
    {
        NotifyAgentsCommands();
        OnPropertyChanged(nameof(CanControlAgents));
    }

    partial void OnIsTopModulesExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(TopModulesExpandIcon));
        OnPropertyChanged(nameof(TopModulesExpandTip));
    }

    partial void OnIsHostMenuCheckedChanged(bool value)
    {
        if (_syncingHostMenu)
        {
            return;
        }

        ObserveDetached(SetHostRunningAsync(value), "agents.host_menu_toggle.detached.fail");
    }

    [RelayCommand]
    private void ToggleTopModulesExpanded()
        => IsTopModulesExpanded = !IsTopModulesExpanded;

    [RelayCommand]
    private void OpenAgentsSettings()
        => NavigateSettingsTab("main.nav.settings.agents", static s => s.OpenAgentsTab());

    [RelayCommand]
    private void OpenDatabaseSettings()
        => NavigateSettingsTab("main.nav.settings.database", static s => s.OpenDatabaseTab());

    [RelayCommand]
    private void OpenModuleSettings(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return;
        }

        NavigateSettingsTab(
            $"main.nav.settings.module:{moduleId}",
            s => s.OpenModuleSettingsTab(moduleId));
    }

    [RelayCommand(CanExecute = nameof(CanProbeDb))]
    private async Task TryReconnectDb()
    {
        if (SkipTrigger("main.db.probe", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        _dbMonitor.Start();

        await RunOnUiAsync(() => IsDbProbeRunning = true);

        var kind = _dbMonitor.IsConnected
            ? DbProbeKind.HealthCheck
            : DbProbeKind.Reconnect;

        DbProbeReport report;

        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            report = await _dbMonitor.ProbeAsync(kind, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("MainWindowVM", "db.probe.timeout", "Database probe timed out");
            _toasts.Error("数据库", "操作超时：请检查网络/配置");
            return;
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "db.probe.error", "Database probe failed", ex);
            _toasts.Error("数据库", ex.Message);
            return;
        }
        finally
        {
            // known 必须先于 probe 落盘：RaiseDb 从 OnIsDbProbeRunningChanged 发出时带上 known
            await RunOnUiAsync(() =>
            {
                MarkDbConnectivityKnown();
                IsDbProbeRunning = false;
            });
        }

        if (report.Success)
        {
            _toasts.Success("数据库", kind == DbProbeKind.HealthCheck ? "健康检查通过" : "重连成功");
        }
        else
        {
            _logger.Warn("MainWindowVM", "db.probe.unsuccessful", "Database probe finished with unsuccessful result", null, new
            {
                kind,
                report.Reason
            });
            _toasts.Error("数据库", report.Reason ?? "连接失败");
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlAgents))]
    private async Task StartOrRestartHost()
    {
        if (SkipTrigger("main.agents.host", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        try
        {
            if (Agents.IsHostRunning)
            {
                IsAgentsActionRunning = true;

                Agents.Reload();
                if (Agents.IsHostRunning)
                {
                    // Host 不依赖 DB：健康检查只看进程
                    TryShowAgentsTopToast(() => _toasts.Success("Agents", "健康检查通过：Host 进程运行中"));
                }
                else
                {
                    TryShowAgentsTopToast(() => _toasts.Error("Agents", "健康检查失败：未检测到 Host 进程"));
                }
            }
            else
            {
                await RunAgentsCommandAsync(() => Agents.StartOrRestartAsync()).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.host_top_action.error", "Agents Host top action failed", ex);
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                RaiseAgentsStateChanged();
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlAgents))]
    private async Task StartOrRestartModule(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return;
        }

        if (SkipTrigger($"main.agents.module:{moduleId}", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        try
        {
            var moduleLabel = Agents.Modules
                .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
                ?.DisplayName;
            if (string.IsNullOrWhiteSpace(moduleLabel))
            {
                moduleLabel = moduleId;
            }

            if (!Agents.IsModuleEnabled(moduleId))
            {
                TryShowAgentsTopToast(() => _toasts.Error("Agents", $"{moduleLabel} 未启用"));
                return;
            }

            // Running：健康检查；Starting：不重入启停；否则走 StartModuleAsync
            var moduleState = Agents.GetModuleState(moduleId);
            if (moduleState is AgentsRunState.Running or AgentsRunState.Starting)
            {
                if (moduleState == AgentsRunState.Starting)
                {
                    return;
                }

                IsAgentsActionRunning = true;

                Agents.Reload();
                if (Agents.GetModuleState(moduleId) != AgentsRunState.Running)
                {
                    TryShowAgentsTopToast(() => _toasts.Error("Agents", $"健康检查失败：{moduleLabel} 未运行"));
                }
                else
                {
                    TryShowAgentsTopToast(() => _toasts.Success("Agents", $"健康检查通过：{moduleLabel} 已就绪"));
                }

                return;
            }

            await RunAgentsCommandAsync(() => Agents.StartModuleAsync(moduleId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.module_top_action.error", "Agents module top action failed", ex, new { moduleId });
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                RaiseAgentsStateChanged();
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlAgents))]
    private async Task StopHost()
    {
        if (SkipTrigger("main.agents.host_stop", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        if (!Agents.IsHostRunning)
        {
            return;
        }

        try
        {
            await RunAgentsCommandAsync(() => Agents.StopAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.host_stop.error", "Agents Host stop failed", ex);
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                RaiseAgentsStateChanged();
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlAgents))]
    private async Task StopModule(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return;
        }

        if (SkipTrigger($"main.agents.module_stop:{moduleId}", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        if (Agents.GetModuleState(moduleId) is not (AgentsRunState.Running or AgentsRunState.Starting))
        {
            return;
        }

        try
        {
            await RunAgentsCommandAsync(() => Agents.StopModuleAsync(moduleId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.module_stop.error", "Agents module stop failed", ex, new { moduleId });
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                RaiseAgentsStateChanged();
            });
        }
    }

    /// <summary>底栏模块 Switch：开=启动模块，关=停止模块</summary>
    public async Task SetModuleRunningAsync(string moduleId, bool wantRunning)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || !CanControlAgents)
        {
            RaiseAgentsStateChanged();
            return;
        }

        var running = Agents.GetModuleState(moduleId) is AgentsRunState.Running or AgentsRunState.Starting;
        if (wantRunning == running)
        {
            return;
        }

        if (SkipTrigger($"main.agents.module_toggle:{moduleId}", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            RaiseAgentsStateChanged();
            return;
        }

        try
        {
            if (wantRunning)
            {
                if (!Agents.IsModuleEnabled(moduleId))
                {
                    var label = Agents.Modules
                        .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
                        ?.DisplayName ?? moduleId;
                    TryShowAgentsTopToast(() => _toasts.Error("Agents", $"{label} 未启用"));
                    return;
                }

                await RunAgentsCommandAsync(() => Agents.StartModuleAsync(moduleId)).ConfigureAwait(false);
            }
            else
            {
                await RunAgentsCommandAsync(() => Agents.StopModuleAsync(moduleId)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.module_menu_toggle.error", "Agents module menu toggle failed", ex, new { moduleId });
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                RaiseAgentsStateChanged();
            });
        }
    }

    private async Task SetHostRunningAsync(bool wantRunning)
    {
        if (!CanControlAgents)
        {
            RaiseAgentsStateChanged();
            return;
        }

        if (wantRunning == IsHostMenuActive)
        {
            return;
        }

        if (SkipTrigger("main.agents.host_toggle", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            RaiseAgentsStateChanged();
            return;
        }

        try
        {
            if (wantRunning)
            {
                await RunAgentsCommandAsync(() => Agents.StartOrRestartAsync()).ConfigureAwait(false);
            }
            else
            {
                await RunAgentsCommandAsync(() => Agents.StopAsync()).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.host_menu_toggle.error", "Agents Host menu toggle failed", ex);
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                RaiseAgentsStateChanged();
            });
        }
    }

    private async Task StartAgentsOnStartupAsync()
    {
        if (Agents.IsHostRunning)
        {
            return;
        }

        try
        {
            var result = await Agents.StartOrRestartAsync().ConfigureAwait(false);
            if (!result.Ok && !result.SuppressToast)
            {
                _logger.Warn(
                    "MainWindowVM",
                    "agents.startup_autostart.fail",
                    "Failed to auto-start Agents host on startup",
                    null,
                    new { result.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "agents.startup_autostart.exception", "Startup auto-start threw exception", ex);
        }
        finally
        {
            PostOnUi(RaiseAgentsStateChanged);
        }
    }

    private void RaiseAgentsStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        SyncHostMenuChecked();
        SyncModuleChrome();
        OnPropertyChanged(nameof(HostVisualState));
        OnPropertyChanged(nameof(RunningModuleCountIcon));
        OnPropertyChanged(nameof(AgentsBarTip));
        OnPropertyChanged(nameof(CanControlAgents));
        NotifyAgentsCommands();
        AgentsChromeUpdated?.Invoke();
    }

    private static string MapCountIcon(int count)
        => count switch
        {
            <= 0 => "CircleSlash2",
            1 => "Dice1",
            2 => "Dice2",
            3 => "Dice3",
            4 => "Dice4",
            5 => "Dice5",
            6 => "Dice6",
            _ => "Dices"
        };

    private bool IsHostMenuActive
        => Agents.IsHostRunning || Agents.HostState == AgentsRunState.Starting;

    private void SyncHostMenuChecked()
    {
        _syncingHostMenu = true;
        try
        {
            IsHostMenuChecked = IsHostMenuActive;
        }
        finally
        {
            _syncingHostMenu = false;
        }
    }

    private void SyncModuleChrome()
    {
        // 仅投影 desktop 声明的顶栏/底栏入口，其它模块不进 chrome
        var scanned = Agents.Modules
            .Where(m => m.Desktop.TopStatusPills || m.Desktop.BottomStatusBar)
            .OrderBy(m => m.Desktop.Order)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var staleId in _moduleChromeById.Keys.Except(scanned.Select(m => m.Id), StringComparer.Ordinal).ToList())
        {
            _moduleChromeById.Remove(staleId);
        }

        foreach (var module in scanned)
        {
            if (!_moduleChromeById.TryGetValue(module.Id, out var chrome))
            {
                chrome = new ModuleChrome(module);
                _moduleChromeById[module.Id] = chrome;
            }
            else
            {
                chrome.ApplyDescriptor(module);
            }

            chrome.Apply(Agents.GetModuleState(module.Id));
        }

        ReplaceModuleChrome(TopStatusPills, scanned.Where(m => m.Desktop.TopStatusPills));
        ReplaceModuleChrome(AgentsMenuModules, scanned.Where(m => m.Desktop.BottomStatusBar));
        OnPropertyChanged(nameof(HasTopModules));
        if (!HasTopModules && IsTopModulesExpanded)
        {
            IsTopModulesExpanded = false;
        }
    }

    private void ReplaceModuleChrome(
        ObservableCollection<ModuleChrome> target,
        IEnumerable<ModuleDescriptor> modules)
    {
        var next = modules
            .Select(m => _moduleChromeById[m.Id])
            .ToList();

        if (target.Count == next.Count &&
            target.Zip(next, (a, b) => ReferenceEquals(a, b)).All(same => same))
        {
            return;
        }

        for (var index = 0; index < next.Count; index++)
        {
            var item = next[index];
            if (index < target.Count && ReferenceEquals(target[index], item))
            {
                continue;
            }

            var currentIndex = target.IndexOf(item);
            if (currentIndex >= 0)
            {
                target.Move(currentIndex, index);
            }
            else
            {
                target.Insert(index, item);
            }
        }

        while (target.Count > next.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private async Task RunAgentsCommandAsync(Func<Task<AgentsCommandResult>> run)
    {
        await RunOnUiAsync(() => IsAgentsActionRunning = true);

        var result = await run().ConfigureAwait(false);
        if (result.SuppressToast)
        {
            return;
        }

        if (result.Ok)
        {
            TryShowAgentsTopToast(() => _toasts.Success("Agents", result.Message));
        }
        else
        {
            TryShowAgentsTopToast(() => _toasts.Error("Agents", result.Message));
        }
    }

    private void NotifyAgentsCommands()
    {
        StartOrRestartHostCommand.NotifyCanExecuteChanged();
        StartOrRestartModuleCommand.NotifyCanExecuteChanged();
        StopHostCommand.NotifyCanExecuteChanged();
        StopModuleCommand.NotifyCanExecuteChanged();
    }

    private void TryShowAgentsTopToast(Action show)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAgentsTopToastAt < ChromeActionDebounce)
        {
            return;
        }

        _lastAgentsTopToastAt = now;
        show();
    }

    private void OnAgentsStatusChanged()
        => PostOnUi(RaiseAgentsStateChanged);
}
