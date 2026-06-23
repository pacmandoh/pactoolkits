using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class SettingsViewModel : AppPageBase, ISettingsPage
{
    [RelayCommand]
    private void StartEditClientAliases()
    {
        if (SkipTrigger())
        {
            return;
        }

        IsClientAliasEditMode = true;
        IsClientAliasReadOnly = false;
        RefreshClientAlias();
        RunDetached(ReloadClientAliasesAsync, "client_alias.reload.edit_start_fail");
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        IsBusy = true;
        Status = null;

        try
        {
            var opt = ToOptions();
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(6));

            var validation = await _settings.ValidateDbConnectionAsync(
                opt,
                BuildSchemaContext(),
                cts.Token);

            if (!validation.ConnectionOk)
            {
                Status = validation.ConnectionSummary;
                IsDbConnected = false;
                RefreshClientAlias();
                _toast.Error("数据库连接失败", validation.ConnectionSummary ?? "连接失败");
                return;
            }

            if (!validation.SchemaMigrationOk)
            {
                var reason = validation.MigrationSummary ?? "数据库结构更新失败";
                Status = reason;
                IsDbConnected = false;
                RefreshClientAlias();
                _toast.Warn("数据库迁移策略", reason);
                return;
            }

            if (!validation.SchemaCompatible)
            {
                Status = "数据库版本不兼容";
                IsDbConnected = false;
                RefreshClientAlias();
                await _dialog.Warn(DbSchemaCompat.GetIncompatibleTitle(), validation.IncompatibleMessage ?? "数据库版本不兼容");
                return;
            }

            Status = "连接成功";
            IsDbConnected = true;
            RunDetached(ReloadClientAliasesAsync, "client_alias.reload.after_test_fail");
            _toast.Success("数据库连接", "连接成功");
        }
        catch (OperationCanceledException)
        {
            if (IsPageWorkCancellation())
            {
                return;
            }

            _logger.Warn("SettingsVM", "db.test.timeout", "DB connection test timed out");
            Status = "连接超时";
            IsDbConnected = false;
            RefreshClientAlias();
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
        if (SkipTrigger())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _settings.SaveDbConfigAsync(ToOptions(), _pageWorkCts.Token);
            if (!await MigrateDbSchemaAsync())
            {
                Status = "配置已保存，但迁移失败，当前不可用";
                IsDbConnected = false;
                RefreshClientAlias();
                _toast.Warn("数据库配置", "配置已保存，但迁移失败，当前不可用");
                return;
            }
            if (!await CheckDbSchemaAsync())
            {
                Status = "配置已保存，但数据库版本不兼容，当前不可用";
                IsDbConnected = false;
                RefreshClientAlias();
                _toast.Warn("数据库配置", "配置已保存，但数据库版本不兼容，当前不可用");
                return;
            }

            Status = "连接成功";
            IsDbConnected = true;
            _toast.Success("配置已保存", "数据库配置已应用");

            RunDetached(ReloadClientAliasesAsync, "client_alias.reload.after_save_fail");
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && IsPageWorkCancellation())
            {
                return;
            }

            _logger.Error("SettingsVM", "db.save.fail", "Failed to save DB settings", ex);
            IsDbConnected = false;
            RefreshClientAlias();
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
        if (SkipTrigger() || IsDbSchemaChecking)
        {
            return;
        }

        await UpdateSchemaStatusAsync(
            "manual_check",
            manualProbe: true,
            connectionOptions: ToOptions(),
            operationCt: _pageWorkCts.Token);
    }

    [RelayCommand]
    private async Task ViewDbSchemaMigrationPlanAsync()
    {
        if (SkipTrigger() || IsDbSchemaChecking)
        {
            return;
        }

        SetDbSchemaStatus("读取计划", checking: true, failed: false, error: null);
        try
        {
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(30));
            var plan = await _settings.GetSchemaMigrationPlanAsync(
                BuildSchemaContext(),
                ToOptions(),
                cts.Token);
            DbSchemaMigrationPlanText = FormatMigrationPlan(plan);
            _toast.Info("数据库迁移计划", DbSchemaMigrationPlanText);
            await UpdateSchemaStatusAsync(
                "migration_plan",
                manualProbe: false,
                connectionOptions: ToOptions(),
                operationCt: cts.Token);
        }
        catch (Exception ex)
        {
            DbSchemaMigrationPlanText = $"读取迁移计划失败：{ex.Message}";
            SetDbSchemaStatus("计划失败", checking: false, failed: true, error: ex.Message);
            _toast.Error("数据库迁移计划", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyDbSchemaUpdateAsync()
    {
        if (SkipTrigger() || IsDbSchemaChecking)
        {
            return;
        }

        var options = ToOptions();
        var snapshot = await _settings.ReadSchemaStatusAsync(
            BuildSchemaContext(),
            options,
            _pageWorkCts.Token);
        if (snapshot.ManualMigrationPolicy.Decision == DbMigrationDecision.RequiresConfirmation)
        {
            var confirmed = await _dialog.Confirm(
                "确认更新数据库",
                $"{snapshot.ManualMigrationPolicy.Reason}\n\n此操作将修改 Beta 隔离测试库结构，是否继续？");
            if (!confirmed)
            {
                return;
            }

            await MigrateDbSchemaAsync(options, userConfirmed: true);
            return;
        }

        await MigrateDbSchemaAsync(options);
    }

    [RelayCommand]
    private async Task CopyDbSchemaDiagnosticsAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        var text = BuildDbSchemaDiagnosticsText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _toast.Warn("数据库结构更新", "当前无可复制的诊断信息");
            return;
        }

        await _clipboard.SetTextAsync(text);
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
    private async Task ReloadClientAliasesAsync(CancellationToken pageCt)
    {
        if (IsClientAliasRefreshing || SkipTrigger())
        {
            return;
        }

        pageCt.ThrowIfCancellationRequested();
        await SetAliasRefreshingAsync(true);
        try
        {
            var opt = ToOptions();
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(6));

            var loaded = await _settings.LoadClientAliasSourcesAsync(opt, cts.Token);
            var isDbConnected = loaded.IsDbConnected;
            var aliasMap = NormalizeAliasMapByMachine(_alias.GetAll());
            var clientMachines = new HashSet<string>(loaded.ClientMachines, StringComparer.OrdinalIgnoreCase);

            await RunOnUiAsync(() =>
            {
                IsDbConnected = isDbConnected;
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
                    if (clientMachines.Contains(kv.Key))
                    {
                        continue;
                    }

                    var row = new ClientAliasRow(kv.Key, kv.Value);
                    ClientAliases.Add(row);
                    TrackAliasRow(row);
                }

                RefreshClientAlias();
            });
        }
        finally
        {
            await SetAliasRefreshingAsync(false);
        }
    }

    private void RefreshClientAlias()
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
        if (row is null)
        {
            return;
        }

        if (IsClientAliasReadOnly)
        {
            return;
        }

        ClientAliases.Remove(row);
        UntrackAliasRow(row);
        RefreshClientAlias();
    }

    [RelayCommand]
    private async Task SaveClientAliasesAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

        if (!IsClientAliasEditMode)
        {
            _toast.Error("客户端别名", "请先点击‘开始编辑’");
            return;
        }

        RefreshClientAlias();

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

            RunDetached(ReloadClientAliasesAsync, "client_alias.reload.after_alias_save_fail");
            RefreshClientAlias();
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
        if (SkipTrigger())
        {
            return;
        }

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

}
