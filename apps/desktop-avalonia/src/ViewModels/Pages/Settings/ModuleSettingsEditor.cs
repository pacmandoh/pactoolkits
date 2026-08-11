using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Agents.Contracts.Settings;
using PacToolkits.Agents.Contracts.Validation;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// 将模块设置 schema 和用户配置转换为可编辑字段，并保留 schema 未声明的配置项
/// </summary>
public sealed partial class ModuleSettingsEditor : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ModuleSettingsSchema _schema;
    private JsonObject _source;

    public ModuleSettingsEditor(
        string moduleId,
        string displayName,
        ModuleSettingsSchema schema,
        JsonObject settings)
    {
        ModuleId = moduleId;
        DisplayName = displayName;
        _schema = schema;
        _source = (JsonObject)settings.DeepClone();
        Title = string.IsNullOrWhiteSpace(schema.Title) ? displayName : schema.Title;
        Sections = schema.Sections
            .Select(section => ModuleSettingsSectionViewModel.From(section, settings))
            .ToArray();
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string Title { get; }

    public IReadOnlyList<ModuleSettingsSectionViewModel> Sections { get; }

    public JsonObject ToJsonObject()
    {
        var root = (JsonObject)_source.DeepClone();
        foreach (var section in Sections)
        {
            foreach (var field in section.Fields)
            {
                root[field.Key] = field.ToJsonNode();
            }
        }

        return root;
    }

    public string ToJsonString()
        => ToJsonObject().ToJsonString(JsonOptions);

    internal string ApplyField(string json, ModuleSettingsFieldViewModel field)
    {
        if (!Sections.SelectMany(section => section.Fields).Contains(field))
        {
            throw new ArgumentException("Field does not belong to this module editor", nameof(field));
        }

        if (!field.IsBool && !field.IsEnum)
        {
            throw new ArgumentException("Field does not support auto-save", nameof(field));
        }

        var root = ParseSettings(json);
        root[field.Key] = field.ToJsonNode();
        var result = ModuleSettingsValidator.Validate(_schema, root);
        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Message);
        }

        return root.ToJsonString(JsonOptions);
    }

    internal void RestoreField(string json, ModuleSettingsFieldViewModel field)
    {
        var root = ParseSettings(json);
        root.TryGetPropertyValue(field.Key, out var node);
        field.RestoreValue(node);
    }

    // 磁盘热更新：保留编辑器实例，只回写变化字段，避免 ContentControl 重建导致滚动回顶
    internal void ApplySettings(JsonObject settings)
    {
        _source = (JsonObject)settings.DeepClone();
        foreach (var field in Sections.SelectMany(section => section.Fields))
        {
            settings.TryGetPropertyValue(field.Key, out var node);
            field.RestoreValue(node);
        }
    }

    public string? Validate()
    {
        var result = ModuleSettingsValidator.Validate(_schema, ToJsonObject());
        return result.Ok ? null : result.Message;
    }

    public static ModuleSettingsSchema? ParseSchema(string? json)
        => ModuleSettingsValidator.TryParseSchema(json);

    public static JsonObject ParseSettings(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Settings root must be a JSON object");
        return node;
    }
}

public sealed class ModuleSettingsSectionViewModel
{
    public required string Title { get; init; }

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public required IReadOnlyList<ModuleSettingsFieldViewModel> Fields { get; init; }

    public static ModuleSettingsSectionViewModel From(ModuleSettingsSection section, JsonObject settings)
    {
        return new ModuleSettingsSectionViewModel
        {
            Title = section.Title,
            Fields = section.Fields
                .Select(field => ModuleSettingsFieldViewModel.From(field, settings))
                .ToArray(),
        };
    }
}

public sealed partial class ModuleSettingsFieldViewModel : ObservableObject
{
    public required string Key { get; init; }

    public required string Type { get; init; }

    public required string Label { get; init; }

    public string? Description { get; init; }

    public int? Min { get; init; }

    public int? Max { get; init; }

    public IReadOnlyList<ModuleSettingsOption> Options { get; init; } = [];

    public bool IsString => Type == ModuleSettingsFieldTypes.String;

    public bool IsBool => Type == ModuleSettingsFieldTypes.Bool;

    public bool IsInt => Type == ModuleSettingsFieldTypes.Int;

    public bool IsEnum => Type == ModuleSettingsFieldTypes.Enum;

    public bool IsStringList => Type == ModuleSettingsFieldTypes.StringList;

