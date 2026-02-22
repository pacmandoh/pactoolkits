using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.DataAccess;
using pactoolkits_ui.Services;
using System.Windows.Input;

namespace pactoolkits_ui.ViewModels.Pages;

public interface ISettingsPage { }

public partial class SettingsViewModel : AppPageBase, ISettingsPage
{
    public override MaterialIconKind Icon => MaterialIconKind.Settings;
    public override int Index => 999;
    public override string DisplayName => "设置";
    public override bool ShowInSidebar => false;
    public override ICommand? RefreshCommand => null;

    private readonly IDbConfigService _svc;
    private readonly IDbConnectionTester _tester;
    private readonly IToastService _toast;
    private readonly IClientAliasService _alias;
    private readonly IClientIdReadRepo _clientRepo;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IUiBehaviorService _uiBehavior;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IAppUpdateService _updates;
    private readonly IUpdateUiFlowService _updateUiFlow;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IDialogService _dialog;
    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IDbSchemaMigrationService _dbSchemaMigration;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IAppLogger _logger;
    private readonly IClipboardService _clipboard;
    private readonly HashSet<ClientAliasRow> _trackedAliasRows = new();
    private bool _syncingUiBehavior;
    private bool _syncingUpdateOptions;
    private bool _syncingLoggingOptions;

    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _database;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password;

    [ObservableProperty] private string? _status;

    [ObservableProperty] private bool _isDbConnected;
    [ObservableProperty] private bool _isClientAliasRefreshing;
    [ObservableProperty] private bool _canSaveClientAliases;
    [ObservableProperty] private string _clientAliasHint = "加载中…";
    [ObservableProperty] private int _traceCodeRequiredLength = 20;
    [ObservableProperty] private string _traceCodePattern = "^8\\d+$";
    [ObservableProperty] private string _traceCodeRuleHint = "默认：长度 20，正则 ^8\\d+$";
    [ObservableProperty] private bool _minimizeToTrayOnClose = true;
    [ObservableProperty] private bool _autoCheckUpdateOnStartup = true;
    [ObservableProperty] private string _updateChannel = "stable";
    [ObservableProperty] private string _updateFeedUrl = string.Empty;
    [ObservableProperty] private int _updatePollIntervalMinutes;
    [ObservableProperty] private string _updatePollIntervalHint = "0=通道默认";
    [ObservableProperty] private string _ignoredUiVersion = string.Empty;
    [ObservableProperty] private string _currentSuiteVersion = "unknown";
    [ObservableProperty] private bool? _suiteUpdateAvailable;
    [ObservableProperty] private string _latestSuiteVersion = "unknown";
    [ObservableProperty] private string _updateStatusHint = "未检查更新";
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _isUpdateApplying;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private bool _loggingEnabled = true;
    [ObservableProperty] private string _loggingMinimumLevel = "Error";
    [ObservableProperty] private int _loggingRetentionDays = 14;
    [ObservableProperty] private int _loggingMaxFileSizeMb = 20;
    [ObservableProperty] private string _loggingDirectory = string.Empty;
    [ObservableProperty] private string _loggingStatusHint = "日志系统已启用";
    [ObservableProperty] private bool _isLoggingBusy;
    [ObservableProperty] private string _dbSchemaCurrentVersion = "unknown";
    [ObservableProperty] private string _dbSchemaRequiredMinVersion = "unknown";
    [ObservableProperty] private string _dbSchemaStatusText = "未检查";
    [ObservableProperty] private bool _isDbSchemaSatisfied;
    [ObservableProperty] private bool _isDbSchemaChecking;
    [ObservableProperty] private bool _isDbSchemaFailed;
    [ObservableProperty] private string _dbSchemaErrorText = string.Empty;
    [ObservableProperty] private bool? _dbSchemaBadgeStatus;
    [ObservableProperty] private string _dbSchemaBadgeLabel = "未检查";
    [ObservableProperty] private string _dbSchemaLastCheckedAtText = "--";
    [ObservableProperty] private string _dbSchemaLastCheckSourceText = "--";
    [ObservableProperty] private string _dbSchemaLastMigrationText = "尚无迁移记录";
    [ObservableProperty] private string _dbSchemaPolicyText = "启动/保存连接时强制迁移，统一写入 public schema";

