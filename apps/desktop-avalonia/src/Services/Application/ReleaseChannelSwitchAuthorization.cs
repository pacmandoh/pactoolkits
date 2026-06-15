using System;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class ReleaseChannelSwitchAuthorization
{
    public static bool IsAuthorized(string targetChannel, string installedChannel, string? validatedChannel)
    {
        var target = NormalizeChannel(targetChannel);
        if (string.IsNullOrWhiteSpace(installedChannel))
            return true;

        var installed = NormalizeChannel(installedChannel);
        if (string.Equals(target, installed, StringComparison.Ordinal))
            return true;

        var validated = NormalizeOptionalChannel(validatedChannel);
        return !string.IsNullOrWhiteSpace(validated)
               && string.Equals(target, validated, StringComparison.Ordinal);
    }

    public static string BuildBlockedMessage(string installedChannel, string targetChannel)
    {
        var installed = string.IsNullOrWhiteSpace(installedChannel) ? "unknown" : NormalizeChannel(installedChannel);
        var target = NormalizeChannel(targetChannel);
        return $"当前安装通道为 {installed}，配置目标通道为 {target}。"
               + "请先在设置中完成通道切换并通过数据库兼容检查后再检查更新。";
    }

    private static string NormalizeChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant();
        return normalized is "stable" or "beta" ? normalized : "stable";
    }

    private static string NormalizeOptionalChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        var normalized = channel.Trim().ToLowerInvariant();
        return normalized is "stable" or "beta" ? normalized : string.Empty;
    }
}
