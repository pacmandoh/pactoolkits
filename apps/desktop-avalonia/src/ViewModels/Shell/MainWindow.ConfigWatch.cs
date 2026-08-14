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
        if (!File.Exists(_configPath))
        {
            var openSettings = await _dialogs.Confirm(
                    "未找到配置文件",
                    "未检测到本地配置文件，请前往 [设置] 完成连接配置后再使用")
                .ConfigureAwait(false);

            if (openSettings)
            {
                await RunOnUiAsync(() => OpenConnectivitySettings());
            }

            return;
        }

        // 未配置：Confirm 引导设置；横幅另由 ConnectivityBanner 展示
        if (!_apiAvailability.IsConfigured)
        {
            var openSettings = await _dialogs.Confirm(
                    "未配置 PacAPI 服务",
                    "请前往 [设置] 完成 PacAPI 服务地址与密钥配置后再使用")
                .ConfigureAwait(false);

            if (openSettings)
            {
                await RunOnUiAsync(() => OpenConnectivitySettings());
            }
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

    private void OnConfigWatcherChanged(object? sender, FileSystemEventArgs e)
        => ObserveDetached(OnConfigChangedAsync(), "config.watch.detached.fail");

    private void OnConfigWatcherRenamed(object? sender, RenamedEventArgs e)
        => ObserveDetached(OnConfigChangedAsync(), "config.watch.detached.fail");
}
