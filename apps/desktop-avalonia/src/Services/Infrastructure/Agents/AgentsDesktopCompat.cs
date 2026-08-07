using System;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Agents 对 Desktop SemVer 闭区间配套校验（minDesktop–maxDesktop）
/// </summary>
internal static class AgentsDesktopCompat
{
    public static AgentsCommandResult Validate(
        string? desktopVersion,
        string? minDesktop,
        string? maxDesktop)
    {
        var desktop = (desktopVersion ?? string.Empty).Trim();
        if (IsUnknown(desktop))
        {
            // 本机未嵌入 Desktop 版本时跳过（开发松绑）
            return new AgentsCommandResult(true, "ok");
        }

        var min = (minDesktop ?? string.Empty).Trim();
        var max = (maxDesktop ?? string.Empty).Trim();
        var minUnknown = IsUnknown(min);
        var maxUnknown = IsUnknown(max);
        if (minUnknown && maxUnknown)
        {
            // 两端皆缺：开发松绑
            return new AgentsCommandResult(true, "ok");
        }

        if (minUnknown || maxUnknown)
        {
            return new AgentsCommandResult(
                false,
                $"Agents 配套 Desktop 范围不完整（minDesktop={min}，maxDesktop={max}）");
        }

        if (!SemVer.TryParse(desktop, out var desktopVer)
            || !SemVer.TryParse(min, out var minimum)
            || !SemVer.TryParse(max, out var maximum)
            || SemVer.Compare(minimum, maximum) > 0)
        {
            return new AgentsCommandResult(
                false,
                $"无法校验 Agents 与 Desktop 配套范围：Desktop {desktop}，要求 {min} - {max}");
        }

        if (SemVer.Compare(desktopVer, minimum) < 0)
        {
            return new AgentsCommandResult(
                false,
                $"当前 Desktop {desktop} 低于本 Agents 支持下限 {min}");
        }

        if (SemVer.Compare(desktopVer, maximum) > 0)
        {
            return new AgentsCommandResult(
                false,
                $"当前 Desktop {desktop} 高于本 Agents 支持上限 {max}");
        }

        return new AgentsCommandResult(true, "ok");
    }

    private static bool IsUnknown(string value)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
}
