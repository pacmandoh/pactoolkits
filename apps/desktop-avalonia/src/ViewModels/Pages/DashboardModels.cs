using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed record OptionItem(string Raw, string Display)
{
    public override string ToString() => Display;
}

public sealed record SimpleModeItem(string Title)
{
    public override string ToString() => Title;
}

public sealed partial class DashboardKpiModel : ObservableObject
{
    [ObservableProperty] private string _availableRemain = "0";
    [ObservableProperty] private string _periodUsed = "0";
    [ObservableProperty] private string _lowStockCount = "0";
    [ObservableProperty] private string _abnormal = "0";

    [ObservableProperty] private string _availableRemainHint = "当前库存中可用的追溯码数量";
    [ObservableProperty] private string _periodUsedHint = "区间内已使用的追溯码数量";
    [ObservableProperty] private string _abnormalHint = "区间内发生回滚/异常的事务数量";
    [ObservableProperty] private string _lowStockHint = "库存剩余量低于阈值的药品数量";

    [ObservableProperty] private double _availableRemainPct;
    [ObservableProperty] private double _periodUsedPct;
    [ObservableProperty] private double _abnormalPct;
    [ObservableProperty] private double _lowStockPct;
}

public sealed partial class TrendDrugItem : ObservableObject
{
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _valueText = "";

    public string SpecDisplay => SpecLine.Format(Name, Sub);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(SpecDisplay));

    partial void OnSubChanged(string value) => OnPropertyChanged(nameof(SpecDisplay));
}

public sealed record TxnItem(
    int DisplayIndex,
    long Id,
    TxnBadge Badge,
    string DrugId,
    string Spec,
    string Qty,
    string Time,
    string ClientDisplay)
{
    public string Title => string.IsNullOrWhiteSpace(Spec) ? DrugId : $"{DrugId} {Spec}";
}

public sealed record AbnormalItem(int DisplayIndex, string Title, string Detail, string ClientDisplay, TxnBadge Badge);

public sealed record TopClientItem(int Index, ClientInfo Client, string Value)
{
    public string Name => Client.Display;

    public string ClientDisplay => Client.Display;
    public string? Machine => Client.Machine;
    public string? User => Client.User;
    public string? Ip => Client.Ip;
    public string? Os => Client.Os;
    public string? Ver => Client.Version;

    public string MetaText
    {
        get
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(User))
            {
                parts.Add(User!);
            }

            if (!string.IsNullOrWhiteSpace(Ip))
            {
                parts.Add(Ip!);
            }

            if (!string.IsNullOrWhiteSpace(Os))
            {
                parts.Add(Os!);
            }

            if (!string.IsNullOrWhiteSpace(Ver))
            {
                parts.Add(Ver!);
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasMeta => !string.IsNullOrWhiteSpace(MetaText);
}

public sealed record EntryRecentItem(
    int DisplayIndex,
    TraceEntryState State,
    DateTimeOffset EntryAt,
    string EntryAtText,
    string DrugId,
    string Spec,
    int TotalAvailableQty,
    string Qty,
    string Source,
    string? Message,
    long? TxnId,
    string ClientRaw,
    string ClientMachine,
    string ClientDisplay,
    string? ClientIp,
    string? ClientOs,
    string? ClientVer
)
{
    public static EntryRecentItem From(TraceEntryLogDto e, ClientInfo client, int displayIndex = 0)
    {
        var result = (e.Result).Trim().ToLowerInvariant();
        var source = (e.Source).Trim().ToLowerInvariant();

        var state = result switch
        {
            "success" => TraceEntryState.Success,
            "partial" => TraceEntryState.Warning,
            "failed" => TraceEntryState.Failed,
            _ => TraceEntryState.Unknown
        };

        var qtyText = e.TotalAvailableQty.ToString("N0", CultureInfo.CurrentCulture);
        var atText = e.EntryAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture);
        var sourceText = MapSource(source);
        var resultText = MapResult(result);
        var messageText = BuildMessage(e.Message, sourceText, resultText);

        return new EntryRecentItem(
            DisplayIndex: displayIndex,
            State: state,
            EntryAt: e.EntryAt,
            EntryAtText: atText,
            DrugId: e.DrugId,
            Spec: e.Spec,
            TotalAvailableQty: e.TotalAvailableQty,
            Qty: qtyText,
            Source: sourceText,
            Message: messageText,
            TxnId: e.TxnId,
            ClientRaw: e.Client,
            ClientMachine: client.Machine ?? client.Display,
            ClientDisplay: client.Display,
            ClientIp: client.Ip,
            ClientOs: client.Os,
            ClientVer: client.Version
        );
    }

    public EntryRecentItem WithClient(ClientInfo client)
        => this with
        {
            ClientMachine = client.Machine ?? client.Display,
            ClientDisplay = client.Display,
            ClientIp = client.Ip,
            ClientOs = client.Os,
            ClientVer = client.Version
        };

    private static string MapSource(string source)
        => source switch
        {
            "manual" => "手动录入",
            "batch" => "批量导入",
            "api" => "自动拉取",
            _ => "未知来源"
        };

    private static string MapResult(string result)
        => result switch
        {
            "success" => "成功",
            "partial" => "部分成功",
            "failed" => "失败",
            _ => "未知状态"
        };

    private static string BuildMessage(string? rawMessage, string sourceText, string resultText)
    {
        var parsed = ParseSummary(rawMessage);
        if (parsed is null)
        {
            return string.IsNullOrWhiteSpace(rawMessage)
                ? $"{sourceText} · {resultText}"
                : $"{sourceText} · {resultText} · {rawMessage}";
        }

        return
            $"{sourceText} · {resultText} · 总数 {parsed.Value.Total} · 成功 {parsed.Value.Valid} · 重复 {parsed.Value.Duplicate} · 无效 {parsed.Value.Invalid} · 写入 {parsed.Value.Inserted} · 跳过 {parsed.Value.Skipped}";
    }

    private static (int Total, int Valid, int Duplicate, int Invalid, int Inserted, int Skipped)? ParseSummary(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return null;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parts = rawMessage.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx <= 0 || idx >= part.Length - 1)
            {
                continue;
            }

            var key = NormalizeSummaryKey(part[..idx].Trim());
            var val = part[(idx + 1)..].Trim();
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                map[key] = number;
            }
        }

        if (!map.TryGetValue("input", out var total) ||
            !map.TryGetValue("valid", out var valid) ||
            !map.TryGetValue("duplicate", out var duplicate) ||
            !map.TryGetValue("invalid", out var invalid) ||
            !map.TryGetValue("inserted", out var inserted) ||
            !map.TryGetValue("skipped", out var skipped))
        {
            return null;
        }

        return (total, valid, duplicate, invalid, inserted, skipped);
    }

    private static string NormalizeSummaryKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return string.Empty;
        }

        var key = rawKey.Trim().ToLowerInvariant();
        if (key.EndsWith(" input", StringComparison.Ordinal))
        {
            return "input";
        }

        return key;
    }
}