    [ObservableProperty] private bool _isClientAliasEditMode;
    [ObservableProperty] private bool _isClientAliasReadOnly = true;
    public bool CanCopyDbSchemaDiagnostics => !string.IsNullOrWhiteSpace(BuildDbSchemaDiagnosticsText());
    public ObservableCollection<string> LoggingLevelOptions { get; } = new()
    {
        "Debug",
        "Info",
        "Warn",
        "Error",
        "Fatal"
    };
    public ObservableCollection<string> UpdateChannelOptions { get; } = new()
    {
        "stable",
        "beta",
        "dev"
    };

    public ObservableCollection<ClientAliasRow> ClientAliases { get; } = new();
    public bool IsClientAliasesEmpty => ClientAliases.Count == 0;
    public string SuiteUpdateAvailabilityLabel => GetAvailabilityLabel(SuiteUpdateAvailable);
    public string LoggingMinimumLevelHint => LoggingMinimumLevel switch
    {
        "Debug" => "记录最详细调试信息，适合临时排障。",
        "Info" => "记录关键流程信息，便于常规回溯。",
        "Warn" => "仅记录异常征兆与潜在问题。",
        "Error" => "仅记录错误与失败，推荐日常运行。",
        "Fatal" => "仅记录致命故障，最小日志开销。",
        _ => "日志级别未识别，将使用 Error。"
    };
    public string DbSchemaStatusBadgeText => DbSchemaStatusText;
    public SettingsViewModel(
        IDbConfigService svc,
        IDbConnectionTester tester,
        IToastService toast,
        IClientAliasService alias,
        ITraceCodeRuleService traceCodeRule,
        IUiBehaviorService uiBehavior,
        IUpdateSettingsService updateSettings,
        IAppUpdateService updates,
        IUpdateUiFlowService updateUiFlow,
        IReleaseVersionService releaseVersion,
        IDialogService dialog,
        IDbSchemaVersionService dbSchemaVersion,
        IDbSchemaMigrationService dbSchemaMigration,
        ILoggingSettingsService loggingSettings,
        IAppLogger logger,
        IClipboardService clipboard,
        IClientIdReadRepo clientRepo)
    {
        _svc = svc;
        _tester = tester;
        _toast = toast;
        _alias = alias;
        _traceCodeRule = traceCodeRule;
        _uiBehavior = uiBehavior;
        _updateSettings = updateSettings;
        _updates = updates;
        _updateUiFlow = updateUiFlow;
        _releaseVersion = releaseVersion;
        _dialog = dialog;
        _dbSchemaVersion = dbSchemaVersion;
        _dbSchemaMigration = dbSchemaMigration;
        _loggingSettings = loggingSettings;
        _logger = logger;
        _clipboard = clipboard;
        _clientRepo = clientRepo;
        ClientAliases.CollectionChanged += OnClientAliasesChanged;
        var c = svc.Current;
        _host = c.Host;
        _port = c.Port;
        _database = c.Database;
        _username = c.Username;
        _password = c.Password;

        IsClientAliasEditMode = false;
        IsClientAliasReadOnly = true;

        LoadAliasesOnly();
        UpdateClientAliasUiState();

        _ = ReloadClientAliasesAsync();

        LoadTraceCodeRule();
        LoadUiBehavior();
        LoadUpdateOptions();
        LoadLoggingOptions();
        _ = RefreshDbSchemaStatusAsync("startup", manualProbe: false);
        _uiBehavior.Changed += OnUiBehaviorChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;
        _updates.Changed += OnUpdatesChanged;
        _loggingSettings.Changed += OnLoggingSettingsChanged;

    }

