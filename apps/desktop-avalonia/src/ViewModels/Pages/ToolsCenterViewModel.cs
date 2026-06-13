using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using global::Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class AgentLineItem : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;

    public AgentLineItem()
    {
    }

    public AgentLineItem(string value)
    {
        _value = value;
    }
}

public sealed class CodePickPolicyOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    public override string ToString() => Label;
}

public sealed partial class ToolsCenterViewModel : AppPageBase
{
    private readonly record struct SaveOptionsResult(bool Saved, bool Changed);

    public override string DisplayName => "自动化套件";
    public override string Icon => "Syringe";
    public override int Index => 4;
    public override ICommand? RefreshCommand => _refreshRuntimeCommand;

    private readonly IAhkRuntimeService _ahkRuntime;
    private readonly IToastService _toast;
    private readonly IAppConfigStore _configStore;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAsyncRelayCommand _refreshRuntimeCommand;
    private readonly object _agentSnapshotGate = new();
    private ToolEditorSnapshot? _savedSnapshot;
    private bool _hasPendingChanges;
    private bool _baselineReady;
    private bool _suppressPendingRecalc;

    private bool _syncingFromRuntime;

    [ObservableProperty] private bool _isAhkEnabled;
    [ObservableProperty] private bool _isAhkToggling;
    [ObservableProperty] private bool _isSavingSettings;
    [ObservableProperty] private string _ahkExecutablePath = string.Empty;
    [ObservableProperty] private string _ahkProcessName = string.Empty;
    [ObservableProperty] private string _ahkStatusText = "检测中";
    [ObservableProperty] private string _ahkStatusHeadline = "状态：检测中";
    [ObservableProperty] private string _ahkStatusDetail = "等待进程状态刷新";
    [ObservableProperty] private string _ahkVersionText = "未知";
    [ObservableProperty] private string _programVersionText = "未知";
    [ObservableProperty] private string _lastLaunchText = "-";
    [ObservableProperty] private string _lastErrorText = "-";
    [ObservableProperty] private string _agentPgDriver = "PostgreSQL Unicode(x64)";
    [ObservableProperty] private string _agentPgSsl = "disable";
    [ObservableProperty] private string _agentOptWindowClass = "TFrm_mzcffy";
    [ObservableProperty] private string _agentIptWindowClass = "Tfrm_wzzsm";
    [ObservableProperty] private int _agentConfirmTimeoutMs = 2500;
    [ObservableProperty] private string _agentOptParseGridClassNN = "TcxGridSite2";
    [ObservableProperty] private string _agentOptVerifyGridClassNN = "TcxGridSite2";
    [ObservableProperty] private string _agentIptParseGridClassNN = "TcxGridSite2";
    [ObservableProperty] private string _agentIptVerifyGridClassNN = "TcxGridSite1";
    [ObservableProperty] private string _agentOptInputClassNN = "TMemo2";
    [ObservableProperty] private string _agentIptInputClassNN = "TEdit1";
    [ObservableProperty] private bool _agentWarehouseEnabled;
    [ObservableProperty] private string _agentCodePickPolicy = "MAX_LEVEL";
    [ObservableProperty] private string _agentWarehouseTaskIdentifier = "单据号||当前编号";
    [ObservableProperty] private CodePickPolicyOption? _selectedAgentCodePickPolicyOption;
    public ObservableCollection<AgentLineItem> AgentAppWinItems { get; } = [];
    public ObservableCollection<AgentLineItem> AgentColSpecsItems { get; } = [];
    public ObservableCollection<AgentLineItem> AgentIntColsItems { get; } = [];
    public ObservableCollection<AgentLineItem> AgentWarehouseAnchorItems { get; } = [];
    public IReadOnlyList<CodePickPolicyOption> AgentCodePickPolicyOptions { get; } =
    [
        new() { Value = "MAX_LEVEL", Label = "按最大码" },
        new() { Value = "MIN_LEVEL", Label = "按最小码" },
    ];

    public bool IsAhkStatusRunning => string.Equals(AhkStatusText, "运行中", StringComparison.Ordinal);
    public bool IsAhkStatusStopped => string.Equals(AhkStatusText, "未启动", StringComparison.Ordinal);
    public bool IsAhkStatusUnknown => !IsAhkStatusRunning && !IsAhkStatusStopped;

