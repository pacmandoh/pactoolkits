using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>
/// Agents 与 Desktop SemVer 闭区间配套（调用方传入的 minDesktop–maxDesktop）
/// </summary>
public sealed class AgentsBundleService : IAgentsBundleService
{
    public AgentsBundleResult Validate(
        string? desktopVersion,
        string? minDesktop,
        string? maxDesktop)
    {
        var desktop = (desktopVersion ?? string.Empty).Trim();
        if (IsUnknown(desktop))
        {
            return new AgentsBundleResult(
                Ok: false,
                AgentsBundleDenyKind.Unparsable,
                "无法校验 Agents 与 PacToolkits 配套：当前 PacToolkits 版本未知");
        }

        var min = (minDesktop ?? string.Empty).Trim();
        var max = (maxDesktop ?? string.Empty).Trim();
        // 半套或全空视为 Incomplete；非法字面量交给 Classify
        if (string.IsNullOrWhiteSpace(min) || string.IsNullOrWhiteSpace(max))
        {
            return new AgentsBundleResult(
                Ok: false,
                AgentsBundleDenyKind.IncompleteRange,
                $"Agents 配套 PacToolkits 范围不完整（下界 {min}，上界 {max}）");
        }

        // Desktop 产品版本可含 prerelease；库 schema 走 allowPrerelease:false
        var range = SemVerRange.Classify(desktop, min, max, allowPrerelease: true);
        return range.Status switch
        {
            SemVerRangeStatus.Compatible => Ok(),
            SemVerRangeStatus.BelowMinimum => new AgentsBundleResult(
                Ok: false,
                AgentsBundleDenyKind.BelowMin,
                $"当前 PacToolkits {desktop} 低于本 Agents 支持下限 {min}"),
            SemVerRangeStatus.AboveMaximum => new AgentsBundleResult(
                Ok: false,
                AgentsBundleDenyKind.AboveMax,
                $"当前 PacToolkits {desktop} 高于本 Agents 支持上限 {max}"),
            _ => new AgentsBundleResult(
                Ok: false,
                AgentsBundleDenyKind.Unparsable,
                $"无法校验 Agents 与 PacToolkits 配套范围：当前 {desktop}，要求 {min} - {max}"),
        };
    }

    private static AgentsBundleResult Ok()
        => new(true, AgentsBundleDenyKind.None, "ok");

    private static bool IsUnknown(string value)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
}