    private void OnClientAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsClientAliasesEmpty));

    private void LoadUiBehavior()
    {
        var ui = _uiBehavior.Current;
        MinimizeToTrayOnClose = ui.MinimizeToTrayOnClose;
    }

    private void LoadUpdateOptions()
    {
        _syncingUpdateOptions = true;

        var options = _updateSettings.Current;
        AutoCheckUpdateOnStartup = options.AutoCheckOnStartup;
        UpdateChannel = options.Channel;
        UpdateFeedUrl = options.FeedUrl;
        UpdatePollIntervalMinutes = options.AutoCheckIntervalMinutes;
        IgnoredUiVersion = options.IgnoredVersion;
        RefreshUpdatePollIntervalHint();

        SyncUpdateStateFromService();

        _syncingUpdateOptions = false;
    }

    private void OnUiBehaviorChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var ui = _uiBehavior.Current;
            _syncingUiBehavior = true;
            MinimizeToTrayOnClose = ui.MinimizeToTrayOnClose;
            _syncingUiBehavior = false;
        });
    }

    private void OnUpdateSettingsChanged()
    {
        Dispatcher.UIThread.Post(LoadUpdateOptions);
    }

    private void OnUpdatesChanged()
    {
        Dispatcher.UIThread.Post(SyncUpdateStateFromService);
    }

    private void LoadLoggingOptions()
    {
        _syncingLoggingOptions = true;
        var options = _loggingSettings.Current;
        LoggingEnabled = options.Enabled;
        LoggingMinimumLevel = options.MinimumLevel;
        LoggingRetentionDays = options.RetentionDays;
        LoggingMaxFileSizeMb = options.MaxFileSizeMb;
        LoggingDirectory = _logger.LogDirectory;
        LoggingStatusHint = options.Enabled
            ? $"已启用（{options.MinimumLevel}）"
            : "已禁用";
        _syncingLoggingOptions = false;
    }

    private void OnLoggingSettingsChanged()
    {
        Dispatcher.UIThread.Post(LoadLoggingOptions);
    }

    partial void OnSuiteUpdateAvailableChanged(bool? value)
        => OnPropertyChanged(nameof(SuiteUpdateAvailabilityLabel));

    private void SyncUpdateStateFromService()
    {
        CurrentSuiteVersion = _updates.CurrentVersion;
        LatestSuiteVersion = _updates.LatestVersion;
        SuiteUpdateAvailable = _updates.HasSuiteUpdateAvailable;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;
        UpdateStatusHint = _updates.LastMessage;
    }

    partial void OnLoggingMinimumLevelChanged(string value)
        => OnPropertyChanged(nameof(LoggingMinimumLevelHint));

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (_syncingUiBehavior)
            return;

        _ = SaveUiBehaviorImmediateAsync(value);
    }

    partial void OnUpdateChannelChanged(string value)
        => RefreshUpdatePollIntervalHint();

    partial void OnUpdatePollIntervalMinutesChanged(int value)
        => RefreshUpdatePollIntervalHint();

    private void RefreshUpdatePollIntervalHint()
    {
        var channel = (UpdateChannel ?? string.Empty).Trim();
        var defaultMinutes = string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) ? 30 : 10;

        if (UpdatePollIntervalMinutes <= 0)
            UpdatePollIntervalHint = $"默认：{channel} {defaultMinutes} 分钟";
        else
            UpdatePollIntervalHint = $"自定义：{Math.Clamp(UpdatePollIntervalMinutes, 1, 720)} 分钟";
    }

    private void LoadTraceCodeRule()
    {
        var rule = _traceCodeRule.Current;
        TraceCodeRequiredLength = rule.RequiredLength;
        TraceCodePattern = rule.Pattern;
        TraceCodeRuleHint = $"当前：长度 {rule.RequiredLength}，正则 {rule.Pattern}";
    }

    private void LoadAliasesOnly()
    {
        UntrackAllAliasRows();
        ClientAliases.Clear();
        foreach (var kv in NormalizeAliasMapByMachine(_alias.GetAll()).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var row = new ClientAliasRow(kv.Key, kv.Value);
            ClientAliases.Add(row);
            TrackAliasRow(row);
        }
    }

    [RelayCommand]
    private void StartEditClientAliases()
    {
        if (ShouldSkipTrigger())
            return;

        IsClientAliasEditMode = true;
        IsClientAliasReadOnly = false;
        UpdateClientAliasUiState();
        _ = ReloadClientAliasesAsync();
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (ShouldSkipTrigger())
            return;

        IsBusy = true;
        Status = null;

        try
        {
            var opt = ToOptions();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            var result = await _tester.TestAsync(opt, cts.Token);

            if (result.Ok)
            {
                await EnsureDbSchemaUpToDateAsync().ConfigureAwait(false);
                if (!await EnsureDbSchemaCompatibleAsync().ConfigureAwait(false))
                {
                    Status = "数据库版本不兼容";
                    IsDbConnected = false;
                    UpdateClientAliasUiState();
                    return;
                }

                Status = "连接成功";
                IsDbConnected = true;
                _ = ReloadClientAliasesAsync();
                _toast.Success("数据库连接", "连接成功");
            }
            else
            {
                Status = result.Summary;
                IsDbConnected = false;
                UpdateClientAliasUiState();
                _toast.Error("数据库连接失败", result.Summary);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("SettingsVM", "db.test.timeout", "DB connection test timed out");
            Status = "连接超时";
            IsDbConnected = false;
            UpdateClientAliasUiState();
            _toast.Error("数据库连接失败", "连接超时：请检查网络/主机/端口");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ShouldSkipTrigger())
            return;

        IsBusy = true;

        try
        {
            await _svc.SaveAndApplyAsync(ToOptions());
            await EnsureDbSchemaUpToDateAsync().ConfigureAwait(false);
            if (!await EnsureDbSchemaCompatibleAsync().ConfigureAwait(false))
            {
                Status = "数据库版本不兼容";
                IsDbConnected = false;
                UpdateClientAliasUiState();
                return;
            }

            Status = "连接成功";
            IsDbConnected = true;
            _toast.Success("配置已保存", "数据库配置已应用");

            _ = ReloadClientAliasesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "db.save.fail", "Failed to save DB settings", ex);
            IsDbConnected = false;
            UpdateClientAliasUiState();
            _toast.Error("保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckDbSchemaStatusAsync()
    {
        if (ShouldSkipTrigger() || IsDbSchemaChecking)
            return;

        await RefreshDbSchemaStatusAsync("manual_check", manualProbe: true).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task CopyDbSchemaDiagnosticsAsync()
    {
        if (ShouldSkipTrigger())
            return;

        var text = BuildDbSchemaDiagnosticsText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _toast.Warn("数据库结构更新", "当前无可复制的诊断信息");
            return;
        }

        await _clipboard.SetTextAsync(text).ConfigureAwait(false);
        _toast.Success("数据库结构更新", "已复制诊断信息");
    }

    private PgOptions ToOptions() => new()
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password
    };

    [RelayCommand]
    private async Task ReloadClientAliasesAsync()
    {
        if (IsClientAliasRefreshing || ShouldSkipTrigger())
            return;

        IsClientAliasRefreshing = true;
        try
        {
            var opt = ToOptions();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            HashSet<string> clients;
            try
            {
                clients = await _clientRepo.GetDistinctClientIdsAsync(opt, cts.Token);
                IsDbConnected = true;
            }
            catch
            {
                clients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IsDbConnected = false;
            }

            var aliasMap = NormalizeAliasMapByMachine(_alias.GetAll());
            var clientMachines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in clients)
            {
                var machine = ExtractMachine(raw);
                if (machine.Length == 0) continue;
                clientMachines.Add(machine);
            }

            UntrackAllAliasRows();
            ClientAliases.Clear();

            foreach (var key in clientMachines.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                aliasMap.TryGetValue(key, out var a);
                var row = new ClientAliasRow(key, a ?? string.Empty);
                ClientAliases.Add(row);
                TrackAliasRow(row);
            }

            foreach (var kv in aliasMap.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (clientMachines.Contains(kv.Key)) continue;
                var row = new ClientAliasRow(kv.Key, kv.Value);
                ClientAliases.Add(row);
                TrackAliasRow(row);
            }

            UpdateClientAliasUiState();
        }
        finally
        {
            IsClientAliasRefreshing = false;
        }
    }

    private void UpdateClientAliasUiState()
    {
        if (!IsClientAliasEditMode)
        {
            ClientAliasHint = ClientAliases.Count == 0
                ? "未找到任何机器标识，点击‘开始编辑’后可编辑别名"
                : "已加载机器标识/本地别名，点击‘开始编辑’后可编辑";

            CanSaveClientAliases = false;
            IsClientAliasReadOnly = true;
            return;
        }

        IsClientAliasReadOnly = false;

        if (!IsDbConnected)
        {
            ClientAliasHint = ClientAliases.Count == 0
                ? "未连接数据库：无法读取客户端列表，仍可编辑/保存本地别名；连接后可自动补全列表"
                : "未连接数据库：当前显示本地别名；连接后可自动补全客户端列表";
        }
        else
        {
            ClientAliasHint = ClientAliases.Count == 0
                ? "已连接，但暂无可用机器标识"
                : "在右侧填写别名，留空表示使用机器标识";
        }

        CanSaveClientAliases = true;
    }

    private void RemoveClientAlias(ClientAliasRow? row)
    {
        if (row is null) return;
        if (IsClientAliasReadOnly) return;

        ClientAliases.Remove(row);
        UntrackAliasRow(row);
        UpdateClientAliasUiState();
    }

    [RelayCommand]
    private async Task SaveClientAliasesAsync()
    {
        if (ShouldSkipTrigger())
            return;

        if (!IsClientAliasEditMode)
        {
            _toast.Error("客户端别名", "请先点击‘开始编辑’");
            return;
        }

        UpdateClientAliasUiState();

        IsBusy = true;
        try
        {
            var items = ClientAliases
                .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
                .Select(x => new KeyValuePair<string, string>(x.Machine, x.Alias));
            _alias.ReplaceAll(items);

            IsClientAliasEditMode = false;
            IsClientAliasReadOnly = true;

            Status = "客户端别名已保存";
            _toast.Success("客户端别名", "已保存并生效");
            await Task.Delay(600);
            Status = null;

            _ = ReloadClientAliasesAsync();
            UpdateClientAliasUiState();
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "client_alias.save.fail", "Failed to save client aliases", ex);
            _toast.Error("保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveTraceCodeRuleAsync()
    {
        if (ShouldSkipTrigger())
            return;

        if (TraceCodeRequiredLength <= 0)
        {
            _toast.Error("追溯码规则", "长度必须大于 0");
            return;
        }

        if (string.IsNullOrWhiteSpace(TraceCodePattern))
        {
            _toast.Error("追溯码规则", "正则表达式不能为空");
            return;
        }

        try
        {
            _ = Regex.IsMatch(string.Empty, TraceCodePattern);
        }
        catch (Exception ex)
        {
            _logger.Warn("SettingsVM", "trace_rule.regex_invalid", "Invalid trace regex pattern", ex, new { TraceCodePattern });
            _toast.Error("追溯码规则", $"正则格式错误：{ex.Message}");
            return;
        }

        IsBusy = true;
        try
        {
            await _traceCodeRule.SaveAsync(new TraceCodeValidationOptions
            {
                RequiredLength = TraceCodeRequiredLength,
                Pattern = TraceCodePattern
            });

            TraceCodeRuleHint = $"当前：长度 {TraceCodeRequiredLength}，正则 {TraceCodePattern}";
            _toast.Success("追溯码规则", "规则已保存并生效");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "trace_rule.save.fail", "Failed to save trace code rule", ex);
            _toast.Error("追溯码规则保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveUiBehaviorImmediateAsync(bool value)
    {
        try
        {
            await _uiBehavior.SaveAsync(new UiBehaviorOptions
            {
                MinimizeToTrayOnClose = value
            });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "ui_behavior.save.fail", "Failed to save UI behavior", ex);
            _syncingUiBehavior = true;
            MinimizeToTrayOnClose = _uiBehavior.Current.MinimizeToTrayOnClose;
            _syncingUiBehavior = false;
            _toast.Error("界面行为保存失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveUpdateOptionsAsync()
    {
        if (_syncingUpdateOptions || ShouldSkipTrigger())
            return;

        IsBusy = true;
        try
        {
            var options = new UpdateOptions
            {
                AutoCheckOnStartup = AutoCheckUpdateOnStartup,
                Channel = UpdateChannel,
                FeedUrl = UpdateFeedUrl,
                AutoCheckIntervalMinutes = Math.Clamp(UpdatePollIntervalMinutes, 0, 720),
                IgnoredVersion = IgnoredUiVersion
            };

            await _updateSettings.SaveAsync(options).ConfigureAwait(false);
            _toast.Success("更新设置", "更新配置已保存");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.settings.save.fail", "Failed to save update settings", ex);
            _toast.Error("更新设置保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveLoggingOptionsAsync()
    {
        if (_syncingLoggingOptions || IsLoggingBusy || ShouldSkipTrigger())
            return;

        IsLoggingBusy = true;
        try
        {
            var options = new LoggingOptions
            {
                Enabled = LoggingEnabled,
                MinimumLevel = LoggingMinimumLevel,
                RetentionDays = LoggingRetentionDays,
                MaxFileSizeMb = LoggingMaxFileSizeMb,
                LogDirectory = LoggingDirectory
            };

            await _loggingSettings.SaveAsync(options).ConfigureAwait(false);
            LoggingDirectory = _logger.LogDirectory;
            LoggingStatusHint = LoggingEnabled ? $"已启用（{LoggingMinimumLevel}）" : "已禁用";
            _toast.Success("日志设置", "日志配置已保存");
            _logger.Info("SettingsVM", "logging.settings.saved", "Logging settings updated", new
            {
                options.Enabled,
                options.MinimumLevel,
                options.RetentionDays,
                options.MaxFileSizeMb,
                LogDirectory = _logger.LogDirectory
            });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.settings.save_fail", "Failed to save logging settings", ex);
            _toast.Error("日志设置保存失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenLogDirectoryAsync()
    {
        if (IsLoggingBusy || ShouldSkipTrigger())
            return Task.CompletedTask;

        IsLoggingBusy = true;
        try
        {
            var dir = _logger.LogDirectory;
            Directory.CreateDirectory(dir);
            OpenDirectory(dir);
            LoggingStatusHint = $"已打开：{dir}";
            _logger.Info("SettingsVM", "logging.open_dir", "Opened log directory", new { dir });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.open_dir_fail", "Failed to open log directory", ex);
            _toast.Error("打开日志目录失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }

        return Task.CompletedTask;
    }

    private static void OpenDirectory(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { dir },
                UseShellExecute = false
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { dir },
                UseShellExecute = false
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { dir },
            UseShellExecute = false
        });
    }

    [RelayCommand]
    private async Task CopyCurrentLogPathAsync()
    {
        if (IsLoggingBusy || ShouldSkipTrigger())
            return;

        IsLoggingBusy = true;
        try
        {
            var path = _logger.CurrentLogPath;
            await _clipboard.SetTextAsync(path).ConfigureAwait(false);
            LoggingStatusHint = $"已复制：{path}";
            _toast.Success("日志", "当前日志路径已复制到剪贴板");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.copy_path_fail", "Failed to copy log path", ex);
            _toast.Error("复制日志路径失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportRecentLogsAsync()
    {
        if (IsLoggingBusy || ShouldSkipTrigger())
            return;

        IsLoggingBusy = true;
        try
        {
            var path = await _logger.ExportRecentAsync(TimeSpan.FromHours(24)).ConfigureAwait(false);
            await _clipboard.SetTextAsync(path).ConfigureAwait(false);
            LoggingStatusHint = $"已导出：{path}";
            _toast.Success("日志导出", "最近24小时日志已导出并复制路径");
            _logger.Info("SettingsVM", "logging.export_recent", "Exported recent logs", new { path, window = "24h" });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.export_recent_fail", "Failed to export recent logs", ex);
            _toast.Error("日志导出失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (_updates.IsChecking || IsUpdateApplying || ShouldSkipTrigger())
            return;

        await _updateUiFlow.CheckAndHandleAsync(
            showNoUpdateToast: true,
            startupMode: false,
            applyNowAction: ApplyUpdateNowAsync,
            ignoreVersionAction: IgnoreCurrentUpdateAsync,
            logScope: "SettingsVM").ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ApplyUpdateNowAsync()
    {
        if (IsUpdateChecking || IsUpdateApplying || ShouldSkipTrigger())
            return;

        IsUpdateApplying = true;
        try
        {
            await _updateUiFlow.ApplyUpdateFlowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.apply.fail", "Failed to apply update", ex);
            _toast.Error("应用更新", ex.Message);
        }
        finally
        {
            IsUpdateApplying = false;
        }
    }

    [RelayCommand]
    private async Task ClearIgnoredVersionAsync()
    {
        if (ShouldSkipTrigger())
            return;

        try
        {
            IgnoredUiVersion = string.Empty;
            await _updateSettings.SaveIgnoredVersionAsync(string.Empty).ConfigureAwait(false);
            _toast.Success("更新设置", "已清除忽略版本");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.clear_ignored.fail", "Failed to clear ignored version", ex);
            _toast.Error("更新设置", ex.Message);
        }
    }

    private async Task IgnoreCurrentUpdateAsync()
    {
        try
        {
            await _updateUiFlow.IgnoreVersionAsync(LatestSuiteVersion).ConfigureAwait(false);
            IgnoredUiVersion = LatestSuiteVersion;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.ignore.fail", "Failed to ignore update version", ex);
            _toast.Error("应用更新", ex.Message);
        }
    }

    private async Task<bool> EnsureDbSchemaCompatibleAsync()
    {
        var version = _releaseVersion.Current;
        var uiMin = DbSchemaCompat.NormalizeBound(version.UiMinDbSchema, version.DbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(version.AgentMinDbSchema, version.DbSchemaVersion);
        var requiredMin = GetRequiredMinSchemaVersion();

        var schema = await _dbSchemaVersion.TryReadSchemaVersionAsync(CancellationToken.None).ConfigureAwait(false);
        var db = schema.value ?? string.Empty;
        var uiOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, uiMin);
        var agentOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, agentMin);
        await RefreshDbSchemaStatusAsync("compat_check", manualProbe: false, cachedSchema: schema).ConfigureAwait(false);
        if (uiOk && agentOk)
            return true;

        var message = DbSchemaCompat.BuildIncompatibleMessage(
            schema.ok,
            schema.value,
            schema.reason,
            uiMin,
            agentMin,
            requiredMin);

        _logger.Warn("SettingsVM", "db.schema.incompatible", "Database schema incompatible when connecting", null, new
        {
            uiMin,
            agentMin,
            schemaOk = schema.ok,
            schemaValue = schema.value,
            schema.reason,
            uiOk,
            agentOk
        });

        await _dialog.Warn(DbSchemaCompat.GetIncompatibleTitle(), message).ConfigureAwait(false);
        return false;
    }

    private async Task EnsureDbSchemaUpToDateAsync()
    {
        SetDbSchemaStatus("更新中", checking: true, failed: false, error: null);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var migration = await _dbSchemaMigration.EnsureUpToDateAsync(cts.Token).ConfigureAwait(false);
            DbSchemaLastMigrationText = $"before={migration.BeforeVersion ?? "unknown"} -> after={migration.AfterVersion ?? "unknown"}（applied={migration.AppliedCount}, skipped={migration.SkippedCount}）";
            if (migration.HasChanges)
            {
                _toast.Success("数据库结构更新", $"已应用 {migration.AppliedCount} 个迁移，当前版本 {migration.AfterVersion ?? "unknown"}");
            }
            await RefreshDbSchemaStatusAsync("migrate_done", manualProbe: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetDbSchemaStatus("更新失败", checking: false, failed: true, error: ex.Message);
            DbSchemaLastMigrationText = $"迁移失败：{ex.Message}";
            OnPropertyChanged(nameof(CanCopyDbSchemaDiagnostics));
            throw;
        }
    }

    private async Task RefreshDbSchemaStatusAsync(
        string source,
        bool manualProbe,
        (bool ok, string? value, string? reason)? cachedSchema = null)
    {
        if (IsDbSchemaChecking && manualProbe)
            return;

        if (manualProbe)
            SetDbSchemaStatus("更新中", checking: true, failed: false, error: null);

        try
        {
            var schema = cachedSchema ?? await _dbSchemaVersion.TryReadSchemaVersionAsync(CancellationToken.None).ConfigureAwait(false);
            DbSchemaLastCheckedAtText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DbSchemaLastCheckSourceText = MapDbSchemaCheckSource(source);
            var requiredMin = GetRequiredMinSchemaVersion();
            DbSchemaRequiredMinVersion = requiredMin;
            DbSchemaCurrentVersion = schema.value ?? "unknown";

            if (!schema.ok)
            {
                SetDbSchemaStatus("更新失败", checking: false, failed: true, error: schema.reason ?? "读取失败");
                if (manualProbe)
                    _toast.Error("数据库结构更新", $"探测失败：{schema.reason ?? "读取失败"}");
                return;
            }

            var satisfied = DbSchemaCompat.IsSemVerAtLeast(schema.value ?? string.Empty, requiredMin);
            IsDbSchemaSatisfied = satisfied;
            SetDbSchemaStatus(satisfied ? "已满足" : "需要更新", checking: false, failed: false, error: satisfied ? null : $"当前版本 {schema.value} 低于最低要求 {requiredMin}");

            if (manualProbe)
            {
                if (satisfied)
                    _toast.Success("数据库结构更新", $"当前版本 {schema.value}，满足最低要求 {requiredMin}");
                else
                    _toast.Warn("数据库结构更新", $"当前版本 {schema.value}，低于最低要求 {requiredMin}");
            }
        }
        finally
        {
            IsDbSchemaChecking = false;
        }
    }

    private static string MapDbSchemaCheckSource(string source)
        => source switch
        {
            "startup" => "应用启动",
            "manual_check" => "手动检查",
            "compat_check" => "兼容性校验",
            "migrate_done" => "迁移完成后回读",
            _ => source
        };

    private string GetRequiredMinSchemaVersion()
    {
        var version = _releaseVersion.Current;
        var uiMin = DbSchemaCompat.NormalizeBound(version.UiMinDbSchema, version.DbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(version.AgentMinDbSchema, version.DbSchemaVersion);
        return DbSchemaCompat.GetRequiredMin(uiMin, agentMin);
    }

    private void SetDbSchemaStatus(string status, bool checking, bool failed, string? error)
    {
        DbSchemaStatusText = status;
        IsDbSchemaChecking = checking;
        IsDbSchemaFailed = failed;
        if (failed || status == "需要更新")
            IsDbSchemaSatisfied = false;
        DbSchemaErrorText = error ?? string.Empty;
        MapDbSchemaBadge(status, checking, failed);
        OnPropertyChanged(nameof(DbSchemaStatusBadgeText));
        OnPropertyChanged(nameof(CanCopyDbSchemaDiagnostics));
    }

    private string BuildDbSchemaDiagnosticsText()
    {
        var lines = new List<string>
        {
            $"status={DbSchemaStatusText}",
            $"current={DbSchemaCurrentVersion}",
            $"required_min={DbSchemaRequiredMinVersion}",
            $"last_checked_at={DbSchemaLastCheckedAtText}",
            $"last_check_source={DbSchemaLastCheckSourceText}",
            $"last_migration={DbSchemaLastMigrationText}",
            $"policy={DbSchemaPolicyText}"
        };

        if (!string.IsNullOrWhiteSpace(DbSchemaErrorText))
            lines.Add($"error={DbSchemaErrorText}");

        return string.Join(Environment.NewLine, lines);
    }

    private void MapDbSchemaBadge(string status, bool checking, bool failed)
    {
        if (checking || string.Equals(status, "更新中", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "更新中";
            return;
        }

        if (failed || string.Equals(status, "更新失败", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "更新失败";
            return;
        }

        if (string.Equals(status, "已满足", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = false;
            DbSchemaBadgeLabel = "已满足";
            return;
        }

        if (string.Equals(status, "需要更新", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = true;
            DbSchemaBadgeLabel = "需要更新";
            return;
        }

        DbSchemaBadgeStatus = null;
        DbSchemaBadgeLabel = string.IsNullOrWhiteSpace(status) ? "未检查" : status;
    }

    private void TrackAliasRow(ClientAliasRow row)
    {
        if (_trackedAliasRows.Add(row))
            row.PropertyChanged += OnClientAliasRowPropertyChanged;
    }

    private void UntrackAliasRow(ClientAliasRow row)
    {
        if (_trackedAliasRows.Remove(row))
            row.PropertyChanged -= OnClientAliasRowPropertyChanged;
    }

    private void UntrackAllAliasRows()
    {
        foreach (var row in _trackedAliasRows.ToArray())
            row.PropertyChanged -= OnClientAliasRowPropertyChanged;
        _trackedAliasRows.Clear();
    }

    private void OnClientAliasRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClientAliasRow.Alias))
            UpdateClientAliasUiState();
    }

    private static Dictionary<string, string> NormalizeAliasMapByMachine(IReadOnlyDictionary<string, string> source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            var machine = ExtractMachine(kv.Key);
            if (machine.Length == 0) continue;

            var alias = (kv.Value ?? string.Empty).Trim();
            if (alias.Length == 0) continue;
            map[machine] = alias;
        }

        return map;
    }

    private static string ExtractMachine(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        var parsed = ClientParser.Parse(text);
        return (parsed.Machine ?? text).Trim();
    }

    private static string GetAvailabilityLabel(bool? value) => value switch
    {
        true => "可更新",
        false => "已最新",
        _ => "未知"
    };

    public override void Dispose()
    {
        try { _uiBehavior.Changed -= OnUiBehaviorChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.ui_behavior_unsub_fail", "Failed to unsubscribe UiBehavior", ex);
        }
        try { _updateSettings.Changed -= OnUpdateSettingsChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.update_settings_unsub_fail", "Failed to unsubscribe UpdateSettings", ex);
        }
        try { _updates.Changed -= OnUpdatesChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.updates_unsub_fail", "Failed to unsubscribe Updates", ex);
        }
        try { _loggingSettings.Changed -= OnLoggingSettingsChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.logging_settings_unsub_fail", "Failed to unsubscribe LoggingSettings", ex);
        }
        ClientAliases.CollectionChanged -= OnClientAliasesChanged;
        base.Dispose();
    }
}
