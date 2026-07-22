using System.Text.Json;

namespace PacToolkits.Application.Services;

/// <summary>
/// 从发布清单 JSON 解析 Avalonia Desktop 节点
/// </summary>
public static class ReleaseManifestDesktop
{
    public static bool TryGetAvalonia(JsonElement components, out JsonElement desktop)
    {
        desktop = default;
        if (components.ValueKind != JsonValueKind.Object
            || !components.TryGetProperty("desktop", out var desktopRoot)
            || desktopRoot.ValueKind != JsonValueKind.Object
            || !desktopRoot.TryGetProperty("avalonia", out desktop)
            || desktop.ValueKind != JsonValueKind.Object)
        {
            desktop = default;
            return false;
        }

        return true;
    }

    public static JsonElement GetRequiredAvalonia(JsonElement components)
    {
        if (TryGetAvalonia(components, out var desktop))
        {
            return desktop;
        }

        throw new InvalidOperationException("更新清单缺少 components.desktop.avalonia");
    }
}
