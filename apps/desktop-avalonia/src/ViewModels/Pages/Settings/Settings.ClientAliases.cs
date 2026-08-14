using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

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
    private Task SaveAsync() => ApplyConnectionTabAsync();

    private async Task<bool> ApplyConnectionTabAsync()
    {
        if (IsPacApiDirty())
        {
            return await ApplyPacApiConfigAsync();
        }

        return true;
    }

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
            var apiReady = ConnectionView.IsReady(_apiAvailability.Current, _apiAvailability.IsConfigured);
            IReadOnlyList<string> machines = Array.Empty<string>();
            if (apiReady)
            {
                using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(6));
                machines = await _lookup.GetClientIdsAsync(cts.Token, forceRefresh: true);
            }

            var aliasMap = NormalizeAliasMapByMachine(_alias.GetAll());
            var clientMachines = new HashSet<string>(machines, StringComparer.OrdinalIgnoreCase);

            await RunOnUiAsync(() =>
            {
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
            Interlocked.Increment(ref _localClientAliasSaveCount);
            try
            {
                _alias.ReplaceAll(items);
            }
            finally
            {
                Interlocked.Decrement(ref _localClientAliasSaveCount);
            }

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

/// <summary>设置页客户端别名编辑行</summary>
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
