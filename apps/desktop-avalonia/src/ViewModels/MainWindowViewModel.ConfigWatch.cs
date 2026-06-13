using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.DataAccess;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private async Task CheckConfigOnStartupAsync()
    {
        if (File.Exists(_configPath)) return;

        var openSettings = await _dialogs.Confirm(
                "未找到配置文件",
                $"未检测到本地配置文件，请前往 [设置] 完成数据库连接配置后再使用")
            .ConfigureAwait(false);

        if (openSettings)
            await RunOnUiAsync(OpenSettings);
    }

    private void StartConfigWatcher()
    {
        try
        {
            Directory.CreateDirectory(_configDir);

            // Watch the unified app config and react to external edits.
            _configWatcher = new FileSystemWatcher(_configDir, _configFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
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
        if (_disposed) return;
        if (_isApplyingConfig) return;

        // Reason: Debounce file watcher bursts from editor write patterns.
        await Task.Delay(350).ConfigureAwait(false);

        if (!File.Exists(_configPath)) return;
        _ahkRuntime.Reload();

        AppConfigRoot? loaded;
        string? json;
        try
        {
            json = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);

            if (string.Equals(json, _lastSeenConfigJson, StringComparison.Ordinal))
                return;

            loaded = JsonSerializer.Deserialize<AppConfigRoot>(json);
        }
        catch
        {
            loaded = null;
            json = null;
        }

        if (loaded?.Postgres is null) return;

        if (IsSamePgOptions(_dbConfig.Current, loaded.Postgres))
        {
            _lastSeenConfigJson = json;
            return;
        }

        try
        {
            // Do not hot-apply external DB target changes during runtime.
            // Runtime datasource switches can cause cross-DB read/write inconsistency.
            _isApplyingConfig = true;
            _lastSeenConfigJson = json;
            _logger.Warn("MainWindowVM", "config.external_db_change_ignored",
                "Detected external DB config change, ignored until manual apply in Settings", null, new
                {
                    loaded.Postgres.Host,
                    loaded.Postgres.Port,
                    loaded.Postgres.Database
                });
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.external_apply_fail", "Failed to apply external config changes", ex);
        }
        finally
        {
            _isApplyingConfig = false;
        }

        _dbMonitor.Signal();
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
        => _ = OnConfigChangedAsync();

    private void OnConfigWatcherRenamed(object? sender, RenamedEventArgs e)
        => _ = OnConfigChangedAsync();

    private void OnDbConfigAppliedEvent(object? sender, EventArgs e)
        => OnDbConfigApplied();

    private void OnDbConfigApplied()
    {
        _dbMonitor.Signal();

        RaiseDbStateChanged();

        if (_dbMonitor.IsConnected)
            ScheduleAutoRefresh();
    }
}
