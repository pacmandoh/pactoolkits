using System.Text.Json.Nodes;
using PacToolkits.Agents.Contracts.Settings;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class ModuleSettingsEditorTests
{
    [Fact]
    public void Validates_string_flag_map_after_form_round_trip()
    {
        var schema = new ModuleSettingsSchema
        {
            Sections =
            [
                new ModuleSettingsSection
                {
                    Fields =
                    [
                        new ModuleSettingsField
                        {
                            Key = "AppWin",
                            Type = ModuleSettingsFieldTypes.StringFlagMap,
                            Label = "目标进程",
                        },
                    ],
                },
            ],
        };
        var settings = ModuleSettingsEditor.ParseSettings(
            """
            {"AppWin":{"app.exe":1}}
            """);
        var editor = new ModuleSettingsEditor("Sample", "Sample", schema, settings);

        var error = editor.Validate();

        Assert.Null(error);
    }

    [Fact]
    public void Applies_auto_save_field_without_persisting_other_drafts()
    {
        var schema = CreateAutoSaveSchema();
        const string savedJson = """
            {"Enabled":false,"Mode":"Safe","Name":"saved"}
            """;
        var editor = new ModuleSettingsEditor(
            "Scanner",
            "Scanner",
            schema,
            ModuleSettingsEditor.ParseSettings(savedJson));
        var fields = editor.Sections[0].Fields;
        fields.Single(field => field.Key == "Name").StringValue = "draft";
        var enabled = fields.Single(field => field.Key == "Enabled");
        enabled.BoolValue = true;

        var result = JsonNode.Parse(editor.ApplyField(savedJson, enabled))!.AsObject();

        Assert.True(result["Enabled"]!.GetValue<bool>());
        Assert.Equal("Safe", result["Mode"]!.GetValue<string>());
        Assert.Equal("saved", result["Name"]!.GetValue<string>());
    }

    [Fact]
    public void Restores_auto_save_field_from_persisted_json()
    {
        var schema = CreateAutoSaveSchema();
        const string savedJson = """
            {"Enabled":false,"Mode":"Safe","Name":"saved"}
            """;
        var editor = new ModuleSettingsEditor(
            "Scanner",
            "Scanner",
            schema,
            ModuleSettingsEditor.ParseSettings(savedJson));
        var mode = editor.Sections[0].Fields.Single(field => field.Key == "Mode");
        mode.SelectedOption = "Fast";

        editor.RestoreField(savedJson, mode);

        Assert.Equal("Safe", mode.SelectedOption);
    }

    [Fact]
    public void Applies_enum_auto_save_field()
    {
        var schema = CreateAutoSaveSchema();
        const string savedJson = """
            {"Enabled":false,"Mode":"Safe","Name":"saved"}
            """;
        var editor = new ModuleSettingsEditor(
            "Scanner",
            "Scanner",
            schema,
            ModuleSettingsEditor.ParseSettings(savedJson));
        var mode = editor.Sections[0].Fields.Single(field => field.Key == "Mode");
        mode.SelectedOption = "Fast";

        var result = JsonNode.Parse(editor.ApplyField(savedJson, mode))!.AsObject();

        Assert.Equal("Fast", result["Mode"]!.GetValue<string>());
        Assert.False(result["Enabled"]!.GetValue<bool>());
        Assert.Equal("saved", result["Name"]!.GetValue<string>());
    }

    [Fact]
    public void Canonicalizes_enum_value_to_schema_option()
    {
        var schema = CreateAutoSaveSchema();
        const string savedJson = """
            {"Enabled":false,"Mode":"safe","Name":"saved"}
            """;
        var editor = new ModuleSettingsEditor(
            "Scanner",
            "Scanner",
            schema,
            ModuleSettingsEditor.ParseSettings(savedJson));
        var mode = editor.Sections[0].Fields.Single(field => field.Key == "Mode");

        Assert.Equal("Safe", mode.SelectedOption);
    }

    private static ModuleSettingsSchema CreateAutoSaveSchema()
        => new()
        {
            Sections =
            [
                new ModuleSettingsSection
                {
                    Fields =
                    [
                        new ModuleSettingsField
                        {
                            Key = "Enabled",
                            Type = ModuleSettingsFieldTypes.Bool,
                            Label = "启用",
                        },
                        new ModuleSettingsField
                        {
                            Key = "Mode",
                            Type = ModuleSettingsFieldTypes.Enum,
                            Label = "模式",
                            Options = ["Safe", "Fast"],
                        },
                        new ModuleSettingsField
                        {
                            Key = "Name",
                            Type = ModuleSettingsFieldTypes.String,
                            Label = "名称",
                        },
                    ],
                },
            ],
        };
}
