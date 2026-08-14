using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings : AppPageBase, ISettingsPage
{
    [RelayCommand]
    private Task SaveUpdateOptionsAsync() => ApplyUpdateOptionsAsync();

    private async Task<bool> ApplyUpdateOptionsAsync()
    {
        if (_syncingUpdateOptions || SkipTrigger())
        {
            return false;
        }

        try
        {
            var previous = _updateSettings.Current;

            var options = AppUpdatePolicy.NormalizeOptions(previous);
            options.FeedUrl = UpdateFeedUrl;
            options.AutoCheckIntervalMinutes = Math.Clamp(UpdatePollIntervalMinutes, 0, 720);
            options.IgnoredVersion = IgnoredProductVersion;

            await SaveUpdateOptionsLocalAsync(options);
            SyncUpdateOptions();
            _toast.Success("更新设置", "更新配置已保存");
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.settings.save.fail", "Failed to save update settings", ex);
            _toast.Error("更新设置保存失败", ex.Message);
            return false;
        }
        finally
        {
            SyncUpdateState();
        }
    }

    private async Task ApplyUpdateChannelImmediateAsync(string channel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var previous = _updateSettings.Current;
        var targetChannel = AppUpdatePolicy.NormalizeChannel(channel);
        if (string.Equals(
                AppUpdatePolicy.NormalizeChannel(previous.Channel),
                targetChannel,
                StringComparison.Ordinal))
        {
            return;
        }

        if (targetChannel == "beta")
        {
            var confirmed = await _dialog.Confirm(
                "切换到 Beta 更新通道",
                "Beta 版本可能包含尚未完成验证的功能；切换后将立即检查目标通道 Feed，是否继续？");
            if (!confirmed)
            {
                await RunOnUiAsync(() => RestoreUpdateChannel(previous.Channel));
                return;
            }

            ct.ThrowIfCancellationRequested();
        }

        if (!string.Equals(AppUpdatePolicy.NormalizeChannel(UpdateChannel), targetChannel, StringComparison.Ordinal))
        {
            return;
        }

        var current = _updateSettings.Current;
        try
        {
            await SaveUpdateOptionsLocalAsync(
                AppUpdatePolicy.WithChannelState(current, targetChannel, current.SeenInstalledChannel),
                ct);
        }
        catch
        {
            await RunOnUiAsync(() => RestoreUpdateChannel(_updateSettings.Current.Channel));
            throw;
        }
        await RunOnUiAsync(() =>
        {
            SyncPollHint();
            RefreshUnsaved();
            _toast.Success("更新设置", $"已切换到 {targetChannel} 通道");
        });
    }

    private async Task SaveUpdateOptionsLocalAsync(UpdateOptions options, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _localUpdateSaveCount);
        try
        {
            await _updateSettings.SaveAsync(options, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _localUpdateSaveCount);
        }
    }

    private void RestoreUpdateChannel(string channel)
    {
        _syncingUpdateOptions = true;
        UpdateChannel = channel;
        _syncingUpdateOptions = false;
        SyncPollHint();
    }

    [RelayCommand]
    private async Task SaveLoggingOptionsAsync()
    {
        _loggingAutoSaveCts?.Cancel();
        await ApplyLoggingOptionsAsync(silent: false);
    }

    private async Task<bool> ApplyLoggingOptionsAsync(bool silent = false)
    {
        if (_syncingLoggingOptions)
        {
            return false;
        }

        if (!silent && SkipTrigger())
        {
            return false;
        }

        // 静默自动保存不置 IsLoggingBusy，避免 H2 动作按钮闪烁；手动保存仍互斥占用中的打开/导出
        if (!silent)
        {
            for (var i = 0; i < 20 && IsLoggingBusy; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }

            if (IsLoggingBusy)
            {
                return false;
            }

            IsLoggingBusy = true;
        }

        try
        {
            var persisted = _loggingSettings.Current;
            var options = new LoggingOptions
            {
                Enabled = LoggingEnabled,
                MinimumLevel = LoggingMinimumLevel,
                RetentionDays = silent ? persisted.RetentionDays : LoggingRetentionDays,
                MaxFileSizeMb = silent ? persisted.MaxFileSizeMb : LoggingMaxFileSizeMb,
                LogDirectory = silent ? persisted.LogDirectory : LoggingDirectory
            };

            await SaveLoggingOptionsLocalAsync(options);
            if (!silent)
            {
                LoggingDirectory = LogDirectory.Resolve(_loggingSettings.Current.LogDirectory).BrowseDirectory;
                _toast.Success("日志设置", "日志配置已保存");
            }

            _logger.Info("SettingsVM", "logging.settings.saved", "Logging settings updated", new
            {
                options.Enabled,
                options.MinimumLevel,
                options.RetentionDays,
                options.MaxFileSizeMb,
                LogDirectory = _logger.LogDirectory
            });
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.settings.save_fail", "Failed to save logging settings", ex);
            _toast.Error("日志设置保存失败", ex.Message);
            return false;
        }
        finally
        {
            if (!silent)
            {
                IsLoggingBusy = false;
            }
        }
    }

    private async Task SaveLoggingOptionsLocalAsync(LoggingOptions options, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _localLoggingSaveCount);
        try
        {
            await _loggingSettings.SaveAsync(options, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _localLoggingSaveCount);
        }
    }

    [RelayCommand]
    private Task OpenLogDirectoryAsync()
    {
        if (SkipTrigger())
        {
            return Task.CompletedTask;
        }

        var dir = LogDirectory.Resolve(_loggingSettings.Current.LogDirectory).BrowseDirectory;
        TryOpenDirectory(dir, "打开日志目录失败", "logging.open_dir", "logging.open_dir_fail");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenConfigDirectoryAsync()
    {
        if (SkipTrigger())
        {
            return Task.CompletedTask;
        }

        var dir = Path.GetDirectoryName(_appConfigStore.ConfigPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            _toast.Error("打开配置目录失败", "无法解析配置目录路径");
            return Task.CompletedTask;
        }

        TryOpenDirectory(dir, "打开配置目录失败", "config.open_dir", "config.open_dir_fail");
        return Task.CompletedTask;
    }

    private void TryOpenDirectory(
        string dir,
        string toastTitle,
        string okEvent,
        string failEvent)
    {
        try
        {
            Directory.CreateDirectory(dir);

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    ArgumentList = { dir },
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { dir },
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    ArgumentList = { dir },
                    UseShellExecute = false
                });
            }

            _logger.Info("SettingsVM", okEvent, "Opened directory", new { dir });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", failEvent, "Failed to open directory", ex, new { dir });
            _toast.Error(toastTitle, ex.Message);
        }
    }

    [RelayCommand]
    private async Task CopyCurrentLogPathAsync()
    {
        if (IsLoggingBusy || SkipTrigger())
        {
            return;
        }

        IsLoggingBusy = true;
        try
        {
            var path = _logger.CurrentLogPath;
            await _clipboard.SetTextAsync(path);
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
        if (IsLoggingBusy || SkipTrigger())
        {
            return;
        }

        IsLoggingBusy = true;
        try
        {
            var path = await _logger.ExportRecentAsync(TimeSpan.FromHours(24));
            await _clipboard.SetTextAsync(path);
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
        if (_updates.IsChecking || IsUpdateApplying || SkipTrigger())
        {
            return;
        }

        await _updateFlow.CheckAndHandleAsync(
            silent: false,
            logScope: "SettingsVM");
    }

    [RelayCommand]
    private Task ApplyUpdateNowAsync()
    {
        if (IsUpdateChecking || IsUpdateApplying || SkipTrigger())
        {
            return Task.CompletedTask;
        }

        return _updateFlow.ApplyUpdateFlowAsync();
    }

    [RelayCommand]
    private async Task ClearIgnoredVersionAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        try
        {
            IgnoredProductVersion = string.Empty;
            var options = _updateSettings.Current;
            options.IgnoredVersion = string.Empty;
            await SaveUpdateOptionsLocalAsync(options);
            RefreshUnsaved();
            _toast.Success("更新设置", "已清除忽略版本");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.clear_ignored.fail", "Failed to clear ignored version", ex);
            _toast.Error("更新设置", ex.Message);
        }
    }

    private void TrackAliasRow(ClientAliasRow row)
    {
        if (_trackedAliasRows.Add(row))
        {
            row.PropertyChanged += OnClientAliasRowPropertyChanged;
        }
    }

    private void UntrackAliasRow(ClientAliasRow row)
    {
        if (_trackedAliasRows.Remove(row))
        {
            row.PropertyChanged -= OnClientAliasRowPropertyChanged;
        }
    }

    private void UntrackAllAliasRows()
    {
        foreach (var row in _trackedAliasRows.ToArray())
        {
            row.PropertyChanged -= OnClientAliasRowPropertyChanged;
        }

        _trackedAliasRows.Clear();
    }

    private void OnClientAliasRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClientAliasRow.Alias))
        {
            RefreshUnsaved();
        }
    }

    private static Dictionary<string, string> NormalizeAliasMapByMachine(IReadOnlyDictionary<string, string> source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            var machine = ExtractMachine(kv.Key);
            if (machine.Length == 0)
            {
                continue;
            }

            var alias = (kv.Value ?? string.Empty).Trim();
            if (alias.Length == 0)
            {
                continue;
            }

            map[machine] = alias;
        }

        return map;
    }

    private static string ExtractMachine(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var parsed = ClientParser.Parse(text);
        return (parsed.Machine ?? text).Trim();
    }

    private static string GetAvailabilityLabel(bool? value) => value switch
    {
        true => "可更新",
        false => "已最新",
        _ => "未知"
    };

}
