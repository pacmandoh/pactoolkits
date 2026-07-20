using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings : AppPageBase, ISettingsPage
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
        RunDetached(ReloadForEditAsync, "client_alias.reload.edit_start_fail");
    }

    private async Task ReloadForEditAsync(CancellationToken pageCt)
    {
        await ReloadClientAliasesAsync(pageCt);
        await RunOnUiAsync(CaptureClientAliasEditBaseline);
    }

    private void RequestClientAliasReload(string failEvent)
    {
        if (IsClientAliasEditMode)
        {
            return;
        }

        RunDetached(ReloadClientAliasesAsync, failEvent);
    }

    private void RebindClientAliasRowsFromMap()
    {
        var map = NormalizeAliasMapByMachine(_alias.GetAll());
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ClientAliases)
        {
            known.Add(row.Machine);
            map.TryGetValue(row.Machine, out var alias);
            var next = alias ?? string.Empty;
            if (!string.Equals(row.Alias, next, StringComparison.Ordinal))
            {
                row.Alias = next;
            }
        }

        foreach (var kv in map.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (known.Contains(kv.Key))
            {
                continue;
            }

            var row = new ClientAliasRow(kv.Key, kv.Value);
            ClientAliases.Add(row);
            TrackAliasRow(row);
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (SkipTrigger())
        {
            return;
        }

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
                IsDbConnected = false;
                _toast.Error("数据库连接失败", validation.ConnectionSummary ?? "连接失败");
                return;
            }

            if (!validation.SchemaMigrationOk)
            {
                var reason = validation.MigrationSummary ?? "数据库结构更新失败";
                IsDbConnected = false;
                _toast.Warn("数据库迁移策略", reason);
                return;
            }

            if (!validation.SchemaCompatible)
            {
                IsDbConnected = false;
                await _dialog.Warn(DbSchemaCompat.GetIncompatibleTitle(), validation.IncompatibleMessage ?? "数据库版本不兼容");
                return;
            }

            IsDbConnected = true;
            _toast.Success("数据库连接", "连接成功");
        }
        catch (OperationCanceledException)
        {
            if (IsPageWorkCancellation())
            {
                return;
            }

            _logger.Warn("SettingsVM", "db.test.timeout", "DB connection test timed out");
            IsDbConnected = false;
            _toast.Error("数据库连接失败", "连接超时：请检查网络/主机/端口");
        }
    }

    [RelayCommand]
    private Task SaveAsync() => ApplyDbConfigAsync();

    private async Task<bool> ApplyDbConfigAsync()
    {
        if (SkipTrigger())
        {
            return false;
        }

        try
        {
            await _settings.SaveDbConfigAsync(ToOptions(), _pageWorkCts.Token);
            if (!await MigrateDbSchemaAsync())
            {
                IsDbConnected = false;
                _toast.Warn("数据库配置", "配置已保存，但迁移失败，当前不可用");
                return false;
            }

            if (!await CheckDbSchemaAsync())
            {
                IsDbConnected = false;
                _toast.Warn("数据库配置", "配置已保存，但数据库版本不兼容，当前不可用");
                return false;
            }

            IsDbConnected = true;
            _toast.Success("配置已保存", "数据库配置已应用");
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && IsPageWorkCancellation())
            {
                return false;
            }

            _logger.Error("SettingsVM", "db.save.fail", "Failed to save DB settings", ex);
            IsDbConnected = false;
            _toast.Error("保存失败", ex.Message);
            return false;
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
        var snapshot = await _settings.GetSchemaStatusAsync(
            BuildSchemaContext(),
            options,
            _pageWorkCts.Token);
        if (snapshot.ManualMigrationPolicy.Decision == DbMigrationDecision.RequiresConfirmation)
        {
            var confirmed = await _dialog.ConfirmDestructive(
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

    private async Task ReloadClientAliasesAsync(CancellationToken pageCt)
    {
        if (IsClientAliasRefreshing)
        {
            return;
        }

        pageCt.ThrowIfCancellationRequested();
        await SetAliasRefreshingAsync(true);
        try
        {
            var opt = ToOptions();
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(6));

            var loaded = await _settings.GetClientAliasSourcesAsync(opt, cts.Token);
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
            });
        }
        finally
        {
            await SetAliasRefreshingAsync(false);
        }
    }

    [RelayCommand]
    private Task SaveClientAliasesAsync() => ApplyClientAliasesAsync();

    private async Task<bool> ApplyClientAliasesAsync()
    {
        if (SkipTrigger())
        {
            return false;
        }

        if (!IsClientAliasEditMode)
        {
            _toast.Error("客户端别名", "请先点击‘开始编辑’");
            return false;
        }

        try
        {
            var items = ClientAliases
                .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
                .Select(x => new KeyValuePair<string, string>(x.Machine, x.Alias));
            _alias.ReplaceAll(items);

            _clientAliasEditBaseline = null;
            IsClientAliasEditMode = false;
            IsClientAliasReadOnly = true;

            _toast.Success("客户端别名", "已保存并生效");
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "client_alias.save.fail", "Failed to save client aliases", ex);
            _toast.Error("保存失败", ex.Message);
            return false;
        }
    }

    [RelayCommand]
    private Task SaveTraceCodeRuleAsync() => ApplyTraceCodeRuleAsync();

    private async Task<bool> ApplyTraceCodeRuleAsync()
    {
        if (SkipTrigger())
        {
            return false;
        }

        if (TraceCodeRequiredLength <= 0)
        {
            _toast.Error("追溯码规则", "长度必须大于 0");
            return false;
        }

        if (string.IsNullOrWhiteSpace(TraceCodePattern))
        {
            _toast.Error("追溯码规则", "正则表达式不能为空");
            return false;
        }

        try
        {
            _ = Regex.IsMatch(string.Empty, TraceCodePattern);
        }
        catch (Exception ex)
        {
            _logger.Warn("SettingsVM", "trace_rule.regex_invalid", "Invalid trace regex pattern", ex, new { TraceCodePattern });
            _toast.Error("追溯码规则", $"正则格式错误：{ex.Message}");
            return false;
        }

        try
        {
            await _traceCodeRule.SaveAsync(new TraceCodeValidationOptions
            {
                RequiredLength = TraceCodeRequiredLength,
                Pattern = TraceCodePattern
            });

            _toast.Success("追溯码规则", "规则已保存并生效");
            RefreshUnsaved();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "trace_rule.save.fail", "Failed to save trace code rule", ex);
            _toast.Error("追溯码规则保存失败", ex.Message);
            return false;
        }
    }

}

public sealed partial class ClientAliasRow : ObservableObject
{
    public ClientAliasRow(string machine, string alias)
    {
        _machine = machine;
        _alias = alias;
    }

    [ObservableProperty] private string _machine;
    [ObservableProperty] private string _alias;
}
