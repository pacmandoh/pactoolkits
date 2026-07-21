using System;
using System.Globalization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
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
    public string VersionText => $"版本 {Info.Version}";
    public string ReleaseText
        => DateOnly.TryParseExact(
            Info.ReleaseDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? $"发布于 {date.Year} 年 {date.Month} 月 {date.Day} 日"
            : $"发布日期 {Info.ReleaseDate}";
}
