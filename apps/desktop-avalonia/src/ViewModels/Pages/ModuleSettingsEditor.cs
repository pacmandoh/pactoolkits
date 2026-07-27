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
/// 单个模块的 schema 驱动设置表单
/// </summary>
public sealed partial class ModuleSettingsEditor : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ModuleSettingsSchema _schema;
    private readonly JsonObject _source;

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
        foreach (var section in schema.Sections)
        {
            Sections.Add(ModuleSettingsSectionViewModel.From(section, settings));
        }
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string Title { get; }

    public ObservableCollection<ModuleSettingsSectionViewModel> Sections { get; } = new();

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

    public ObservableCollection<ModuleSettingsFieldViewModel> Fields { get; } = new();

    public static ModuleSettingsSectionViewModel From(ModuleSettingsSection section, JsonObject settings)
    {
        var viewModel = new ModuleSettingsSectionViewModel { Title = section.Title };
        foreach (var field in section.Fields)
        {
            viewModel.Fields.Add(ModuleSettingsFieldViewModel.From(field, settings));
        }

        return viewModel;
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

    public IReadOnlyList<string> Options { get; init; } = [];

    public bool IsString => Type == ModuleSettingsFieldTypes.String;

    public bool IsBool => Type == ModuleSettingsFieldTypes.Bool;

    public bool IsInt => Type == ModuleSettingsFieldTypes.Int;

    public bool IsEnum => Type == ModuleSettingsFieldTypes.Enum;

    public bool IsStringList => Type == ModuleSettingsFieldTypes.StringList;

    public bool IsStringFlagMap => Type == ModuleSettingsFieldTypes.StringFlagMap;

    [ObservableProperty] private string _stringValue = string.Empty;

    [ObservableProperty] private bool _boolValue;

    [ObservableProperty] private int _intValue;

    [ObservableProperty] private string? _selectedOption;

    public ObservableCollection<SettingsLineItem> ListItems { get; } = new();

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
        switch (type)
        {
            case ModuleSettingsFieldTypes.Bool:
                viewModel.BoolValue = node?.GetValue<bool>() ?? false;
                break;
            case ModuleSettingsFieldTypes.Int:
                viewModel.IntValue = node?.GetValue<int>() ?? field.Min ?? 0;
                break;
            case ModuleSettingsFieldTypes.Enum:
                viewModel.SelectedOption = ResolveOption(node, viewModel.Options);
                break;
            case ModuleSettingsFieldTypes.StringList:
                if (node is JsonArray list)
                {
                    foreach (var item in list)
                    {
                        var text = item?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            viewModel.ListItems.Add(new SettingsLineItem(text));
                        }
                    }
                }

                break;
            case ModuleSettingsFieldTypes.StringFlagMap:
                if (node is JsonObject map)
                {
                    foreach (var kv in map)
                    {
                        // 仅启用项进入表单，避免 Save 把 0/false 写成 1
                        if (!string.IsNullOrWhiteSpace(kv.Key)
                            && ModuleSettingsFlag.TryRead(kv.Value, out var enabled)
                            && enabled)
                        {
                            viewModel.ListItems.Add(new SettingsLineItem(kv.Key));
                        }
                    }
                }

                break;
            default:
                viewModel.StringValue = node?.GetValue<string>() ?? string.Empty;
                break;
        }

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

    public JsonNode? ToJsonNode()
        => Type switch
        {
            ModuleSettingsFieldTypes.Bool => JsonValue.Create(BoolValue),
            ModuleSettingsFieldTypes.Int => JsonValue.Create(IntValue),
            ModuleSettingsFieldTypes.Enum => JsonValue.Create(SelectedOption),
            ModuleSettingsFieldTypes.StringList => ToStringArray(),
            ModuleSettingsFieldTypes.StringFlagMap => ToFlagMap(),
            _ => JsonValue.Create(StringValue),
        };

    internal void RestoreValue(JsonNode? node)
    {
        if (IsBool)
        {
            BoolValue = node?.GetValue<bool>() ?? false;
        }
        else if (IsEnum)
        {
            SelectedOption = ResolveOption(node, Options);
        }
    }

    private static string ResolveOption(JsonNode? node, IReadOnlyList<string> options)
    {
        var value = node?.GetValue<string>()?.Trim();
        return options.FirstOrDefault(option =>
                   string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
               ?? value
               ?? options.FirstOrDefault()
               ?? string.Empty;
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

}
