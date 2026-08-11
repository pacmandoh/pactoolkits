using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private async Task CheckConfigOnStartupAsync()
    {
        if (File.Exists(_configPath))
        {
            return;
        }

        var openSettings = await _dialogs.Confirm(
                "未找到配置文件",
                $"未检测到本地配置文件，请前往 [设置] 完成数据库连接配置后再使用")
            .ConfigureAwait(false);

        if (openSettings)
        {
            await RunOnUiAsync(OpenSettings);
        }
    }

    private void StartConfigWatcher()
    {
        try
        {
            Directory.CreateDirectory(_configDir);

            // 监视 AppData 统一配置（非安装目录）
            // 路径：%AppData%/PacToolkits/PacToolkits.Desktop.config.json
            _configWatcher = new FileSystemWatcher(_configDir, _configFile)
            {
                NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.FileName
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Attributes
            };

            _configWatcher.Changed += OnConfigWatcherChanged;
            _configWatcher.Created += OnConfigWatcherChanged;
            _configWatcher.Renamed += OnConfigWatcherRenamed;
            _configWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.watcher.init_fail", "Failed to initialize config watcher", ex);
        }
    }

    private async Task OnConfigChangedAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_isApplyingConfig)
        {
            return;
        }

        // 编辑器连写会触发 watcher 连发，先 debounce
        await Task.Delay(350).ConfigureAwait(false);

        if (!File.Exists(_configPath))
        {
            return;
        }

        try
        {
            await _agentsManager.SyncConfigAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.agent_sync_fail", "Failed to synchronize agent configuration", ex);
        }

        AppConfigRoot? loaded;
        string? json;
        try
        {
            json = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);

            if (string.Equals(json, _lastSeenConfigJson, StringComparison.Ordinal))
            {
                return;
            }

            loaded = JsonSerializer.Deserialize<AppConfigRoot>(json);
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.watch.read_fail", "Failed to read or parse config for hot-reload", ex);
            return;
        }

        if (loaded is null)
        {
            _logger.Warn("MainWindowVM", "config.watch.parse_null", "Config deserialize returned null; hot-reload skipped");
            return;
        }

        _lastSeenConfigJson = json;

        // Postgres 目标切换仅手动应用；其余安全段落仍可热重载
        if (loaded.Postgres is not null)
        {
            var postgresChanged = !IsSamePgOptions(_settings.AppliedDb, loaded.Postgres);
            if (postgresChanged)
            {
                try
                {
                    _isApplyingConfig = true;
                    _logger.Warn("MainWindowVM", "config.external_db_change_ignored",
                        "Detected external DB config change, ignored until manual apply in Settings", null, new
                        {
                            loaded.Postgres.Host,
                            loaded.Postgres.Port,
                            loaded.Postgres.Database
                        });
                    await RunOnUiAsync(() => _toasts.Warn(
                        "配置文件",
                        "检测到外部数据库配置变更，请在设置页手动保存并测试连接后生效"));
                }
                catch (Exception ex)
                {
                    _logger.Warn("MainWindowVM", "config.external_apply_fail", "Failed to handle external DB config change", ex);
                }
                finally
                {
                    _isApplyingConfig = false;
                }

                _dbMonitor.Signal();
            }
        }

        ApplySafeConfigHotReload(loaded);
    }

    private void ApplySafeConfigHotReload(AppConfigRoot cfg)
    {
        try
        {
            // 同一次反序列化结果按配置区域应用，未变化区域不发布 Changed 事件
            _clientAlias.Apply(cfg.ClientAliases ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            _loggingSettings.Apply(cfg.Logging ?? new LoggingOptions());
            _uiBehavior.Apply(cfg.UiBehavior ?? new UiBehaviorOptions());
            _updateSettings.Apply(cfg.Update ?? new UpdateOptions());
            _traceCodeRule.Apply(cfg.TraceCodeValidation ?? new TraceCodeValidationOptions());
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.safe_hot_reload.fail", "Failed to hot-reload safe config sections", ex);
        }
    }

    private static bool IsSamePgOptions(PgOptions a, PgOptions b)
        => string.Equals(a.Host, b.Host, StringComparison.Ordinal)
           && a.Port == b.Port
           && string.Equals(a.Database, b.Database, StringComparison.Ordinal)
           && string.Equals(a.Username, b.Username, StringComparison.Ordinal)
           && string.Equals(a.Password, b.Password, StringComparison.Ordinal)
           && a.ConnectTimeoutSeconds == b.ConnectTimeoutSeconds
           && a.CommandTimeoutSeconds == b.CommandTimeoutSeconds
           && a.PoolSize == b.PoolSize
           && a.ReconnectIntervalSeconds == b.ReconnectIntervalSeconds
           && a.KeepAliveSeconds == b.KeepAliveSeconds
           && a.MonitorPingSeconds == b.MonitorPingSeconds
           && a.MonitorPingTimeoutSeconds == b.MonitorPingTimeoutSeconds;

    private void OnConfigWatcherChanged(object? sender, FileSystemEventArgs e)
        => ObserveDetached(OnConfigChangedAsync(), "config.watch.detached.fail");

    private void OnConfigWatcherRenamed(object? sender, RenamedEventArgs e)
        => ObserveDetached(OnConfigChangedAsync(), "config.watch.detached.fail");

    private void OnDbConfigAppliedEvent(object? sender, EventArgs e)
        => OnDbConfigApplied();

    private void OnDbConfigApplied()
    {
        _dbMonitor.Signal();

        RaiseDbStateChanged();

        if (_dbMonitor.IsConnected)
        {
            ScheduleAutoRefresh();
        }
    }
}
