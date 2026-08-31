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
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>标题胶囊与底栏 chrome：PacAPI 可用性、Agents 控制、状态入口</summary>
public partial class MainWindowViewModel
{
    private static readonly TimeSpan ChromeActionDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    private readonly Dictionary<string, ModuleChrome> _moduleChromeById = new(StringComparer.Ordinal);
    private DateTimeOffset _lastAgentsTopToastAt = DateTimeOffset.MinValue;
    private string? _agentsChromeKey;

    private IAgentsRuntime Agents => _agentsManager.GetRequired(AgentsIds.Agents);

    public ObservableCollection<ModuleChrome> TopStatusPills { get; } = new();

    public ObservableCollection<ModuleChrome> AgentsMenuModules { get; } = new();

    public event Action? AgentsChromeUpdated;

    [ObservableProperty] private bool _isAgentsActionRunning;
    [ObservableProperty] private bool _isTopModulesExpanded = true;
    [ObservableProperty] private bool _isApiProbeRunning;

    public bool IsHostMenuChecked => Agents.IsHostRunning;

    public RuntimeVisualState ApiVisualState
    {
        get
        {
            if (!_apiAvailability.IsConfigured)
            {
                return RuntimeVisualState.Inactive;
            }

            if (IsApiProbeRunning || !_apiAvailability.Current.FirstCheckCompleted)
            {
                return RuntimeVisualState.Transitioning;
            }

            return ConnectionView.From(_apiAvailability.Current, isConfigured: true).Kind switch
            {
                ConnectionKind.Up => RuntimeVisualState.Active,
                ConnectionKind.Unknown => RuntimeVisualState.Transitioning,
                ConnectionKind.NotConfigured => RuntimeVisualState.Inactive,
                _ => RuntimeVisualState.Inactive,
            };
        }
    }

    public string ApiItemText
    {
        get
        {
            if (IsApiProbeRunning)
            {
                return "检测中…";
            }

            // 未配置文案
            if (!_apiAvailability.IsConfigured)
            {
                return "PacAPI 服务未配置";
            }

            var snap = _apiAvailability.Current;
            if (!snap.FirstCheckCompleted)
            {
                return "PacAPI 服务检查中…";
            }

            var view = ConnectionView.From(snap, isConfigured: true);
            return view.Kind switch
            {
                ConnectionKind.Up => "PacAPI 服务已连接",
                ConnectionKind.NotConfigured => "PacAPI 服务未配置",
                ConnectionKind.Blocked => view.Title,
                ConnectionKind.Down => string.IsNullOrEmpty(view.Title) ? "PacAPI 服务不可用" : view.Title,
                _ => "PacAPI 服务检查中…",
            };
        }
    }

    public bool IsApiStatusReady
        => !IsApiProbeRunning
           && ConnectionView.IsReady(_apiAvailability.Current, _apiAvailability.IsConfigured);

    public bool IsApiStatusNotConfigured
        => !IsApiProbeRunning && !_apiAvailability.IsConfigured;

    public bool IsApiStatusDown
    {
        get
        {
            if (IsApiProbeRunning || !_apiAvailability.IsConfigured)
            {
                return false;
            }

            if (!_apiAvailability.Current.FirstCheckCompleted)
            {
                return false;
            }

            return ConnectionView.From(_apiAvailability.Current, isConfigured: true).Kind
                is ConnectionKind.Down or ConnectionKind.Blocked;
        }
    }

    private bool CanProbeApi() => !IsApiProbeRunning;

    public bool CanControlAgents
        => !IsAgentsActionRunning && Agents.HostState != AgentsRunState.Starting;

    private int RunningBottomModuleCount
        => AgentsMenuModules.Count(m => m.IsRunning);

    public string RunningModuleCountIcon
        => $"rosette-number-{Math.Clamp(RunningBottomModuleCount, 0, 9)}";