    public bool IsStringFlagMap => Type == ModuleSettingsFieldTypes.StringFlagMap;

    public bool IsColFieldList => Type == ModuleSettingsFieldTypes.ColFieldList;

    /// <summary>
    /// 单值字段：左侧 label，右侧控件
    /// </summary>
    public bool IsScalar => IsString || IsBool || IsInt || IsEnum;

    /// <summary>
    /// 行列表字段：stringList / stringFlagMap 共用行编辑器
    /// </summary>
    public bool IsLineList => IsStringList || IsStringFlagMap;

    [ObservableProperty] private string _stringValue = string.Empty;

    [ObservableProperty] private bool _boolValue;

    [ObservableProperty] private int _intValue;

    [ObservableProperty] private ModuleSettingsOption? _selectedOption;

    public ObservableCollection<SettingsLineItem> ListItems { get; } = new();

    public ObservableCollection<SettingsColFieldItem> ColFields { get; } = new();

    public static ModuleSettingsFieldViewModel From(ModuleSettingsField field, JsonObject settings)
    {
        var type = (field.Type ?? ModuleSettingsFieldTypes.String).Trim();
        var viewModel = new ModuleSettingsFieldViewModel
        {
            Key = field.Key,
            Type = type,
            Label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label,
            Description = field.Description,
            Min = field.Min,
            Max = field.Max,
            Options = field.Options ?? [],
        };

        settings.TryGetPropertyValue(field.Key, out var node);
        viewModel.RestoreValue(node);
        return viewModel;
    }

    [RelayCommand]
    private void AddListItem()
        => ListItems.Add(new SettingsLineItem());

    [RelayCommand]
    private void RemoveListItem(SettingsLineItem? item)
    {
        if (item is not null)
        {
            ListItems.Remove(item);
        }
    }

    [RelayCommand]
    private void AddColField()
    {
        var id = SettingsColFieldItem.NewUniqueId(ColFields.Select(static r => r.Id));
        ColFields.Insert(
            0,
            new SettingsColFieldItem(id, "自定义", [], required: false, asInt: false, isLocked: false));
    }

    [RelayCommand]
    private void RemoveColField(SettingsColFieldItem? item)
    {
        if (item is not null && item.CanEdit)
        {
            ColFields.Remove(item);
        }
    }

    public JsonNode? ToJsonNode()
        => Type switch
        {
            ModuleSettingsFieldTypes.Bool => JsonValue.Create(BoolValue),
            ModuleSettingsFieldTypes.Int => JsonValue.Create(IntValue),
            ModuleSettingsFieldTypes.Enum => JsonValue.Create(SelectedOption?.Value),
            ModuleSettingsFieldTypes.StringList => ToStringArray(),
            ModuleSettingsFieldTypes.StringFlagMap => ToFlagMap(),
            ModuleSettingsFieldTypes.ColFieldList => ToColFieldArray(),
            _ => JsonValue.Create(StringValue),
        };

