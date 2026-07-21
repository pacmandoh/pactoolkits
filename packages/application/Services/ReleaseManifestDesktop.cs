using System.Text.Json;

namespace PacToolkits.Application.Services;

/// <summary>
/// 从发布清单 JSON 解析唯一 desktop 实现节点
/// </summary>
public static class ReleaseManifestDesktop
{
    public static bool TryGetImplementation(JsonElement components, out JsonElement desktop)
    {
        desktop = default;
        if (components.ValueKind != JsonValueKind.Object
            || !components.TryGetProperty("desktop", out var desktopRoot)
            || desktopRoot.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement? found = null;
        foreach (var property in desktopRoot.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (found is not null)
            {
                return false;
            }

            found = property.Value;
        }

        if (found is null)
        {
            return false;
        }

        desktop = found.Value;
        return true;
    }

    public static JsonElement GetRequiredImplementation(JsonElement components)
    {
        if (TryGetImplementation(components, out var desktop))
        {
            return desktop;
        }

        throw new InvalidOperationException("更新清单缺少唯一的 components.desktop.<impl> 实现块");
    }
}
