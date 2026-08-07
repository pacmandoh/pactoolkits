using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Host/IPC/文件 共用 JSON 选项（状态枚举统一 string）
/// </summary>
public static class AgentsJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