    public string AgentsBarTip
    {
        get
        {
            var host = Agents.HostState switch
            {
                AgentsRunState.Running => "Agents 运行中",
                AgentsRunState.Starting => "Agents 启动中",
                AgentsRunState.Failed => "Agents 启动失败",
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
            AgentsRunState.Failed => RuntimeVisualState.Inactive,
            _ => RuntimeVisualState.Inactive,
        };

    public bool ShowServiceBlockItem
        => ConnectionView.IsBlocked(_apiAvailability.Current, _apiAvailability.IsConfigured);

    public string ServiceBlockItemText
    {
        get
        {
            var view = ConnectionView.From(
                _apiAvailability.Current,
                isConfigured: _apiAvailability.IsConfigured);
            return string.IsNullOrEmpty(view.Title) ? "PacAPI 服务不可用" : view.Title;
        }
    }

    public bool IsSettingsPageActive => ActivePage is ISettingsPage;

    private void AttachChromeHooks()
    {
        Agents.StatusChanged += OnAgentsStatusChanged;
        RaiseAgentsStateChanged();
    }

    private void DetachChromeHooks()
        => Agents.StatusChanged -= OnAgentsStatusChanged;

    partial void OnIsApiProbeRunningChanged(bool value)
    {
        TryProbeApiCommand.NotifyCanExecuteChanged();
        RaiseApiChromeChanged();
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

    private void RaiseApiChromeChanged()
    {
        OnPropertyChanged(nameof(ApiVisualState));
        OnPropertyChanged(nameof(ApiItemText));
        OnPropertyChanged(nameof(IsApiStatusReady));
        OnPropertyChanged(nameof(IsApiStatusNotConfigured));
        OnPropertyChanged(nameof(IsApiStatusDown));
        OnPropertyChanged(nameof(ShowServiceBlockItem));
        OnPropertyChanged(nameof(ServiceBlockItemText));
    }

    [RelayCommand]
    private void ToggleTopModulesExpanded()
        => IsTopModulesExpanded = !IsTopModulesExpanded;

    [RelayCommand]
    private void OpenAgentsSettings()
        => NavigateSettingsTab("main.nav.settings.agents", static s => s.OpenAgentsTab());

    [RelayCommand]
    private void OpenConnectivitySettings()
        => NavigateSettingsTab("main.nav.settings.connection", static s => s.OpenConnectionTab());

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

    [RelayCommand(CanExecute = nameof(CanProbeApi))]
    private async Task TryProbeApi()
    {
        if (SkipTrigger("main.api.probe", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        await RunOnUiAsync(() => IsApiProbeRunning = true);

        try
        {
            if (!_apiAvailability.IsConfigured)
            {
                _toasts.Error("PacAPI 服务", "请前往设置配置地址与密钥并测试连接");
                return;
            }

            using var cts = new CancellationTokenSource(ProbeTimeout);
            await _apiAvailability.ProbeAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("MainWindowVM", "api.probe.timeout", "API availability probe timed out");
            _toasts.Error("PacAPI 服务", "操作超时：请检查网络与配置");
            return;
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "api.probe.error", "API availability probe failed", ex);
            _toasts.Error("PacAPI 服务", "PacAPI 服务不可用");
            return;
        }
        finally
        {
            await RunOnUiAsync(() => IsApiProbeRunning = false);
        }

        var snap = _apiAvailability.Current;
        if (ConnectionView.IsReady(snap, isConfigured: true))
        {
            _toasts.Success("PacAPI 服务", "PacAPI 服务可用");
            return;
        }

        var view = ConnectionView.From(snap, isConfigured: true);
        var detail = !string.IsNullOrWhiteSpace(view.Message)
            ? view.Message
            : snap.Detail ?? "PacAPI 服务不可用";
        _toasts.Error("PacAPI 服务", detail);
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
                if (!Agents.IsHostRunning)
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
    private async Task RestartHost()
    {
        if (SkipTrigger("main.agents.host_restart", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        if (!Agents.IsHostRunning)
        {
            return;
        }

        try
        {
            await RunAgentsCommandAsync(() => Agents.StartOrRestartAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.host_restart.error", "Agents Host restart failed", ex);
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
    private async Task RestartModule(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return;
        }

        if (SkipTrigger($"main.agents.module_restart:{moduleId}", (int)ChromeActionDebounce.TotalMilliseconds))
        {
            return;
        }

        if (!Agents.IsModuleEnabled(moduleId))
        {
            var label = Agents.Modules
                .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
                ?.DisplayName ?? moduleId;
            TryShowAgentsTopToast(() => _toasts.Error("Agents", $"{label} 未启用"));
            return;
        }

        if (Agents.GetModuleState(moduleId) is not (AgentsRunState.Running or AgentsRunState.Starting))
        {
            return;
        }

        try
        {
            await RunAgentsCommandAsync(() => Agents.StartModuleAsync(moduleId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.module_restart.error", "Agents module restart failed", ex, new { moduleId });
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

        var running = Agents.GetModuleState(moduleId) == AgentsRunState.Running;
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

    /// <summary>底栏 Host 状态开关（仅 Running 为开）</summary>
    public async Task SetHostRunningAsync(bool wantRunning)
    {
        if (!CanControlAgents)
        {
            RaiseAgentsStateChanged();
            return;
        }

        // 开关只表示已 Running（与 Settings 一致）；Starting/Failed 不算开
        if (wantRunning == Agents.IsHostRunning)
        {
            RaiseAgentsStateChanged();
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
                // 失败/取消后按 Runtime 校准，避免 Toggle 卡在用户点选的「开」
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

        var key = BuildAgentsChromeKey(Agents.HostState, Agents.Modules, Agents.GetModuleState);
        if (string.Equals(key, _agentsChromeKey, StringComparison.Ordinal))
        {
            return;
        }

        _agentsChromeKey = key;
        SyncModuleChrome();
        OnPropertyChanged(nameof(IsHostMenuChecked));
        OnPropertyChanged(nameof(HostVisualState));
        OnPropertyChanged(nameof(RunningModuleCountIcon));
        OnPropertyChanged(nameof(AgentsBarTip));
        OnPropertyChanged(nameof(CanControlAgents));
        NotifyAgentsCommands();
        AgentsChromeUpdated?.Invoke();
    }

    internal static string BuildAgentsChromeKey(
        AgentsRunState host,
        IEnumerable<ModuleDescriptor> modules,
        Func<string, AgentsRunState> moduleState)
    {
        var listed = modules
            .Where(m => m.Desktop.TopStatusPills || m.Desktop.BottomStatusBar)
            .OrderBy(m => m.Desktop.Order)
            .ThenBy(m => m.Id, StringComparer.Ordinal);
        var parts = listed.Select(m =>
            string.Join(
                '\t',
                m.Id,
                m.DisplayName,
                m.Desktop.Icons.Active,
                m.Desktop.Icons.Inactive,
                m.Desktop.TopStatusPills ? "1" : "0",
                m.Desktop.BottomStatusBar ? "1" : "0",
                moduleState(m.Id).ToString()));
        return string.Join('\n', new[] { host.ToString() }.Concat(parts));
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
        if (result.SuppressToast || result.Ok)
        {
            return;
        }

        TryShowAgentsTopToast(() => _toasts.Error("Agents", result.Message));
    }

    private void NotifyAgentsCommands()
    {
        StartOrRestartHostCommand.NotifyCanExecuteChanged();
        StartOrRestartModuleCommand.NotifyCanExecuteChanged();
        RestartHostCommand.NotifyCanExecuteChanged();
        RestartModuleCommand.NotifyCanExecuteChanged();
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
