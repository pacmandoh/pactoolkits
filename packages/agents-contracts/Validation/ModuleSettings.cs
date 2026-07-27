using System.Text.Json;
using System.Text.Json.Nodes;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Agents.Contracts.Settings;

namespace PacToolkits.Agents.Contracts.Validation;

/// <summary>
/// 按 settings.schema.json 校验模块业务 settings（与 Settings 表单字段规则对齐）
/// </summary>
public static class ModuleSettingsValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ModuleSettingsSchema? TryParseSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var schema = JsonSerializer.Deserialize<ModuleSettingsSchema>(json, JsonOptions);
            return schema is not null && ValidateSchema(schema) is null ? schema : null;
        }
        catch
        {
            return null;
        }
    }

    public static AgentsCommandResult Validate(string? schemaJson, string settingsJson)
    {
        var schema = TryParseSchema(schemaJson);
        if (schema is null)
        {
            return new AgentsCommandResult(false, "模块 settings.schema.json 无效或缺失");
        }

        JsonObject settings;
        try
        {
            settings = JsonNode.Parse(settingsJson) as JsonObject
                       ?? throw new InvalidOperationException("settings root must be a JSON object");
        }
        catch (Exception ex)
        {
            return new AgentsCommandResult(false, $"模块 settings 不是合法 JSON 对象：{ex.Message}");
        }

        return Validate(schema, settings);
    }

    public static AgentsCommandResult Validate(ModuleSettingsSchema schema, JsonObject settings)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(settings);

        var schemaError = ValidateSchema(schema);
        if (schemaError is not null)
        {
            return new AgentsCommandResult(false, schemaError);
        }

        foreach (var section in schema.Sections ?? [])
        {
            foreach (var field in section.Fields ?? [])
            {
                if (string.IsNullOrWhiteSpace(field.Key))
                {
                    continue;
                }

                settings.TryGetPropertyValue(field.Key, out var node);
                var error = ValidateField(field, node);
                if (error is not null)
                {
                    return new AgentsCommandResult(false, error);
                }
            }
        }

        return new AgentsCommandResult(true, "ok");
    }

    private static string? ValidateField(ModuleSettingsField field, JsonNode? node)
    {
        var label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
        var type = (field.Type ?? ModuleSettingsFieldTypes.String).Trim();

        switch (type)
        {
            case ModuleSettingsFieldTypes.Bool:
                if (node is null)
                {
                    return $"{label} 不能为空";
                }

                if (node is JsonValue value && value.TryGetValue<bool>(out _))
                {
                    return null;
                }

                return $"{label} 必须是布尔值";

            case ModuleSettingsFieldTypes.Int:
                if (node is null || node is not JsonValue intValue || !intValue.TryGetValue(out int number))
                {
                    return $"{label} 必须是整数";
                }

                if (field.Min is int min && number < min)
                {
                    return $"{label} 不能小于 {min}";
                }

                if (field.Max is int max && number > max)
                {
                    return $"{label} 不能大于 {max}";
                }

                return null;

            case ModuleSettingsFieldTypes.Enum:
                if (!TryGetString(node, out var selected))
                {
                    return $"{label} 必须是字符串";
                }

                selected = selected.Trim();
                if (string.IsNullOrWhiteSpace(selected))
                {
                    return $"{label} 不能为空";
                }

                var options = field.Options ?? [];
                if (options.Count > 0
                    && !options.Any(o => string.Equals(o, selected, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"{label} 取值无效";
                }

                return null;

            case ModuleSettingsFieldTypes.StringList:
                // AllowEmpty 只允许空数组，不允许缺 key（与 AHK Util_CfgGetStringArray 对齐）
                if (node is null)
                {
                    return $"{label} 不能为空";
                }

                if (node is not JsonArray list)
                {
                    return $"{label} 必须是字符串列表";
                }

                if (list.Count == 0)
                {
                    return field.AllowEmpty ? null : $"{label} 不能为空";
                }

                var hasValue = false;
                foreach (var item in list)
                {
                    if (!TryGetString(item, out var listText))
                    {
                        return $"{label} 只能包含字符串";
                    }

                    hasValue |= !string.IsNullOrWhiteSpace(listText);
                }

                if (!field.AllowEmpty && !hasValue)
                {
                    return $"{label} 不能为空";
                }

                return null;

            case ModuleSettingsFieldTypes.StringFlagMap:
                if (node is null)
                {
                    return $"{label} 不能为空";
                }

                if (node is not JsonObject map)
                {
                    return $"{label} 必须是字符串映射";
                }

                if (map.Count == 0)
                {
                    return field.AllowEmpty ? null : $"{label} 不能为空";
                }

                var hasEnabledValue = false;
                foreach (var (key, flagValue) in map)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        return $"{label} 不能包含空键";
                    }

                    if (!ModuleSettingsFlag.TryRead(flagValue, out var enabled))
                    {
                        return $"{label} 的值必须是布尔值、数字或布尔字符串";
                    }

                    hasEnabledValue |= enabled;
                }

                if (!field.AllowEmpty && !hasEnabledValue)
                {
                    return $"{label} 不能为空";
                }

                return null;

            default:
                if (!TryGetString(node, out var stringText))
                {
                    return $"{label} 必须是字符串";
                }

                stringText = stringText.Trim();
                if (string.IsNullOrWhiteSpace(stringText))
                {
                    return $"{label} 不能为空";
                }

                return null;
        }
    }

    private static string? ValidateSchema(ModuleSettingsSchema schema)
    {
        if (schema.SchemaVersion != 1)
        {
            return $"模块 settings schema 版本不受支持：{schema.SchemaVersion}（仅支持 schemaVersion=1）";
        }

        if (schema.Sections is null || schema.Sections.Count == 0)
        {
            return "模块 settings schema 缺少有效 sections";
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in schema.Sections)
        {
            if (section?.Fields is null || section.Fields.Count == 0)
            {
                return "模块 settings schema 的 section 缺少有效 fields";
            }

            foreach (var field in section.Fields)
            {
                if (field is null || string.IsNullOrWhiteSpace(field.Key))
                {
                    return "模块 settings schema 包含空字段 key";
                }

                if (!keys.Add(field.Key))
                {
                    return $"模块 settings schema 包含重复字段：{field.Key}";
                }

                var type = (field.Type ?? string.Empty).Trim();
                if (!ModuleSettingsFieldTypes.IsSupported(type))
                {
                    return $"模块 settings schema 字段类型不受支持：{field.Key}={type}";
                }

                if (field.Min is int min && field.Max is int max && min > max)
                {
                    return $"模块 settings schema 字段范围无效：{field.Key}";
                }

                if (type == ModuleSettingsFieldTypes.Enum)
                {
                    var options = field.Options?
                        .Where(static option => !string.IsNullOrWhiteSpace(option))
                        .ToList() ?? [];
                    if (options.Count == 0
                        || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
                    {
                        return $"模块 settings schema 枚举选项无效：{field.Key}";
                    }
                }
            }
        }

        return null;
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        value = string.Empty;
        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
        {
            return false;
        }

        value = text ?? string.Empty;
        return true;
    }
}
