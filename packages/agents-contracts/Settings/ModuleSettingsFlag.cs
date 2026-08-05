using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PacToolkits.Agents.Contracts.Settings;

/// <summary>
/// 解析 stringFlagMap 的启用值（bool / 数字 / 常见真值字符串）
/// </summary>
public static class ModuleSettingsFlag
{
    public static bool TryRead(JsonNode? node, out bool enabled)
    {
        enabled = false;
        if (node is not JsonValue value)
        {
            return false;
        }

        var kind = value.GetValueKind();
        if (kind is JsonValueKind.True or JsonValueKind.False)
        {
            enabled = kind == JsonValueKind.True;
            return true;
        }

        if (kind == JsonValueKind.Number)
        {
            if (!double.TryParse(
                    value.ToJsonString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number))
            {
                return false;
            }

            enabled = number != 0;
            return true;
        }

        if (kind == JsonValueKind.String && value.TryGetValue(out string? text))
        {
            enabled = (text ?? string.Empty).Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
            return true;
        }

        return false;
    }
}
