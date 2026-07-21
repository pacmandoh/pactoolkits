using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    private readonly record struct SaveOptionsResult(bool Saved, bool Changed);

    private readonly IAgentsRuntime _agents;
    private readonly IAgentsConfigService _agentsConfig;
    private readonly object _snapshotGate = new();
    private AgentsEditorSnapshot? _savedSnapshot;
    private bool _hasPendingChanges;
    private bool _baselineReady;
    private bool _suppressPendingRecalc;

    private bool _syncingFromRuntime;

    [ObservableProperty] private bool _isInjectorEnabled;
    [ObservableProperty] private bool _isHostRunningSwitch;
    [ObservableProperty] private bool _isInjectorRunningSwitch;
    [ObservableProperty] private bool _isAgentsToggling;
    [ObservableProperty] private bool _isSavingSettings;
    [ObservableProperty] private string _agentsExecutablePath = string.Empty;
    [ObservableProperty] private string _agentsProcessName = string.Empty;
    [ObservableProperty] private string _hostStatusText = "检测中";
    [ObservableProperty] private string _hostStatusDetail = "等待 Agents 状态刷新";
    [ObservableProperty] private string _injectorStatusText = "检测中";
    [ObservableProperty] private string _injectorStatusDetail = "等待模块状态刷新";
    [ObservableProperty] private string _hostVersionText = "未知";
    [ObservableProperty] private string _injectorVersionText = "未知";
    [ObservableProperty] private string _hostLastLaunchText = "-";
    [ObservableProperty] private string _injectorLastLaunchText = "-";
    [ObservableProperty] private string _hostLastErrorText = "-";
    [ObservableProperty] private string _injectorLastErrorText = "-";
    [ObservableProperty] private string _injectorPgDriver = "PostgreSQL Unicode(x64)";
    [ObservableProperty] private string _injectorPgSsl = "disable";
    [ObservableProperty] private string _injectorOptWindowClass = "TFrm_mzcffy";
    [ObservableProperty] private string _injectorIptWindowClass = "Tfrm_wzzsm";
    [ObservableProperty] private int _injectorConfirmTimeoutMs = 2500;
    [ObservableProperty] private string _injectorOptParseGridClassNN = "TcxGridSite2";
    [ObservableProperty] private string _injectorOptVerifyGridClassNN = "TcxGridSite2";
    [ObservableProperty] private string _injectorIptParseGridClassNN = "TcxGridSite2";
    [ObservableProperty] private string _injectorIptVerifyGridClassNN = "TcxGridSite1";
    [ObservableProperty] private string _injectorOptInputClassNN = "TMemo2";
    [ObservableProperty] private string _injectorIptInputClassNN = "TEdit1";
    [ObservableProperty] private bool _injectorWarehouseEnabled;
    [ObservableProperty] private string _injectorCodePickPolicy = "MAX_LEVEL";
    [ObservableProperty] private string _injectorWarehouseTaskIdentifier = "单据号||当前编号";
    [ObservableProperty] private CodePickPolicyOption? _selectedInjectorCodePickPolicyOption;
    public ObservableCollection<InjectorLineItem> InjectorAppWinItems { get; } = [];
    public ObservableCollection<InjectorLineItem> InjectorColSpecsItems { get; } = [];
    public ObservableCollection<InjectorLineItem> InjectorIntColsItems { get; } = [];
    public ObservableCollection<InjectorLineItem> InjectorWarehouseAnchorItems { get; } = [];
    public IReadOnlyList<CodePickPolicyOption> InjectorCodePickPolicyOptions { get; } =
    [
        new() { Value = "MAX_LEVEL", Label = "按最大码" },
        new() { Value = "MIN_LEVEL", Label = "按最小码" },
    ];

    public bool IsHostStatusRunning => _agents.HostState == AgentsRunState.Running;
    public bool IsHostStatusStarting => _agents.HostState == AgentsRunState.Starting;
    public bool IsHostStatusFailed => _agents.HostState == AgentsRunState.Failed;
    public bool IsHostStatusStopped => _agents.HostState == AgentsRunState.Stopped;
    public bool IsHostStatusUnknown => _agents.HostState == AgentsRunState.Unknown;

    public bool IsInjectorStatusRunning => _agents.InjectorState == AgentsRunState.Running;
    public bool IsInjectorStatusStarting => _agents.InjectorState == AgentsRunState.Starting;
    public bool IsInjectorStatusFailed => _agents.InjectorState == AgentsRunState.Failed;
    public bool IsInjectorStatusStopped => _agents.InjectorState == AgentsRunState.Stopped;
    public bool IsInjectorStatusUnknown => _agents.InjectorState == AgentsRunState.Unknown;

    private bool CanRestartAgents() => !IsAgentsToggling && IsHostStatusRunning;
    partial void OnIsAgentsTogglingChanged(bool value) => RestartAgentsCommand.NotifyCanExecuteChanged();

    private void InitializeAgents()
    {
        WireLineCollection(InjectorAppWinItems);
        WireLineCollection(InjectorColSpecsItems);
        WireLineCollection(InjectorIntColsItems);
        WireLineCollection(InjectorWarehouseAnchorItems);

        HostVersionText = _agents.HostVersion;
        InjectorVersionText = _agents.InjectorVersion;
        ApplyRuntimeSnapshot();
        SyncAgentsConfig();
        RefreshPendingChanges();

        _agents.StatusChanged += OnAgentsRuntimeChanged;
    }

    private void ReloadAgentsRuntime()
    {
        _agents.Reload();
        ApplyRuntimeSnapshot();
        SyncAgentsConfig();
    }

    partial void OnAgentsExecutablePathChanged(string value) => RefreshPendingChanges();
    partial void OnAgentsProcessNameChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorPgDriverChanged(string value) => RefreshPendingChanges();
    public bool InjectorPgSslEnabled
    {
        get => !string.Equals(InjectorPgSsl, "disable", StringComparison.OrdinalIgnoreCase);
        set
        {
            var mapped = value ? "require" : "disable";
            if (string.Equals(InjectorPgSsl, mapped, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged(nameof(InjectorPgSslEnabled));
                return;
            }

            InjectorPgSsl = mapped;
        }
    }

    partial void OnInjectorPgSslChanged(string value)
    {
        OnPropertyChanged(nameof(InjectorPgSslEnabled));
        RefreshPendingChanges();
    }
    partial void OnInjectorOptWindowClassChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorIptWindowClassChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorConfirmTimeoutMsChanged(int value) => RefreshPendingChanges();
    partial void OnInjectorOptParseGridClassNNChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorOptVerifyGridClassNNChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorIptParseGridClassNNChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorIptVerifyGridClassNNChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorOptInputClassNNChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorIptInputClassNNChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorWarehouseEnabledChanged(bool value) => RefreshPendingChanges();
    partial void OnInjectorWarehouseTaskIdentifierChanged(string value) => RefreshPendingChanges();
    partial void OnInjectorCodePickPolicyChanged(string value)
    {
        var normalized = NormCodePickPolicy(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            InjectorCodePickPolicy = normalized;
            return;
        }

        var selected = InjectorCodePickPolicyOptions.FirstOrDefault(x => string.Equals(x.Value, normalized, StringComparison.Ordinal));
        if (!ReferenceEquals(selected, SelectedInjectorCodePickPolicyOption))
            SelectedInjectorCodePickPolicyOption = selected;

        RefreshPendingChanges();
    }

    partial void OnSelectedInjectorCodePickPolicyOptionChanged(CodePickPolicyOption? value)
    {
        var selectedValue = NormCodePickPolicy(value?.Value ?? "MAX_LEVEL");
        if (!string.Equals(InjectorCodePickPolicy, selectedValue, StringComparison.Ordinal))
            InjectorCodePickPolicy = selectedValue;
        else
            RefreshPendingChanges();
    }

    public bool HasPendingChanges
        => _baselineReady && _hasPendingChanges;

    private void OnAgentsRuntimeChanged()
    {
        // While a start/stop is in flight, keep the optimistic switch; settle in the toggle finally.
        // External config edits Reload() then raise StatusChanged — rebind form when no local draft.
        Dispatcher.UIThread.Post(() =>
        {
            ApplyRuntimeSnapshot(syncRunSwitches: !IsAgentsToggling);
            if (!IsAgentsToggling && !HasPendingChanges)
            {
                SyncAgentsConfig();
            }
        });
    }

    partial void OnIsInjectorEnabledChanged(bool value)
    {
        RefreshPendingChanges();

        if (_syncingFromRuntime)
            return;

        ObserveDetached(SaveInjectorEnabledAsync(value), "settings.agents.injector_enable.detached.fail");
    }

    partial void OnIsHostRunningSwitchChanged(bool value)
    {
        if (_syncingFromRuntime)
            return;

        ObserveDetached(ToggleHostRunningAsync(value), "settings.agents.host_run.detached.fail");
    }

    partial void OnIsInjectorRunningSwitchChanged(bool value)
    {
        if (_syncingFromRuntime)
            return;

        ObserveDetached(ToggleInjectorRunningAsync(value), "settings.agents.injector_run.detached.fail");
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

    partial void OnInjectorStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsInjectorStatusRunning));
        OnPropertyChanged(nameof(IsInjectorStatusStarting));
        OnPropertyChanged(nameof(IsInjectorStatusFailed));
        OnPropertyChanged(nameof(IsInjectorStatusStopped));
        OnPropertyChanged(nameof(IsInjectorStatusUnknown));
    }

    [RelayCommand]
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
        try
        {
            var result = await SaveOptionsToConfigAsync(showToastOnError: true).ConfigureAwait(false);
            if (!result.Saved)
            {
                return false;
            }

            var restarted = false;
            if (result.Changed && _agents.IsHostRunning)
            {
                var restart = await _agents.StartOrRestartAsync().ConfigureAwait(false);
                if (!restart.Ok)
                {
                    if (!restart.SuppressToast)
                    {
                        _toast.Error("自动化集成", restart.Message);
                    }

                    return false;
                }

                restarted = true;
            }

            if (showSuccessToast)
            {
                _toast.Success("自动化集成", restarted ? "配置已保存，Agents 已重启" : "配置已保存");
            }

            // Save path uses ConfigureAwait(false); status binds + RestartAgentsCommand must update on UI.
            await RunOnUiAsync(() =>
            {
                ApplyRuntimeSnapshot();
                SyncAgentsConfig();
            });
            return !HasPendingChanges;
        }
        catch (Exception ex)
        {
            LogError("settings.agents.save.fail", "Failed to save Agents settings", ex);
            _toast.Error("自动化集成", $"保存失败：{ex.Message}");
            return false;
        }
        finally
        {
            await RunOnUiAsync(() => IsSavingSettings = false);
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
            var saved = await SaveCurrentOptionsSilentlyAsync().ConfigureAwait(false);
            if (!saved)
            {
                return;
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsAgentsToggling = false);
        }

        try
        {
            var result = await _agents.StartOrRestartAsync().ConfigureAwait(false);
            if (result.SuppressToast)
            {
                return;
            }

            if (result.Ok)
            {
                _toast.Success("自动化集成", result.Message);
            }
            else
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
            await Dispatcher.UIThread.InvokeAsync(() => ApplyRuntimeSnapshot());
        }
    }

    private async Task SaveInjectorEnabledAsync(bool enabled)
    {
        if (IsAgentsToggling)
        {
            ApplyRuntimeSnapshot();
            return;
        }

        if (SkipTrigger(enabled ? "settings.agents.injector.enable" : "settings.agents.injector.disable"))
        {
            ApplyRuntimeSnapshot();
            return;
        }

        try
        {
            await _agentsConfig
                .SetInjectorEnabledAsync(enabled, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError("settings.agents.injector_enable.fail", "Failed to save Injector enabled", ex, new { enabled });
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
                var saved = await SaveCurrentOptionsSilentlyAsync().ConfigureAwait(false);
                if (!saved)
                {
                    return;
                }

                var result = await _agents.StartOrRestartAsync().ConfigureAwait(false);
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
                // Snapshot before re-enabling so ToggleSwitch cannot push a stale On checked state.
                ApplyRuntimeSnapshot(syncRunSwitches: true);
                IsAgentsToggling = false;
            });
        }
    }

    private async Task ToggleInjectorRunningAsync(bool running)
    {
        if (IsAgentsToggling)
        {
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        if (SkipTrigger(running ? "settings.agents.injector.start" : "settings.agents.injector.stop"))
        {
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        if (running && !IsInjectorEnabled)
        {
            _toast.Error("自动化集成", "请先勾选启用 Injector");
            await RevertRunSwitchesAsync().ConfigureAwait(false);
            return;
        }

        IsAgentsToggling = true;
        try
        {
            if (running)
            {
                var saved = await SaveCurrentOptionsSilentlyAsync().ConfigureAwait(false);
                if (!saved)
                {
                    return;
                }

                var result = await _agents.StartInjectorAsync().ConfigureAwait(false);
                if (!result.Ok && !result.SuppressToast)
                {
                    _toast.Error("自动化集成", result.Message);
                }
            }
            else
            {
                var result = await _agents.StopInjectorAsync().ConfigureAwait(false);
                if (!result.Ok && !result.SuppressToast)
                {
                    _toast.Error("自动化集成", result.Message);
                }
            }
        }
        catch (Exception ex)
        {
            LogError("settings.agents.injector_run.fail", "Failed to toggle Injector running", ex, new { running });
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

    private async Task<bool> SaveCurrentOptionsSilentlyAsync()
    {
        try
        {
            var result = await SaveOptionsToConfigAsync(showToastOnError: true).ConfigureAwait(false);
            return result.Saved;
        }
        catch (Exception ex)
        {
            LogError("settings.agents.save_options.silent_fail", "Silent save options failed", ex);
            _toast.Error("自动化集成", $"配置保存失败：{ex.Message}");
            return false;
        }
    }

    private async Task<SaveOptionsResult> SaveOptionsToConfigAsync(bool showToastOnError)
    {
        var parsedInjector = ParseInjectorOptionsForSave();
        if (parsedInjector is null)
        {
            return new SaveOptionsResult(false, false);
        }

        try
        {
            var cfg = _agentsConfig.Load();
            parsedInjector.Enabled = cfg.Injector.Enabled;
            var next = new AgentsConfigDto
            {
                ExecutablePath = AgentsExecutablePath,
                ProcessName = AgentsProcessName,
                Injector = parsedInjector,
            };
            var changed = !SameAgentsHost(cfg, next)
                || !SameInjectorOptions(cfg.Injector, parsedInjector);

            if (!changed)
            {
                _savedSnapshot = BuildCurrentSnapshot();
                _baselineReady = _savedSnapshot is not null;
                RefreshPendingChanges();
                return new SaveOptionsResult(true, false);
            }

            await _agentsConfig.SaveAsync(next, CancellationToken.None).ConfigureAwait(false);
            _savedSnapshot = BuildCurrentSnapshot();
            _baselineReady = _savedSnapshot is not null;
            RefreshPendingChanges();
            return new SaveOptionsResult(true, true);
        }
        catch (Exception ex)
        {
            LogError("settings.agents.save_options.fail", "Failed to save Agents options to config", ex);
            if (showToastOnError)
            {
                _toast.Error("自动化集成", $"配置保存失败：{ex.Message}");
            }

            return new SaveOptionsResult(false, false);
        }
    }

    private static bool SameAgentsHost(AgentsConfigDto left, AgentsConfigDto right)
        => string.Equals(left.ExecutablePath?.Trim(), right.ExecutablePath?.Trim(), StringComparison.Ordinal)
           && string.Equals(left.ProcessName?.Trim(), right.ProcessName?.Trim(), StringComparison.Ordinal);

    private static bool SameInjectorOptions(InjectorOptionsDto left, InjectorOptionsDto right)
    {
        static string Norm(string? value) => (value ?? string.Empty).Trim();

        static bool SameList(IReadOnlyList<string> a, IReadOnlyList<string> b)
            => a.Count == b.Count && a.Select(Norm).SequenceEqual(b.Select(Norm), StringComparer.Ordinal);

        static bool SameAppWin(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out var value) || value != kv.Value)
                {
                    return false;
                }
            }

            return true;
        }

        return left.Enabled == right.Enabled
               && string.Equals(Norm(left.PgDriver), Norm(right.PgDriver), StringComparison.Ordinal)
               && string.Equals(Norm(left.PgSsl), Norm(right.PgSsl), StringComparison.OrdinalIgnoreCase)
               && string.Equals(Norm(left.OptWindowClass), Norm(right.OptWindowClass), StringComparison.Ordinal)
               && string.Equals(Norm(left.IptWindowClass), Norm(right.IptWindowClass), StringComparison.Ordinal)
               && left.ConfirmTimeoutMs == right.ConfirmTimeoutMs
               && string.Equals(Norm(left.OptParseGridClassNN), Norm(right.OptParseGridClassNN), StringComparison.Ordinal)
               && string.Equals(Norm(left.OptVerifyGridClassNN), Norm(right.OptVerifyGridClassNN), StringComparison.Ordinal)
               && string.Equals(Norm(left.IptParseGridClassNN), Norm(right.IptParseGridClassNN), StringComparison.Ordinal)
               && string.Equals(Norm(left.IptVerifyGridClassNN), Norm(right.IptVerifyGridClassNN), StringComparison.Ordinal)
               && string.Equals(Norm(left.OptInputClassNN), Norm(right.OptInputClassNN), StringComparison.Ordinal)
               && string.Equals(Norm(left.IptInputClassNN), Norm(right.IptInputClassNN), StringComparison.Ordinal)
               && left.WarehouseEnabled == right.WarehouseEnabled
               && string.Equals(Norm(left.CodePickPolicy), Norm(right.CodePickPolicy), StringComparison.Ordinal)
               && string.Equals(Norm(left.WarehouseTaskIdentifier), Norm(right.WarehouseTaskIdentifier), StringComparison.Ordinal)
               && SameList(left.ColSpecs, right.ColSpecs)
               && SameList(left.IntCols, right.IntCols)
               && SameList(left.WarehouseAnchorTexts, right.WarehouseAnchorTexts)
               && SameAppWin(left.AppWin, right.AppWin);
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
            IsInjectorEnabled = _agents.IsInjectorEnabled;
            // Switches mirror Running only — Starting/Failed must not look "activated".
            if (syncRunSwitches)
            {
                IsHostRunningSwitch = _agents.HostState == AgentsRunState.Running;
                IsInjectorRunningSwitch = _agents.InjectorState == AgentsRunState.Running;
            }

            HostVersionText = string.IsNullOrWhiteSpace(_agents.HostVersion) ? "未知" : _agents.HostVersion;
            InjectorVersionText = string.IsNullOrWhiteSpace(_agents.InjectorVersion) ? "未知" : _agents.InjectorVersion;
            HostLastLaunchText = _agents.HostLastLaunchAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            InjectorLastLaunchText = _agents.InjectorLastLaunchAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            HostLastErrorText = string.IsNullOrWhiteSpace(_agents.HostLastError) ? "-" : _agents.HostLastError!;
            InjectorLastErrorText = string.IsNullOrWhiteSpace(_agents.InjectorLastError) ? "-" : _agents.InjectorLastError!;

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

            var injectorState = _agents.InjectorState;
            InjectorStatusText = injectorState switch
            {
                AgentsRunState.Running => "运行中",
                AgentsRunState.Starting => "启动中",
                AgentsRunState.Failed => "启动失败",
                AgentsRunState.Stopped => "未启动",
                _ => "未知",
            };
            InjectorStatusDetail = injectorState switch
            {
                AgentsRunState.Running => "Injector 已就绪",
                AgentsRunState.Starting => "Agents 已拉起，正在等待 Injector 自检完成",
                AgentsRunState.Failed => string.IsNullOrWhiteSpace(_agents.InjectorLastError)
                    ? "Injector 启动未完成，请检查模块配置后重试"
                    : _agents.InjectorLastError!,
                AgentsRunState.Stopped => !IsInjectorEnabled
                    ? "Injector 未启用"
                    : hostState is not AgentsRunState.Running
                        ? "Agents 未运行，模块随 Agents 停止"
                        : "Injector 未运行",
                _ => "Injector 状态检测异常",
            };
        }
        finally
        {
            _syncingFromRuntime = false;
        }
    }

    private void SyncAgentsConfig()
    {
        var cfg = _agentsConfig.Load();
        var injector = cfg.Injector;
        var appWin = injector.AppWin.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var colSpecs = injector.ColSpecs.ToList();
        var intCols = injector.IntCols.ToList();
        var warehouseAnchors = injector.WarehouseAnchorTexts.ToList();
        var warehouseTaskIdentifier = injector.WarehouseTaskIdentifier;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyAgentsSnapshot(
                cfg.ExecutablePath,
                cfg.ProcessName,
                injector.PgDriver,
                injector.PgSsl,
                injector.OptWindowClass,
                injector.IptWindowClass,
                injector.ConfirmTimeoutMs,
                injector.OptParseGridClassNN,
                injector.OptVerifyGridClassNN,
                injector.IptParseGridClassNN,
                injector.IptVerifyGridClassNN,
                injector.OptInputClassNN,
                injector.IptInputClassNN,
                injector.WarehouseEnabled,
                warehouseTaskIdentifier,
                injector.CodePickPolicy,
                appWin,
                colSpecs,
                intCols,
                warehouseAnchors));
            return;
        }

        ApplyAgentsSnapshot(
            cfg.ExecutablePath,
            cfg.ProcessName,
            injector.PgDriver,
            injector.PgSsl,
            injector.OptWindowClass,
            injector.IptWindowClass,
            injector.ConfirmTimeoutMs,
            injector.OptParseGridClassNN,
            injector.OptVerifyGridClassNN,
            injector.IptParseGridClassNN,
            injector.IptVerifyGridClassNN,
            injector.OptInputClassNN,
            injector.IptInputClassNN,
            injector.WarehouseEnabled,
            warehouseTaskIdentifier,
            injector.CodePickPolicy,
            appWin,
            colSpecs,
            intCols,
            warehouseAnchors);
    }

    private void ApplyAgentsSnapshot(
        string agentsExecutablePath,
        string agentsProcessName,
        string pgDriver,
        string pgSsl,
        string optWindowClass,
        string iptWindowClass,
        int confirmTimeoutMs,
        string optParseGridClassNn,
        string optVerifyGridClassNn,
        string iptParseGridClassNn,
        string iptVerifyGridClassNn,
        string optInputClassNn,
        string iptInputClassNn,
        bool warehouseEnabled,
        string warehouseTaskIdentifier,
        string codePickPolicy,
        IReadOnlyCollection<string> appWin,
        IReadOnlyCollection<string> colSpecs,
        IReadOnlyCollection<string> intCols,
        IReadOnlyCollection<string> warehouseAnchors)
    {
        lock (_snapshotGate)
        {
            _suppressPendingRecalc = true;
            AgentsExecutablePath = agentsExecutablePath;
            AgentsProcessName = agentsProcessName;
            InjectorPgDriver = pgDriver;
            InjectorPgSsl = pgSsl;
            InjectorOptWindowClass = optWindowClass;
            InjectorIptWindowClass = iptWindowClass;
            InjectorConfirmTimeoutMs = confirmTimeoutMs;
            InjectorOptParseGridClassNN = optParseGridClassNn;
            InjectorOptVerifyGridClassNN = optVerifyGridClassNn;
            InjectorIptParseGridClassNN = iptParseGridClassNn;
            InjectorIptVerifyGridClassNN = iptVerifyGridClassNn;
            InjectorOptInputClassNN = optInputClassNn;
            InjectorIptInputClassNN = iptInputClassNn;
            InjectorWarehouseEnabled = warehouseEnabled;
            InjectorWarehouseTaskIdentifier = warehouseTaskIdentifier;
            InjectorCodePickPolicy = codePickPolicy;
            SelectedInjectorCodePickPolicyOption = InjectorCodePickPolicyOptions
                .FirstOrDefault(x => string.Equals(x.Value, NormCodePickPolicy(codePickPolicy), StringComparison.Ordinal));
            ResetLineItems(InjectorAppWinItems, appWin);
            ResetLineItems(InjectorColSpecsItems, colSpecs);
            ResetLineItems(InjectorIntColsItems, intCols);
            ResetLineItems(InjectorWarehouseAnchorItems, warehouseAnchors);
            _savedSnapshot = BuildCurrentSnapshot();
            _baselineReady = _savedSnapshot is not null;
            _suppressPendingRecalc = false;
            RefreshPendingChanges();
        }
    }

    private static string NormCodePickPolicy(string? value)
    {
        var policy = (value ?? string.Empty).Trim().ToUpperInvariant();
        return policy is "MAX_LEVEL" or "MIN_LEVEL" ? policy : "MAX_LEVEL";
    }

    private static string NormTaskId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "单据号||当前编号" : value.Trim();

    private void WireLineCollection(ObservableCollection<InjectorLineItem> collection)
    {
        collection.CollectionChanged += OnInjectorLineCollectionChanged;
        foreach (var item in collection)
        {
            item.PropertyChanged += OnInjectorLineItemPropertyChanged;
        }
    }

    private void OnInjectorLineCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is InjectorLineItem line)
                {
                    line.PropertyChanged -= OnInjectorLineItemPropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is InjectorLineItem line)
                {
                    line.PropertyChanged += OnInjectorLineItemPropertyChanged;
                }
            }
        }

        RefreshPendingChanges();
    }

    private void OnInjectorLineItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InjectorLineItem.Value) or null or "")
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
            if (_hasPendingChanges == pending)
            {
                return;
            }

            _hasPendingChanges = pending;
            OnPropertyChanged(nameof(HasPendingChanges));
            RefreshUnsaved();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return;
        }

        Dispatcher.UIThread.Post(Apply);
    }

    private InjectorOptionsDto? ParseInjectorOptionsForSave()
    {
        try
        {
            var appWin = ParseAppWinItems(InjectorAppWinItems);
            if (appWin.Count == 0)
            {
                _toast.Error("自动化集成", "AppWin 至少需要一个可执行文件");
                return null;
            }

            var colSpecs = ParseLineItems(InjectorColSpecsItems);
            if (colSpecs.Count == 0)
            {
                _toast.Error("自动化集成", "ColSpecs 不能为空");
                return null;
            }

            var intCols = ParseLineItems(InjectorIntColsItems);

            if (InjectorConfirmTimeoutMs is < 100 or > 10000)
            {
                _toast.Error("自动化集成", "ConfirmTimeoutMs 范围应为 100-10000");
                return null;
            }

            var codePickPolicy = InjectorCodePickPolicy.Trim().ToUpperInvariant();
            if (codePickPolicy is not ("MAX_LEVEL" or "MIN_LEVEL"))
            {
                _toast.Error("自动化集成", "CodePickPolicy 仅支持 MAX_LEVEL 或 MIN_LEVEL");
                return null;
            }

            var warehouseAnchors = ParseLineItems(InjectorWarehouseAnchorItems);
            var warehouseTaskIdentifier = NormTaskId(InjectorWarehouseTaskIdentifier);

            return new InjectorOptionsDto
            {
                PgDriver = InjectorPgDriver.Trim(),
                PgSsl = InjectorPgSsl.Trim(),
                OptWindowClass = InjectorOptWindowClass.Trim(),
                IptWindowClass = InjectorIptWindowClass.Trim(),
                ConfirmTimeoutMs = InjectorConfirmTimeoutMs,
                OptParseGridClassNN = InjectorOptParseGridClassNN.Trim(),
                OptVerifyGridClassNN = InjectorOptVerifyGridClassNN.Trim(),
                IptParseGridClassNN = InjectorIptParseGridClassNN.Trim(),
                IptVerifyGridClassNN = InjectorIptVerifyGridClassNN.Trim(),
                OptInputClassNN = InjectorOptInputClassNN.Trim(),
                IptInputClassNN = InjectorIptInputClassNN.Trim(),
                WarehouseEnabled = InjectorWarehouseEnabled,
                AppWin = appWin,
                ColSpecs = colSpecs,
                IntCols = intCols,
                CodePickPolicy = codePickPolicy,
                WarehouseAnchorTexts = warehouseAnchors,
                WarehouseTaskIdentifier = warehouseTaskIdentifier,
            };
        }
        catch (Exception ex)
        {
            LogError("settings.agents.injector_options.parse_fail", "Failed to parse Injector options", ex);
            _toast.Error("自动化集成", $"Injector 配置格式错误：{ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    private void AddInjectorAppWinItem() => InjectorAppWinItems.Add(new InjectorLineItem());

    [RelayCommand]
    private void RemoveInjectorAppWinItem(InjectorLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        InjectorAppWinItems.Remove(item);
    }

    [RelayCommand]
    private void AddInjectorColSpecsItem() => InjectorColSpecsItems.Add(new InjectorLineItem());

    [RelayCommand]
    private void RemoveInjectorColSpecsItem(InjectorLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        InjectorColSpecsItems.Remove(item);
    }

    [RelayCommand]
    private void AddInjectorIntColsItem() => InjectorIntColsItems.Add(new InjectorLineItem());

    [RelayCommand]
    private void RemoveInjectorIntColsItem(InjectorLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        InjectorIntColsItems.Remove(item);
    }

    [RelayCommand]
    private void AddInjectorWarehouseAnchorItem() => InjectorWarehouseAnchorItems.Add(new InjectorLineItem());

    [RelayCommand]
    private void RemoveInjectorWarehouseAnchorItem(InjectorLineItem? item)
    {
        if (item is null)
        {
            return;
        }

        InjectorWarehouseAnchorItems.Remove(item);
    }

    private static Dictionary<string, int> ParseAppWinItems(IEnumerable<InjectorLineItem> items)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in ParseLineItems(items))
        {
            result[v] = 1;
        }

        return result;
    }

    private static List<string> ParseLineItems(IEnumerable<InjectorLineItem> items)
        => items.Select(x => (x.Value).Trim()).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> SnapshotLineItems(IEnumerable<InjectorLineItem> items)
        => items.Select(x => (x.Value).Trim()).ToList();

    private static void ResetLineItems(ObservableCollection<InjectorLineItem> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values
                     .Select(x => x.Trim())
                     .Where(x => x != "")
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            target.Add(new InjectorLineItem(value));
        }
    }

    private AgentsEditorSnapshot? BuildCurrentSnapshot()
    {
        try
        {
            return new AgentsEditorSnapshot(
                AgentsExecutablePath.Trim(),
                AgentsProcessName.Trim(),
                InjectorPgDriver.Trim(),
                InjectorPgSsl.Trim(),
                InjectorOptWindowClass.Trim(),
                InjectorIptWindowClass.Trim(),
                InjectorConfirmTimeoutMs,
                InjectorOptParseGridClassNN.Trim(),
                InjectorOptVerifyGridClassNN.Trim(),
                InjectorIptParseGridClassNN.Trim(),
                InjectorIptVerifyGridClassNN.Trim(),
                InjectorOptInputClassNN.Trim(),
                InjectorIptInputClassNN.Trim(),
                InjectorWarehouseEnabled,
                NormTaskId(InjectorWarehouseTaskIdentifier),
                InjectorCodePickPolicy.Trim().ToUpperInvariant(),
                SnapshotLineItems(InjectorAppWinItems),
                SnapshotLineItems(InjectorColSpecsItems),
                SnapshotLineItems(InjectorIntColsItems),
                SnapshotLineItems(InjectorWarehouseAnchorItems));
        }
        catch
        {
            return null;
        }
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

        if (!string.Equals(left.InjectorPgDriver, right.InjectorPgDriver, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorPgSsl, right.InjectorPgSsl, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorOptWindowClass, right.InjectorOptWindowClass, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorIptWindowClass, right.InjectorIptWindowClass, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.InjectorConfirmTimeoutMs != right.InjectorConfirmTimeoutMs)
        {
            return false;
        }

        if (!string.Equals(left.InjectorOptParseGridClassNN, right.InjectorOptParseGridClassNN, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorOptVerifyGridClassNN, right.InjectorOptVerifyGridClassNN, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorIptParseGridClassNN, right.InjectorIptParseGridClassNN, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorIptVerifyGridClassNN, right.InjectorIptVerifyGridClassNN, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorOptInputClassNN, right.InjectorOptInputClassNN, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorIptInputClassNN, right.InjectorIptInputClassNN, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.InjectorWarehouseEnabled != right.InjectorWarehouseEnabled)
        {
            return false;
        }

        if (!string.Equals(left.InjectorWarehouseTaskIdentifier, right.InjectorWarehouseTaskIdentifier, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.InjectorCodePickPolicy, right.InjectorCodePickPolicy, StringComparison.Ordinal))
        {
            return false;
        }

        if (!left.InjectorAppWinItems.SequenceEqual(right.InjectorAppWinItems, StringComparer.Ordinal))
        {
            return false;
        }

        if (!left.InjectorColSpecsItems.SequenceEqual(right.InjectorColSpecsItems, StringComparer.Ordinal))
        {
            return false;
        }

        if (!left.InjectorIntColsItems.SequenceEqual(right.InjectorIntColsItems, StringComparer.Ordinal))
        {
            return false;
        }

        if (!left.InjectorWarehouseAnchorItems.SequenceEqual(right.InjectorWarehouseAnchorItems, StringComparer.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void DisposeAgents()
    {
        try { _agents.StatusChanged -= OnAgentsRuntimeChanged; }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.runtime_unsub_fail", "Failed to unsubscribe runtime status", ex);
        }

        try { InjectorAppWinItems.CollectionChanged -= OnInjectorLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.appwin_collection_unsub_fail", "Failed to unsubscribe InjectorAppWinItems", ex);
        }

        try { InjectorColSpecsItems.CollectionChanged -= OnInjectorLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.colspecs_collection_unsub_fail", "Failed to unsubscribe InjectorColSpecsItems", ex);
        }

        try { InjectorIntColsItems.CollectionChanged -= OnInjectorLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.intcols_collection_unsub_fail", "Failed to unsubscribe InjectorIntColsItems", ex);
        }

        try { InjectorWarehouseAnchorItems.CollectionChanged -= OnInjectorLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("settings.agents.dispose.warehouse_anchors_collection_unsub_fail", "Failed to unsubscribe InjectorWarehouseAnchorItems", ex);
        }

        foreach (var item in InjectorAppWinItems)
        {
            try { item.PropertyChanged -= OnInjectorLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("settings.agents.dispose.appwin_item_unsub_fail", "Failed to unsubscribe InjectorAppWin item", ex);
            }
        }

        foreach (var item in InjectorColSpecsItems)
        {
            try { item.PropertyChanged -= OnInjectorLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("settings.agents.dispose.colspecs_item_unsub_fail", "Failed to unsubscribe InjectorColSpecs item", ex);
            }
        }

        foreach (var item in InjectorIntColsItems)
        {
            try { item.PropertyChanged -= OnInjectorLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("settings.agents.dispose.intcols_item_unsub_fail", "Failed to unsubscribe InjectorIntCols item", ex);
            }
        }

        foreach (var item in InjectorWarehouseAnchorItems)
        {
            try { item.PropertyChanged -= OnInjectorLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("settings.agents.dispose.warehouse_anchor_item_unsub_fail", "Failed to unsubscribe InjectorWarehouseAnchor item", ex);
            }
        }
    }

    private sealed record AgentsEditorSnapshot(
        string AgentsExecutablePath,
        string AgentsProcessName,
        string InjectorPgDriver,
        string InjectorPgSsl,
        string InjectorOptWindowClass,
        string InjectorIptWindowClass,
        int InjectorConfirmTimeoutMs,
        string InjectorOptParseGridClassNN,
        string InjectorOptVerifyGridClassNN,
        string InjectorIptParseGridClassNN,
        string InjectorIptVerifyGridClassNN,
        string InjectorOptInputClassNN,
        string InjectorIptInputClassNN,
        bool InjectorWarehouseEnabled,
        string InjectorWarehouseTaskIdentifier,
        string InjectorCodePickPolicy,
        IReadOnlyList<string> InjectorAppWinItems,
        IReadOnlyList<string> InjectorColSpecsItems,
        IReadOnlyList<string> InjectorIntColsItems,
        IReadOnlyList<string> InjectorWarehouseAnchorItems);
}
