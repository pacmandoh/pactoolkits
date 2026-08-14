using System;
using System.Globalization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>
/// 关于 PacToolkits 应用信息对话框 ViewModel
/// </summary>
public sealed class AppInfo(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required AppInfoArgs Info { get; init; }

    public string CopyrightText => $"© {DateTime.Today.Year} PacDocs · PacmanDoh 维护";
    public string VersionText => $"版本 {Dash(Info.Version)}";
    public string ComponentsText => $"Desktop {Dash(Info.Desktop)} · Agents {Dash(Info.Agents)}";
    public string ContractText => $"PacAPI 协议 {FormatContract()}";
    public string ReleaseText
        => DateOnly.TryParseExact(
            Info.ReleaseDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? $"发布于 {date.Year} 年 {date.Month} 月 {date.Day} 日"
            : $"发布日期 {Dash(Info.ReleaseDate)}";

    private string FormatContract()
    {
        var min = Dash(Info.MinApiContract);
        var max = Dash(Info.MaxApiContract);
        if (min == "--" || max == "--")
        {
            return "--";
        }

        return string.Equals(min, max, StringComparison.Ordinal)
            ? min
            : $"{min} – {max}";
    }

    private static string Dash(string? value)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
            ? "--"
            : value.Trim();
}
