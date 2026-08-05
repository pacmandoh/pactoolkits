using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacToolkits.Agents.Contracts.Settings;

/// <summary>
/// 模块设置枚举项：存盘用 Value；页面展示用 Display（Label 或回退 Value）
/// </summary>
[JsonConverter(typeof(ModuleSettingsOptionJsonConverter))]
public sealed class ModuleSettingsOption
{
    public string Value { get; set; } = string.Empty;

    public string? Label { get; set; }

    [JsonIgnore]
    public string Display
    {
        get
        {
            var value = (Value ?? string.Empty).Trim();
            var label = (Label ?? string.Empty).Trim();
            return label.Length > 0 ? label : value;
        }
    }

    public static ModuleSettingsOption FromValue(string value)
    {
        var text = (value ?? string.Empty).Trim();
        return new ModuleSettingsOption { Value = text };
    }

    public static implicit operator ModuleSettingsOption(string value)
        => FromValue(value);
}

/// <summary>
/// options 项：字符串，或 { value, label }
/// </summary>
public sealed class ModuleSettingsOptionJsonConverter : JsonConverter<ModuleSettingsOption>
{
    public override ModuleSettingsOption Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ModuleSettingsOption.FromValue(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("enum option must be a string or { value, label } object");
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var value = root.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.String
            ? (valueProp.GetString() ?? string.Empty).Trim()
            : string.Empty;
        var label = root.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String
            ? (labelProp.GetString() ?? string.Empty).Trim()
            : string.Empty;
        if (label.Length == 0)
        {
            label = value;
        }

        return new ModuleSettingsOption { Value = value, Label = label };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ModuleSettingsOption value,
        JsonSerializerOptions options)
    {
        var raw = (value.Value ?? string.Empty).Trim();
        var label = (value.Label ?? string.Empty).Trim();
        if (label.Length == 0 || string.Equals(label, raw, StringComparison.Ordinal))
        {
            writer.WriteStringValue(raw);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("value", raw);
        writer.WriteString("label", label);
        writer.WriteEndObject();
    }
}