    private bool CanRestartAhk() => !IsAhkToggling && IsAhkEnabled;
    partial void OnIsAhkTogglingChanged(bool value) => RestartAhkCommand.NotifyCanExecuteChanged();

    public ToolsCenterViewModel(
        IAhkRuntimeService ahkRuntime,
        IToastService toast,
        IAppConfigStore configStore,
        IReleaseVersionService releaseVersion)
    {
        _ahkRuntime = ahkRuntime;
        _toast = toast;
        _configStore = configStore;
        _releaseVersion = releaseVersion;
        _refreshRuntimeCommand = new AsyncRelayCommand(RefreshRuntimeStateAsync);
        WireLineCollection(AgentAppWinItems);
        WireLineCollection(AgentColSpecsItems);
        WireLineCollection(AgentIntColsItems);
        WireLineCollection(AgentWarehouseAnchorItems);

        ProgramVersionText = ResolveProgramVersionText(_ahkRuntime.ToolVersion, _releaseVersion.Current.AgentVersion);
        ApplyRuntimeSnapshot();
        LoadAgentConfigSnapshot();
        NotifyPendingChangesState();

        _ahkRuntime.StatusChanged += OnAhkRuntimeChanged;
    }

    partial void OnAhkExecutablePathChanged(string value) => NotifyPendingChangesState();
    partial void OnAhkProcessNameChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentPgDriverChanged(string value) => NotifyPendingChangesState();
    public bool AgentPgSslEnabled
    {
        get => string.Equals(AgentPgSsl, "enable", StringComparison.OrdinalIgnoreCase);
        set
        {
            var mapped = value ? "enable" : "disable";
            if (string.Equals(AgentPgSsl, mapped, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged(nameof(AgentPgSslEnabled));
                return;
            }

            AgentPgSsl = mapped;
        }
    }

    partial void OnAgentPgSslChanged(string value)
    {
        OnPropertyChanged(nameof(AgentPgSslEnabled));
        NotifyPendingChangesState();
    }
    partial void OnAgentOptWindowClassChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentIptWindowClassChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentConfirmTimeoutMsChanged(int value) => NotifyPendingChangesState();
    partial void OnAgentOptParseGridClassNNChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentOptVerifyGridClassNNChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentIptParseGridClassNNChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentIptVerifyGridClassNNChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentOptInputClassNNChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentIptInputClassNNChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentWarehouseEnabledChanged(bool value) => NotifyPendingChangesState();
    partial void OnAgentWarehouseTaskIdentifierChanged(string value) => NotifyPendingChangesState();
    partial void OnAgentCodePickPolicyChanged(string value)
    {
        var normalized = NormalizeCodePickPolicyValue(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            AgentCodePickPolicy = normalized;
            return;
        }

        var selected = AgentCodePickPolicyOptions.FirstOrDefault(x => string.Equals(x.Value, normalized, StringComparison.Ordinal));
        if (!ReferenceEquals(selected, SelectedAgentCodePickPolicyOption))
            SelectedAgentCodePickPolicyOption = selected;

        NotifyPendingChangesState();
    }

    partial void OnSelectedAgentCodePickPolicyOptionChanged(CodePickPolicyOption? value)
    {
        var selectedValue = NormalizeCodePickPolicyValue(value?.Value ?? "MAX_LEVEL");
        if (!string.Equals(AgentCodePickPolicy, selectedValue, StringComparison.Ordinal))
            AgentCodePickPolicy = selectedValue;
        else
            NotifyPendingChangesState();
    }

    public bool HasPendingChanges
        => _baselineReady && _hasPendingChanges;
    public bool IsSavedState => !HasPendingChanges;

    protected override Task ReloadCoreAsync(CancellationToken ct)
    {
        _ahkRuntime.Reload();
        ApplyRuntimeSnapshot();
        LoadAgentConfigSnapshot();
        return Task.CompletedTask;
    }

    private Task RefreshRuntimeStateAsync()
    {
        _ahkRuntime.Reload();
        ApplyRuntimeSnapshot();
        return Task.CompletedTask;
    }

    private void OnAhkRuntimeChanged()
    {
        Dispatcher.UIThread.Post(ApplyRuntimeSnapshot);
    }

    partial void OnIsAhkEnabledChanged(bool value)
    {
        RestartAhkCommand.NotifyCanExecuteChanged();
        NotifyPendingChangesState();

        if (_syncingFromRuntime)
            return;

        _ = ToggleAhkAsync(value);
    }

    partial void OnAhkStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsAhkStatusRunning));
        OnPropertyChanged(nameof(IsAhkStatusStopped));
        OnPropertyChanged(nameof(IsAhkStatusUnknown));
    }

    [RelayCommand]
    private async Task SaveAhkSettingsAsync()
    {
        if (ShouldSkipTrigger())
            return;

        IsSavingSettings = true;
        try
        {
            var result = await SaveOptionsToConfigAsync(showToastOnError: true).ConfigureAwait(false);
            if (!result.Saved)
                return;

            var restarted = false;
            if (result.Changed && _ahkRuntime.IsRunning)
            {
                var restart = await _ahkRuntime.StartOrRestartAsync().ConfigureAwait(false);
                if (!restart.Ok)
                {
                    if (!restart.SuppressToast)
                        _toast.Error("自动化套件", restart.Message);
                    return;
                }

                restarted = true;
            }

            _toast.Success("自动化套件", restarted ? "配置已保存，Agent 已重启" : "配置已保存");
            ApplyRuntimeSnapshot();
            LoadAgentConfigSnapshot();
        }
        catch (Exception ex)
        {
            LogError("tools.save_settings.fail", "Failed to save AHK settings", ex);
            _toast.Error("自动化套件", $"保存失败：{ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsSavingSettings = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestartAhk))]
    private async Task RestartAhkAsync()
    {
        if (IsAhkToggling)
            return;
        if (ShouldSkipTrigger("tools.ahk.restart"))
            return;
        if (!_ahkRuntime.IsRunning)
            return;

        IsAhkToggling = true;
        try
        {
            var saved = await SaveCurrentOptionsSilentlyAsync().ConfigureAwait(false);
            if (!saved)
                return;

            var result = await _ahkRuntime.StartOrRestartAsync().ConfigureAwait(false);
            if (result.SuppressToast)
                return;

            if (result.Ok)
                _toast.Success("自动化套件", result.Message);
            else
                _toast.Error("自动化套件", result.Message);
        }
        catch (Exception ex)
        {
            LogError("tools.restart_ahk.fail", "Failed to restart AHK runtime", ex);
            _toast.Error("自动化套件", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsAhkToggling = false;
                ApplyRuntimeSnapshot();
            });
        }
    }

    private async Task ToggleAhkAsync(bool enabled)
    {
        if (IsAhkToggling)
            return;
        if (ShouldSkipTrigger(enabled ? "tools.ahk.enable" : "tools.ahk.disable"))
            return;

        IsAhkToggling = true;
        try
        {
            if (enabled)
            {
                var saved = await SaveCurrentOptionsSilentlyAsync().ConfigureAwait(false);
                if (!saved)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsAhkToggling = false;
                        ApplyRuntimeSnapshot();
                    });
                    return;
                }
            }

            ToolCommandResult result = enabled
                ? await _ahkRuntime.StartOrRestartAsync().ConfigureAwait(false)
                : await _ahkRuntime.StopAsync().ConfigureAwait(false);

            if (!result.Ok && !result.SuppressToast)
                _toast.Error("自动化套件", result.Message);
        }
        catch (Exception ex)
        {
            LogError("tools.toggle_ahk.fail", "Failed to toggle AHK runtime", ex, new { enabled });
            _toast.Error("自动化套件", ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsAhkToggling = false;
                ApplyRuntimeSnapshot();
            });
        }
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
            LogError("tools.save_options.silent_fail", "Silent save options failed", ex);
            _toast.Error("自动化套件", $"配置保存失败：{ex.Message}");
            return false;
        }
    }

    private async Task<SaveOptionsResult> SaveOptionsToConfigAsync(bool showToastOnError)
    {
        var parsedAgent = ParseAgentOptionsForSave();
        if (parsedAgent is null)
            return new SaveOptionsResult(false, false);

        try
        {
            var cfg = _configStore.Load();
            var nextAhk = new AhkToolOptions
            {
                ExecutablePath = AhkExecutablePath,
                ProcessName = AhkProcessName
            };
            var changed = !SameAhkOptions(cfg.AutomationTools.Ahk, nextAhk)
                || !SameAgentOptions(cfg.AutomationTools.Agent, parsedAgent);

            if (!changed)
            {
                _savedSnapshot = BuildCurrentSnapshot();
                _baselineReady = _savedSnapshot is not null;
                NotifyPendingChangesState();
                return new SaveOptionsResult(true, false);
            }

            cfg.AutomationTools.Ahk = nextAhk;
            cfg.AutomationTools.Agent = parsedAgent;
            await _configStore.SaveAsync(cfg).ConfigureAwait(false);
            _ahkRuntime.Reload();
            _savedSnapshot = BuildCurrentSnapshot();
            _baselineReady = _savedSnapshot is not null;
            NotifyPendingChangesState();
            return new SaveOptionsResult(true, true);
        }
        catch (Exception ex)
        {
            LogError("tools.save_options.fail", "Failed to save tool options to config", ex);
            if (showToastOnError)
                _toast.Error("自动化套件", $"配置保存失败：{ex.Message}");
            return new SaveOptionsResult(false, false);
        }
    }

    private static bool SameAhkOptions(AhkToolOptions left, AhkToolOptions right)
        => string.Equals(left.ExecutablePath?.Trim(), right.ExecutablePath?.Trim(), StringComparison.Ordinal)
           && string.Equals(left.ProcessName?.Trim(), right.ProcessName?.Trim(), StringComparison.Ordinal);

    private static bool SameAgentOptions(AgentToolOptions left, AgentToolOptions right)
    {
        static string Norm(string? value) => (value ?? string.Empty).Trim();

        static bool SameList(IReadOnlyList<string> a, IReadOnlyList<string> b)
            => a.Count == b.Count && a.Select(Norm).SequenceEqual(b.Select(Norm), StringComparer.Ordinal);

        static bool SameAppWin(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
        {
            if (a.Count != b.Count)
                return false;

            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out var value) || value != kv.Value)
                    return false;
            }

            return true;
        }

        return string.Equals(Norm(left.PgDriver), Norm(right.PgDriver), StringComparison.Ordinal)
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

    private void ApplyRuntimeSnapshot()
    {
        _syncingFromRuntime = true;
        try
        {
            IsAhkEnabled = _ahkRuntime.IsRunning;
            AhkVersionText = _ahkRuntime.ToolVersion;
            ProgramVersionText = ResolveProgramVersionText(AhkVersionText, _releaseVersion.Current.AgentVersion);
            LastLaunchText = _ahkRuntime.LastLaunchAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            LastErrorText = string.IsNullOrWhiteSpace(_ahkRuntime.LastError) ? "-" : _ahkRuntime.LastError!;
            AhkStatusText = _ahkRuntime.State switch
            {
                ToolRunState.Running => "运行中",
                ToolRunState.Stopped => "未启动",
                _ => "未知",
            };
            AhkStatusHeadline = $"状态：{AhkStatusText}";
            AhkStatusDetail = _ahkRuntime.State switch
            {
                ToolRunState.Running => "进程已运行，可在右上角或本页执行“重启”",
                ToolRunState.Stopped => "当前未检测到进程，开启开关或点击“重启”即可启动",
                _ => "状态检测异常，请检查进程名和可执行路径",
            };
        }
        finally
        {
            _syncingFromRuntime = false;
        }
    }

    private void LoadAgentConfigSnapshot()
    {
        var cfg = _configStore.Load();
        var ahk = cfg.AutomationTools.Ahk;
        var agent = cfg.AutomationTools.Agent;
        var appWin = agent.AppWin.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var colSpecs = agent.ColSpecs.ToList();
        var intCols = agent.IntCols.ToList();
        var warehouseAnchors = agent.WarehouseAnchorTexts.ToList();
        var warehouseTaskIdentifier = agent.WarehouseTaskIdentifier;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyAgentSnapshot(
                ahk.ExecutablePath,
                ahk.ProcessName,
                agent.PgDriver,
                agent.PgSsl,
                agent.OptWindowClass,
                agent.IptWindowClass,
                agent.ConfirmTimeoutMs,
                agent.OptParseGridClassNN,
                agent.OptVerifyGridClassNN,
                agent.IptParseGridClassNN,
                agent.IptVerifyGridClassNN,
                agent.OptInputClassNN,
                agent.IptInputClassNN,
                agent.WarehouseEnabled,
                warehouseTaskIdentifier,
                agent.CodePickPolicy,
                appWin,
                colSpecs,
                intCols,
                warehouseAnchors));
            return;
        }

        ApplyAgentSnapshot(
            ahk.ExecutablePath,
            ahk.ProcessName,
            agent.PgDriver,
            agent.PgSsl,
            agent.OptWindowClass,
            agent.IptWindowClass,
            agent.ConfirmTimeoutMs,
            agent.OptParseGridClassNN,
            agent.OptVerifyGridClassNN,
            agent.IptParseGridClassNN,
            agent.IptVerifyGridClassNN,
            agent.OptInputClassNN,
            agent.IptInputClassNN,
            agent.WarehouseEnabled,
            warehouseTaskIdentifier,
            agent.CodePickPolicy,
            appWin,
            colSpecs,
            intCols,
            warehouseAnchors);
    }

    private void ApplyAgentSnapshot(
        string ahkExecutablePath,
        string ahkProcessName,
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
        lock (_agentSnapshotGate)
        {
            _suppressPendingRecalc = true;
            AhkExecutablePath = ahkExecutablePath;
            AhkProcessName = ahkProcessName;
            AgentPgDriver = pgDriver;
            AgentPgSsl = pgSsl;
            AgentOptWindowClass = optWindowClass;
            AgentIptWindowClass = iptWindowClass;
            AgentConfirmTimeoutMs = confirmTimeoutMs;
            AgentOptParseGridClassNN = optParseGridClassNn;
            AgentOptVerifyGridClassNN = optVerifyGridClassNn;
            AgentIptParseGridClassNN = iptParseGridClassNn;
            AgentIptVerifyGridClassNN = iptVerifyGridClassNn;
            AgentOptInputClassNN = optInputClassNn;
            AgentIptInputClassNN = iptInputClassNn;
            AgentWarehouseEnabled = warehouseEnabled;
            AgentWarehouseTaskIdentifier = warehouseTaskIdentifier;
            AgentCodePickPolicy = codePickPolicy;
            SelectedAgentCodePickPolicyOption = AgentCodePickPolicyOptions
                .FirstOrDefault(x => string.Equals(x.Value, NormalizeCodePickPolicyValue(codePickPolicy), StringComparison.Ordinal));
            ResetLineItems(AgentAppWinItems, appWin);
            ResetLineItems(AgentColSpecsItems, colSpecs);
            ResetLineItems(AgentIntColsItems, intCols);
            ResetLineItems(AgentWarehouseAnchorItems, warehouseAnchors);
            _savedSnapshot = BuildCurrentSnapshot();
            _baselineReady = _savedSnapshot is not null;
            _suppressPendingRecalc = false;
            NotifyPendingChangesState();
        }
    }

    private static string NormalizeCodePickPolicyValue(string? value)
    {
        var policy = (value ?? string.Empty).Trim().ToUpperInvariant();
        return policy is "MAX_LEVEL" or "MIN_LEVEL" ? policy : "MAX_LEVEL";
    }

    private static string NormalizeWarehouseTaskIdentifierValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "单据号||当前编号" : value.Trim();

    private void WireLineCollection(ObservableCollection<AgentLineItem> collection)
    {
        collection.CollectionChanged += OnAgentLineCollectionChanged;
        foreach (var item in collection)
            item.PropertyChanged += OnAgentLineItemPropertyChanged;
    }

    private void OnAgentLineCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is AgentLineItem line)
                    line.PropertyChanged -= OnAgentLineItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is AgentLineItem line)
                    line.PropertyChanged += OnAgentLineItemPropertyChanged;
            }
        }

        NotifyPendingChangesState();
    }

    private void OnAgentLineItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentLineItem.Value) or null or "")
            NotifyPendingChangesState();
    }

    private void NotifyPendingChangesState()
    {
        if (_suppressPendingRecalc)
            return;

        if (!_baselineReady || _savedSnapshot is null)
        {
            if (_hasPendingChanges)
            {
                _hasPendingChanges = false;
                OnPropertyChanged(nameof(HasPendingChanges));
                OnPropertyChanged(nameof(IsSavedState));
            }
            return;
        }

        var current = BuildCurrentSnapshot();
        var pending = current is null || !SnapshotEquals(_savedSnapshot, current);
        if (_hasPendingChanges == pending)
            return;

        _hasPendingChanges = pending;
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(IsSavedState));
    }

    private AgentToolOptions? ParseAgentOptionsForSave()
    {
        try
        {
            var appWin = ParseAppWinItems(AgentAppWinItems);
            if (appWin.Count == 0)
            {
                _toast.Error("自动化套件", "AppWin 至少需要一个可执行文件");
                return null;
            }

            var colSpecs = ParseLineItems(AgentColSpecsItems);
            if (colSpecs.Count == 0)
            {
                _toast.Error("自动化套件", "ColSpecs 不能为空");
                return null;
            }

            var intCols = ParseLineItems(AgentIntColsItems);

            if (AgentConfirmTimeoutMs is < 100 or > 10000)
            {
                _toast.Error("自动化套件", "ConfirmTimeoutMs 范围应为 100-10000");
                return null;
            }

            var codePickPolicy = AgentCodePickPolicy.Trim().ToUpperInvariant();
            if (codePickPolicy is not ("MAX_LEVEL" or "MIN_LEVEL"))
            {
                _toast.Error("自动化套件", "CodePickPolicy 仅支持 MAX_LEVEL 或 MIN_LEVEL");
                return null;
            }

            var warehouseAnchors = ParseLineItems(AgentWarehouseAnchorItems);
            var warehouseTaskIdentifier = NormalizeWarehouseTaskIdentifierValue(AgentWarehouseTaskIdentifier);

            return new AgentToolOptions
            {
                PgDriver = AgentPgDriver.Trim(),
                PgSsl = AgentPgSsl.Trim(),
                OptWindowClass = AgentOptWindowClass.Trim(),
                IptWindowClass = AgentIptWindowClass.Trim(),
                ConfirmTimeoutMs = AgentConfirmTimeoutMs,
                OptParseGridClassNN = AgentOptParseGridClassNN.Trim(),
                OptVerifyGridClassNN = AgentOptVerifyGridClassNN.Trim(),
                IptParseGridClassNN = AgentIptParseGridClassNN.Trim(),
                IptVerifyGridClassNN = AgentIptVerifyGridClassNN.Trim(),
                OptInputClassNN = AgentOptInputClassNN.Trim(),
                IptInputClassNN = AgentIptInputClassNN.Trim(),
                WarehouseEnabled = AgentWarehouseEnabled,
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
            LogError("tools.agent_options.parse_fail", "Failed to parse agent options", ex);
            _toast.Error("自动化套件", $"Agent 配置格式错误：{ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    private void AddAgentAppWinItem() => AgentAppWinItems.Add(new AgentLineItem());

    [RelayCommand]
    private void RemoveAgentAppWinItem(AgentLineItem? item)
    {
        if (item is null) return;
        AgentAppWinItems.Remove(item);
    }

    [RelayCommand]
    private void AddAgentColSpecsItem() => AgentColSpecsItems.Add(new AgentLineItem());

    [RelayCommand]
    private void RemoveAgentColSpecsItem(AgentLineItem? item)
    {
        if (item is null) return;
        AgentColSpecsItems.Remove(item);
    }

    [RelayCommand]
    private void AddAgentIntColsItem() => AgentIntColsItems.Add(new AgentLineItem());

    [RelayCommand]
    private void RemoveAgentIntColsItem(AgentLineItem? item)
    {
        if (item is null) return;
        AgentIntColsItems.Remove(item);
    }

    [RelayCommand]
    private void AddAgentWarehouseAnchorItem() => AgentWarehouseAnchorItems.Add(new AgentLineItem());

    [RelayCommand]
    private void RemoveAgentWarehouseAnchorItem(AgentLineItem? item)
    {
        if (item is null) return;
        AgentWarehouseAnchorItems.Remove(item);
    }

    private static Dictionary<string, int> ParseAppWinItems(IEnumerable<AgentLineItem> items)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in ParseLineItems(items))
            result[v] = 1;
        return result;
    }

    private static List<string> ParseLineItems(IEnumerable<AgentLineItem> items)
        => items.Select(x => (x.Value).Trim()).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> SnapshotLineItems(IEnumerable<AgentLineItem> items)
        => items.Select(x => (x.Value).Trim()).ToList();

    private static void ResetLineItems(ObservableCollection<AgentLineItem> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values
                     .Select(x => x.Trim())
                     .Where(x => x != "")
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            target.Add(new AgentLineItem(value));
    }

    private ToolEditorSnapshot? BuildCurrentSnapshot()
    {
        try
        {
            return new ToolEditorSnapshot(
                AhkExecutablePath.Trim(),
                AhkProcessName.Trim(),
                AgentPgDriver.Trim(),
                AgentPgSsl.Trim(),
                AgentOptWindowClass.Trim(),
                AgentIptWindowClass.Trim(),
                AgentConfirmTimeoutMs,
                AgentOptParseGridClassNN.Trim(),
                AgentOptVerifyGridClassNN.Trim(),
                AgentIptParseGridClassNN.Trim(),
                AgentIptVerifyGridClassNN.Trim(),
                AgentOptInputClassNN.Trim(),
                AgentIptInputClassNN.Trim(),
                AgentWarehouseEnabled,
                NormalizeWarehouseTaskIdentifierValue(AgentWarehouseTaskIdentifier),
                AgentCodePickPolicy.Trim().ToUpperInvariant(),
                SnapshotLineItems(AgentAppWinItems),
                SnapshotLineItems(AgentColSpecsItems),
                SnapshotLineItems(AgentIntColsItems),
                SnapshotLineItems(AgentWarehouseAnchorItems));
        }
        catch
        {
            return null;
        }
    }

    private static bool SnapshotEquals(ToolEditorSnapshot left, ToolEditorSnapshot right)
    {
        if (!string.Equals(left.AhkExecutablePath, right.AhkExecutablePath, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AhkProcessName, right.AhkProcessName, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentPgDriver, right.AgentPgDriver, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentPgSsl, right.AgentPgSsl, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentOptWindowClass, right.AgentOptWindowClass, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentIptWindowClass, right.AgentIptWindowClass, StringComparison.Ordinal))
            return false;
        if (left.AgentConfirmTimeoutMs != right.AgentConfirmTimeoutMs)
            return false;
        if (!string.Equals(left.AgentOptParseGridClassNN, right.AgentOptParseGridClassNN, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentOptVerifyGridClassNN, right.AgentOptVerifyGridClassNN, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentIptParseGridClassNN, right.AgentIptParseGridClassNN, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentIptVerifyGridClassNN, right.AgentIptVerifyGridClassNN, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentOptInputClassNN, right.AgentOptInputClassNN, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentIptInputClassNN, right.AgentIptInputClassNN, StringComparison.Ordinal))
            return false;
        if (left.AgentWarehouseEnabled != right.AgentWarehouseEnabled)
            return false;
        if (!string.Equals(left.AgentWarehouseTaskIdentifier, right.AgentWarehouseTaskIdentifier, StringComparison.Ordinal))
            return false;
        if (!string.Equals(left.AgentCodePickPolicy, right.AgentCodePickPolicy, StringComparison.Ordinal))
            return false;
        if (!left.AgentAppWinItems.SequenceEqual(right.AgentAppWinItems, StringComparer.Ordinal))
            return false;
        if (!left.AgentColSpecsItems.SequenceEqual(right.AgentColSpecsItems, StringComparer.Ordinal))
            return false;
        if (!left.AgentIntColsItems.SequenceEqual(right.AgentIntColsItems, StringComparer.Ordinal))
            return false;
        if (!left.AgentWarehouseAnchorItems.SequenceEqual(right.AgentWarehouseAnchorItems, StringComparer.Ordinal))
            return false;

        return true;
    }

    private static string ResolveProgramVersionText(string? runtimeVersion, string? manifestAgentVersion)
    {
        var runtime = NormalizeVersionText(runtimeVersion);
        if (runtime is not null)
            return runtime;

        var manifest = NormalizeVersionText(manifestAgentVersion);
        return manifest ?? "未知";
    }

    private static string? NormalizeVersionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value.Trim();
        return v is "未知" or "未配置" ? null : v;
    }

    public override void Dispose()
    {
        try { _ahkRuntime.StatusChanged -= OnAhkRuntimeChanged; }
        catch (Exception ex)
        {
            LogWarn("tools.dispose.runtime_unsub_fail", "Failed to unsubscribe runtime status", ex);
        }

        try { AgentAppWinItems.CollectionChanged -= OnAgentLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("tools.dispose.appwin_collection_unsub_fail", "Failed to unsubscribe AgentAppWinItems", ex);
        }

        try { AgentColSpecsItems.CollectionChanged -= OnAgentLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("tools.dispose.colspecs_collection_unsub_fail", "Failed to unsubscribe AgentColSpecsItems", ex);
        }

        try { AgentIntColsItems.CollectionChanged -= OnAgentLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("tools.dispose.intcols_collection_unsub_fail", "Failed to unsubscribe AgentIntColsItems", ex);
        }

        try { AgentWarehouseAnchorItems.CollectionChanged -= OnAgentLineCollectionChanged; }
        catch (Exception ex)
        {
            LogWarn("tools.dispose.warehouse_anchors_collection_unsub_fail", "Failed to unsubscribe AgentWarehouseAnchorItems", ex);
        }

        foreach (var item in AgentAppWinItems)
        {
            try { item.PropertyChanged -= OnAgentLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("tools.dispose.appwin_item_unsub_fail", "Failed to unsubscribe AgentAppWin item", ex);
            }
        }

        foreach (var item in AgentColSpecsItems)
        {
            try { item.PropertyChanged -= OnAgentLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("tools.dispose.colspecs_item_unsub_fail", "Failed to unsubscribe AgentColSpecs item", ex);
            }
        }

        foreach (var item in AgentIntColsItems)
        {
            try { item.PropertyChanged -= OnAgentLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("tools.dispose.intcols_item_unsub_fail", "Failed to unsubscribe AgentIntCols item", ex);
            }
        }

        foreach (var item in AgentWarehouseAnchorItems)
        {
            try { item.PropertyChanged -= OnAgentLineItemPropertyChanged; }
            catch (Exception ex)
            {
                LogWarn("tools.dispose.warehouse_anchor_item_unsub_fail", "Failed to unsubscribe AgentWarehouseAnchor item", ex);
            }
        }

        base.Dispose();
    }

    private sealed record ToolEditorSnapshot(
        string AhkExecutablePath,
        string AhkProcessName,
        string AgentPgDriver,
        string AgentPgSsl,
        string AgentOptWindowClass,
        string AgentIptWindowClass,
        int AgentConfirmTimeoutMs,
        string AgentOptParseGridClassNN,
        string AgentOptVerifyGridClassNN,
        string AgentIptParseGridClassNN,
        string AgentIptVerifyGridClassNN,
        string AgentOptInputClassNN,
        string AgentIptInputClassNN,
        bool AgentWarehouseEnabled,
        string AgentWarehouseTaskIdentifier,
        string AgentCodePickPolicy,
        IReadOnlyList<string> AgentAppWinItems,
        IReadOnlyList<string> AgentColSpecsItems,
        IReadOnlyList<string> AgentIntColsItems,
        IReadOnlyList<string> AgentWarehouseAnchorItems);
}
