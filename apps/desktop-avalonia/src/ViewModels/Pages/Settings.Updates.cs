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
using PacToolkits.Application.DTOs;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

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

        IsUpdateChecking = true;
        try
        {
            var previous = _updateSettings.Current;
            var targetChannel = NormalizeUpdateChannel(UpdateChannel);
            var channelChanged = !string.Equals(previous.Channel, targetChannel, StringComparison.Ordinal);
            if (channelChanged)
            {
                var switched = await TrySwitchUpdateChannelAsync(previous, targetChannel);
                if (!switched)
                {
                    return false;
                }
            }

            var options = new UpdateOptions
            {
                AutoCheckOnStartup = AutoCheckUpdateOnStartup,
                Channel = targetChannel,
                ValidatedChannel = channelChanged ? targetChannel : previous.ValidatedChannel,
                FeedUrl = UpdateFeedUrl,
                AutoCheckIntervalMinutes = Math.Clamp(UpdatePollIntervalMinutes, 0, 720),
                IgnoredVersion = IgnoredProductVersion
            };

            await _updateSettings.SaveAsync(options);
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
            IsUpdateChecking = false;
        }
    }

    private async Task<bool> TrySwitchUpdateChannelAsync(UpdateOptions previous, string targetChannel)
    {
        if (targetChannel == "beta")
        {
            var confirmed = await _dialog.Confirm(
                "切换到 Beta 更新通道",
                "Beta 版本可能包含尚未完成验证的功能。切换前将检查 Beta Feed 和当前数据库兼容范围；不会执行数据库迁移。是否继续？");
            if (!confirmed)
            {
                RestoreUpdateChannel(previous.Channel);
                return false;
            }
        }

        using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(20));
        var probe = await _releaseChannelService.ProbeAsync(
            UpdateFeedUrl,
            targetChannel,
            ToOptions(),
            cts.Token);
        UpdateChannelSwitchHint = probe.Message;
        if (!probe.Success)
        {
            RestoreUpdateChannel(previous.Channel);
            await _dialog.Warn("无法切换更新通道", probe.Message);
            return false;
        }

        _logger.Info("SettingsVM", "update.channel.switch.validated",
            "Release channel switch validated without database migration", new
            {
                PreviousChannel = previous.Channel,
                TargetChannel = targetChannel,
                probe.FeedManifestUrl,
                probe.CurrentDbSchema,
                probe.RequiredMinDbSchema,
                probe.RequiredMaxDbSchema
            });
        return true;
    }

    private void RestoreUpdateChannel(string channel)
    {
        _syncingUpdateOptions = true;
        UpdateChannel = channel;
        _syncingUpdateOptions = false;
        SyncPollHint();
    }

    private static string NormalizeUpdateChannel(string? channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant();
        return normalized is "stable" or "beta" ? normalized : "stable";
    }

    [RelayCommand]
    private Task SaveLoggingOptionsAsync() => ApplyLoggingOptionsAsync();

    private async Task<bool> ApplyLoggingOptionsAsync(bool silent = false)
    {
        if (_syncingLoggingOptions || IsLoggingBusy || SkipTrigger())
        {
            return false;
        }

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

            await _loggingSettings.SaveAsync(options);
            LoggingDirectory = _logger.LogDirectory;
            if (!silent)
            {
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
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenLogDirectoryAsync()
    {
        if (IsLoggingBusy || SkipTrigger())
        {
            return Task.CompletedTask;
        }

        IsLoggingBusy = true;
        try
        {
            var dir = _logger.LogDirectory;
            Directory.CreateDirectory(dir);
            OpenDirectory(dir);
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
            showNoUpdateToast: true,
            startupMode: false,
            applyNowAction: ApplyUpdateNowAsync,
            ignoreVersionAction: IgnoreCurrentUpdateAsync,
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
            await _updateSettings.SaveIgnoredVersionAsync(string.Empty);
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
            await _updateFlow.IgnoreVersionAsync(LatestProductVersion);
            IgnoredProductVersion = LatestProductVersion;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.ignore.fail", "Failed to ignore update version", ex);
            _toast.Error("应用更新", ex.Message);
        }
    }

    private DbSchemaVersionContext BuildSchemaContext()
    {
        var version = _releaseVersion.Current;
        return new DbSchemaVersionContext(
            version.UiMinDbSchema,
            version.UiMaxDbSchema,
            version.AgentMinDbSchema,
            version.AgentMaxDbSchema,
            version.DbSchemaVersion,
            version.BuildChannel,
            version.DbMigrationPolicy);
    }

    private async Task<bool> CheckDbSchemaAsync(PgOptions? connectionOptions = null)
    {
        var options = connectionOptions ?? ToOptions();
        var compat = await _settings.CheckSchemaCompatibilityAsync(
            BuildSchemaContext(),
            options,
            _pageWorkCts.Token);
        await UpdateSchemaStatusAsync(
            "compat_check",
            manualProbe: false,
            connectionOptions: options,
            operationCt: _pageWorkCts.Token);
        if (compat.Compatible)
        {
            return true;
        }

        await _dialog.Warn(DbSchemaCompat.GetIncompatibleTitle(), compat.IncompatibleMessage ?? "数据库版本不兼容");
        return false;
    }

    private Task<bool> MigrateDbSchemaAsync(bool userConfirmed = false)
        => MigrateDbSchemaAsync(ToOptions(), userConfirmed);

    private async Task<bool> MigrateDbSchemaAsync(
        PgOptions connectionOptions,
        bool userConfirmed = false)
    {
        SetDbSchemaStatus("更新中", checking: true, failed: false, error: null);
        try
        {
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(120));
            var status = await _settings.GetSchemaStatusAsync(
                BuildSchemaContext(),
                connectionOptions,
                cts.Token);
            if (status.Compatibility == DbSchemaCompatibility.AboveMaximum)
            {
                DbSchemaCurrentVersion = status.CurrentVersion ?? "unknown";
                DbSchemaTargetVersion = status.TargetVersion;
                DbSchemaRequiredMinVersion = status.RequiredMinVersion;
                DbSchemaRequiredMaxVersion = status.RequiredMaxVersion;
                var message =
                    $"数据库版本高于当前程序支持范围：当前 {status.CurrentVersion}，最高支持 {status.RequiredMaxVersion}。不会执行自动降级。";
                SetDbSchemaStatus("版本过高", checking: false, failed: true, error: message);
                _toast.Error("数据库结构更新", message);
                return false;
            }

            var migration = await _settings.MigrateSchemaAsync(
                BuildSchemaContext(),
                DbMigrationTrigger.SettingsManual,
                connectionOptions,
                userConfirmed: userConfirmed,
                ciMigrationAuthorized: false,
                ct: cts.Token);
            if (!migration.Ok)
            {
                SetDbSchemaStatus("更新失败", checking: false, failed: true, error: migration.Summary);
                DbSchemaLastMigrationText = migration.Summary;
                _toast.Error("数据库结构更新", migration.Summary);
                return false;
            }

            DbSchemaLastMigrationText = migration.Summary;
            if (migration.Summary.Contains("applied=", StringComparison.Ordinal))
            {
                _toast.Success("数据库结构更新", "数据库结构已更新");
            }
            else if (status.ManualMigrationPolicy.Decision == DbMigrationDecision.ReadOnlyRequired)
            {
                _toast.Warn("数据库结构更新", migration.Summary);
            }

            await UpdateSchemaStatusAsync(
                "migrate_done",
                manualProbe: false,
                connectionOptions: connectionOptions,
                operationCt: cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            SetDbSchemaStatus("更新失败", checking: false, failed: true, error: ex.Message);
            DbSchemaLastMigrationText = $"迁移失败：{ex.Message}";
            OnPropertyChanged(nameof(CanCopyDbSchemaDiagnostics));
            _toast.Error("数据库结构更新", $"更新失败：{ex.Message}");
            return false;
        }
    }

    private async Task UpdateSchemaStatusAsync(
        string source,
        bool manualProbe,
        PgOptions? connectionOptions = null,
        CancellationToken operationCt = default)
    {
        if (IsDbSchemaChecking && manualProbe)
        {
            return;
        }

        if (manualProbe)
        {
            SetDbSchemaStatus("更新中", checking: true, failed: false, error: null);
        }

        try
        {
            var ct = operationCt.CanBeCanceled ? operationCt : _pageWorkCts.Token;
            var options = connectionOptions ?? ToOptions();
            var snapshot = await _settings.GetSchemaStatusAsync(BuildSchemaContext(), options, ct);
            DbSchemaLastCheckedAtText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DbSchemaLastCheckSourceText = MapDbSchemaCheckSource(source);
            DbSchemaTargetVersion = snapshot.TargetVersion;
            DbSchemaRequiredMinVersion = snapshot.RequiredMinVersion;
            DbSchemaRequiredMaxVersion = snapshot.RequiredMaxVersion;
            DbSchemaCurrentVersion = snapshot.CurrentVersion ?? "unknown";

            SyncMigrationPolicy(snapshot.ManualMigrationPolicy);

            if (snapshot.Compatibility == DbSchemaCompatibility.MetadataMissing)
            {
                SetDbSchemaStatus(
                    "需要初始化",
                    checking: false,
                    failed: false,
                    error: snapshot.Reason ?? "数据库缺少迁移元数据，需要初始化");
                if (manualProbe)
                {
                    _toast.Warn("数据库结构更新", snapshot.Reason ?? "数据库缺少迁移元数据，需要初始化");
                }

                return;
            }

            if (!snapshot.SchemaOk)
            {
                SetDbSchemaStatus("未知", checking: false, failed: false, error: snapshot.Reason ?? "读取失败");
                if (manualProbe)
                {
                    _toast.Warn("数据库结构更新", $"状态未知：{snapshot.Reason ?? "读取失败"}");
                }

                return;
            }

            if (snapshot.Compatibility == DbSchemaCompatibility.AboveMaximum)
            {
                SetDbSchemaStatus(
                    "版本过高",
                    checking: false,
                    failed: true,
                    error: $"数据库版本高于当前程序支持范围：当前 {snapshot.CurrentVersion}，最高支持 {snapshot.RequiredMaxVersion}");
            }
            else if (!snapshot.Satisfied)
            {
                SetDbSchemaStatus("需要更新", checking: false, failed: false, error: $"当前版本 {snapshot.CurrentVersion} 低于最低要求 {snapshot.RequiredMinVersion}");
            }
            else if (snapshot.Updatable)
            {
                SetDbSchemaStatus("可更新", checking: false, failed: false, error: $"当前版本 {snapshot.CurrentVersion} 低于本地版本文件 {snapshot.TargetVersion}");
            }
            else
            {
                SetDbSchemaStatus("已满足", checking: false, failed: false, error: null);
            }

            if (manualProbe)
            {
                if (snapshot.Compatibility == DbSchemaCompatibility.AboveMaximum)
                {
                    _toast.Error("数据库结构更新", $"数据库版本高于当前程序支持范围，最高支持 {snapshot.RequiredMaxVersion}");
                }
                else if (!snapshot.Satisfied)
                {
                    _toast.Warn("数据库结构更新", $"当前版本 {snapshot.CurrentVersion}，低于最低要求 {snapshot.RequiredMinVersion}");
                }
                else if (snapshot.Updatable)
                {
                    _toast.Warn("数据库结构更新", $"当前版本 {snapshot.CurrentVersion}，可更新到本地版本 {snapshot.TargetVersion}");
                }
                else
                {
                    _toast.Success("数据库结构更新", $"当前版本 {snapshot.CurrentVersion}，满足最低要求 {snapshot.RequiredMinVersion}");
                }
            }
        }
        finally
        {
            IsDbSchemaChecking = false;
        }
    }

    private async Task RefreshSchemaStatusOnStartupAsync(CancellationToken ct)
    {
        try
        {
            await UpdateSchemaStatusAsync("startup", manualProbe: false, operationCt: ct);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "db.schema.startup_refresh.fail", "Startup schema status refresh failed", ex);
            SetDbSchemaStatus("未知", checking: false, failed: false, error: ex.Message);
            DbSchemaLastCheckedAtText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DbSchemaLastCheckSourceText = MapDbSchemaCheckSource("startup");
        }
    }

    private static string MapDbSchemaCheckSource(string source)
        => source switch
        {
            "startup" => "应用启动",
            "startup_postcheck" => "启动检查完成后回读",
            "open_settings" => "打开设置页",
            "db_reconnected" => "数据库重连后回读",
            "manual_check" => "手动检查",
            "migration_plan" => "查看迁移计划",
            "compat_check" => "兼容性校验",
            "migrate_done" => "迁移完成后回读",
            _ => source
        };

    private static string FormatMigrationPlan(DbSchemaMigrationPlan plan)
    {
        var pending = plan.Items.Where(x => !x.Applied).ToArray();
        if (pending.Length == 0)
        {
            return $"当前 {plan.CurrentVersion ?? "未初始化"}，目标 {plan.TargetVersion}，无待执行迁移";
        }

        var files = string.Join(", ", pending.Select(x => x.FileName));
        var bootstrap = plan.BootstrapRequired ? "，需要初始化迁移元数据" : string.Empty;
        return $"当前 {plan.CurrentVersion ?? "未初始化"}，目标 {plan.TargetVersion}，待执行 {pending.Length} 项{bootstrap}：{files}";
    }

    private void SetDbSchemaStatus(string status, bool checking, bool failed, string? error)
    {
        DbSchemaStatusText = status;
        IsDbSchemaChecking = checking;

        DbSchemaErrorText = error ?? string.Empty;
        MapDbSchemaBadge(status, checking, failed);
        OnPropertyChanged(nameof(CanCopyDbSchemaDiagnostics));
        OnPropertyChanged(nameof(CanApplyDbSchemaUpdate));
    }

    private void SyncMigrationPolicy(DbMigrationOutcome policy)
    {
        CanApplyDbSchemaUpdateByPolicy =
            policy.Decision is DbMigrationDecision.Allowed
                or DbMigrationDecision.RequiresConfirmation;
        DbSchemaPolicyText = $"{_releaseVersion.Current.DbMigrationPolicy}: {policy.Reason}";
        OnPropertyChanged(nameof(CanApplyDbSchemaUpdate));
    }

    private string BuildDbSchemaDiagnosticsText()
    {
        var lines = new List<string>
        {
            $"status={DbSchemaStatusText}",
            $"target={DbSchemaTargetVersion}",
            $"current={DbSchemaCurrentVersion}",
            $"required_min={DbSchemaRequiredMinVersion}",
            $"required_max={DbSchemaRequiredMaxVersion}",
            $"last_checked_at={DbSchemaLastCheckedAtText}",
            $"last_check_source={DbSchemaLastCheckSourceText}",
            $"last_migration={DbSchemaLastMigrationText}",
            $"migration_plan={DbSchemaMigrationPlanText}",
            $"policy={DbSchemaPolicyText}"
        };

        if (!string.IsNullOrWhiteSpace(DbSchemaErrorText))
        {
            lines.Add($"error={DbSchemaErrorText}");
        }

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

        if (string.Equals(status, "版本过高", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "版本过高";
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

        if (string.Equals(status, "可更新", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = true;
            DbSchemaBadgeLabel = "可更新";
            return;
        }

        if (string.Equals(status, "需要更新", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = true;
            DbSchemaBadgeLabel = "需要更新";
            return;
        }

        if (string.Equals(status, "未知", StringComparison.Ordinal)
            || string.Equals(status, "未检查", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "未知";
            return;
        }

        DbSchemaBadgeStatus = null;
        DbSchemaBadgeLabel = string.IsNullOrWhiteSpace(status) ? "未知" : status;
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
            RefreshClientAlias();
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