    internal void RestoreValue(JsonNode? node)
    {
        if (IsBool)
        {
            var next = node?.GetValue<bool>() ?? false;
            if (BoolValue != next)
            {
                BoolValue = next;
            }

            return;
        }

        if (IsInt)
        {
            var next = node?.GetValue<int>() ?? Min ?? 0;
            if (IntValue != next)
            {
                IntValue = next;
            }

            return;
        }

        if (IsEnum)
        {
            var next = ResolveOption(node, Options);
            if (!ReferenceEquals(SelectedOption, next))
            {
                SelectedOption = next;
            }

            return;
        }

        if (IsStringList)
        {
            ReplaceListItems(ReadStringList(node as JsonArray));
            return;
        }

        if (IsStringFlagMap)
        {
            ReplaceListItems(ReadEnabledFlagKeys(node as JsonObject));
            return;
        }

        if (IsColFieldList)
        {
            ReplaceColFields(ReadColFields(node as JsonArray));
            return;
        }

        var text = node?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(StringValue, text, StringComparison.Ordinal))
        {
            StringValue = text;
        }
    }

    private void ReplaceListItems(IReadOnlyList<string> next)
    {
        if (ListItems.Count == next.Count
            && ListItems
                .Select(item => item.Value ?? string.Empty)
                .SequenceEqual(next, StringComparer.Ordinal))
        {
            return;
        }

        // 逐项 Remove，便于 CollectionChanged 携带 OldItems 解绑（Clear 仅 Reset）
        for (var i = ListItems.Count - 1; i >= 0; i--)
        {
            ListItems.RemoveAt(i);
        }

        foreach (var text in next)
        {
            ListItems.Add(new SettingsLineItem(text));
        }
    }

    private void ReplaceColFields(IReadOnlyList<SettingsColFieldItem> next)
    {
        if (ColFields.Count == next.Count
            && ColFields.Zip(next, static (a, b) => a.SameAs(b)).All(static same => same))
        {
            return;
        }

        for (var i = ColFields.Count - 1; i >= 0; i--)
        {
            ColFields.RemoveAt(i);
        }

        foreach (var item in next)
        {
            ColFields.Add(item);
        }
    }

    private static IReadOnlyList<string> ReadStringList(JsonArray? list)
    {
        if (list is null || list.Count == 0)
        {
            return [];
        }

        var values = new List<string>(list.Count);
        foreach (var item in list)
        {
            var text = item?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ReadEnabledFlagKeys(JsonObject? map)
    {
        if (map is null || map.Count == 0)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var kv in map)
        {
            // 表单只编辑启用项，保留关闭项会在序列化时错误地改写为启用
            if (!string.IsNullOrWhiteSpace(kv.Key)
                && ModuleSettingsFlag.TryRead(kv.Value, out var enabled)
                && enabled)
            {
                values.Add(kv.Key);
            }
        }

        return values;
    }

    private static IReadOnlyList<SettingsColFieldItem> ReadColFields(JsonArray? list)
    {
        if (list is null || list.Count == 0)
        {
            return [];
        }

        var values = new List<SettingsColFieldItem>();
        foreach (var item in list)
        {
            if (item is not JsonObject row)
            {
                continue;
            }

            // 保留不完整行，由 Validate 报错，避免静默丢配置
            var id = ReadJsonString(row["id"]);
            var label = ReadJsonString(row["label"]);
            var headers = new List<string>();
            if (row["headers"] is JsonArray arr)
            {
                foreach (var h in arr)
                {
                    var text = ReadJsonString(h);
                    if (text.Length > 0)
                    {
                        headers.Add(text);
                    }
                }
            }

            var required = true;
            if (row["required"] is JsonValue reqVal && reqVal.TryGetValue(out bool reqBool))
            {
                required = reqBool;
            }

            var asInt = false;
            if (row["asInt"] is JsonValue asIntVal && asIntVal.TryGetValue(out bool asIntBool))
            {
                asInt = asIntBool;
            }

            var isLocked = row["locked"] is JsonValue lockedVal
                && lockedVal.TryGetValue(out bool lockedBool)
                && lockedBool;

            values.Add(new SettingsColFieldItem(id, label, headers, required, asInt, isLocked));
        }

        return values;
    }

    private static string ReadJsonString(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue(out string? text))
        {
            return string.Empty;
        }

        return (text ?? string.Empty).Trim();
    }

    private static ModuleSettingsOption? ResolveOption(JsonNode? node, IReadOnlyList<ModuleSettingsOption> options)
    {
        if (options.Count == 0)
        {
            return null;
        }

        var value = node?.GetValue<string>()?.Trim();
        return options.FirstOrDefault(option =>
                   string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
               ?? options[0];
    }

    private JsonArray ToStringArray()
    {
        var arr = new JsonArray();
        foreach (var item in ListItems)
        {
            var text = item.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                arr.Add(text);
            }
        }

        return arr;
    }

    private JsonObject ToFlagMap()
    {
        var map = new JsonObject();
        foreach (var item in ListItems)
        {
            var text = item.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                map[text] = 1;
            }
        }

        return map;
    }

    private JsonArray ToColFieldArray()
    {
        var arr = new JsonArray();
        foreach (var item in ColFields)
        {
            // 原样序列化，空/重复 id 与空 headers 交 Validate，避免保存路径静默改写
            var row = new JsonObject
            {
                ["id"] = (item.Id ?? string.Empty).Trim(),
                ["label"] = (item.Label ?? string.Empty).Trim(),
                ["required"] = item.Required,
                ["asInt"] = item.AsInt,
            };
            if (item.IsLocked)
            {
                row["locked"] = true;
            }

            var headerArr = new JsonArray();
            foreach (var header in item.ParseHeaders())
            {
                headerArr.Add(header);
            }

            row["headers"] = headerArr;
            arr.Add(row);
        }

        return arr;
    }
}
