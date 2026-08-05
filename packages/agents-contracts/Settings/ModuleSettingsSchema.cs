namespace PacToolkits.Agents.Contracts.Settings;

/// <summary>
/// 模块设置页结构和业务配置校验规则
/// </summary>
public sealed class ModuleSettingsSchema
{
    public int SchemaVersion { get; set; } = 1;

    public string Title { get; set; } = string.Empty;

    public List<ModuleSettingsSection> Sections { get; set; } = [];
}

public sealed class ModuleSettingsSection
{
    public string Title { get; set; } = string.Empty;

    public List<ModuleSettingsField> Fields { get; set; } = [];
}

public sealed class ModuleSettingsField
{
    public string Key { get; set; } = string.Empty;

    public string Type { get; set; } = "string";

    public string Label { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// 枚举选项：字符串，或 { value, label }（见 <see cref="ModuleSettingsOption"/>）
    /// </summary>
    public List<ModuleSettingsOption>? Options { get; set; }

    public int? Min { get; set; }

    public int? Max { get; set; }

    public bool AllowEmpty { get; set; }
}

public static class ModuleSettingsFieldTypes
{
    public const string String = "string";
    public const string Bool = "bool";
    public const string Int = "int";
    public const string Enum = "enum";
    public const string StringList = "stringList";
    public const string StringFlagMap = "stringFlagMap";
    public const string ColFieldList = "colFieldList";

    internal static bool IsSupported(string type)
        => type is String or Bool or Int or Enum or StringList or StringFlagMap or ColFieldList;
}
