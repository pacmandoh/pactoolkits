using System.Text.Json.Nodes;
using PacToolkits.Agents.Contracts.Settings;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class ModuleSettingsEditorTests
{
    [Fact]
    public void Validates_string_list_after_form_round_trip()
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
                            Type = ModuleSettingsFieldTypes.StringList,
                            Label = "目标进程",
                        },
                    ],
                },
            ],
        };
        var settings = ModuleSettingsEditor.ParseSettings(
            """
            {"AppWin":["app.exe","other.exe"]}
            """);
        var editor = new ModuleSettingsEditor("Sample", "Sample", schema, settings);

        Assert.Null(editor.Validate());
        Assert.Equal(
            ["app.exe", "other.exe"],
            editor.Sections[0].Fields[0].ListItems.Select(static i => i.Value).ToArray());
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
        mode.SelectedOption = mode.Options.First(option =>
            string.Equals(option.Value, "Fast", StringComparison.Ordinal));

        editor.RestoreField(savedJson, mode);

        Assert.Equal("Safe", mode.SelectedOption?.Value);
        Assert.Equal("Safe", mode.SelectedOption?.Display);
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
        mode.SelectedOption = mode.Options.First(option =>
            string.Equals(option.Value, "Fast", StringComparison.Ordinal));

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

        Assert.Equal("Safe", mode.SelectedOption?.Value);
        Assert.Equal("Safe", mode.SelectedOption?.Display);
    }

    [Fact]
    public void Shows_enum_option_labels_while_saving_values()
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
                            Key = "CodePickPolicy",
                            Type = ModuleSettingsFieldTypes.Enum,
                            Label = "选码策略",
                            Options =
                            [
                                new ModuleSettingsOption { Value = "MAX_LEVEL", Label = "按最大码" },
                                new ModuleSettingsOption { Value = "MIN_LEVEL", Label = "按最小码" },
                            ],
                        },
                    ],
                },
            ],
        };
        var editor = new ModuleSettingsEditor(
            "Injector",
            "Injector",
            schema,
            ModuleSettingsEditor.ParseSettings("""{"CodePickPolicy":"MIN_LEVEL"}"""));
        var field = editor.Sections[0].Fields.Single();
        Assert.Equal("MIN_LEVEL", field.SelectedOption?.Value);
        Assert.Equal("按最小码", field.SelectedOption?.Display);

        var result = JsonNode.Parse(editor.ApplyField(
            """{"CodePickPolicy":"MIN_LEVEL"}""",
            field))!.AsObject();
        Assert.Equal("MIN_LEVEL", result["CodePickPolicy"]!.GetValue<string>());
    }

    [Fact]
    public void Round_trips_col_field_list()
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
                            Key = "ColFields",
                            Type = ModuleSettingsFieldTypes.ColFieldList,
                            Label = "列映射",
                        },
                    ],
                },
            ],
        };
        const string saved = """
            {
              "ColFields": [
                {
                  "id": "drugName",
                  "label": "药品名称",
                  "headers": ["物资名称", "药品名称"],
                  "required": true,
                  "asInt": false,
                  "locked": true
                }
              ]
            }
            """;
        var editor = new ModuleSettingsEditor(
            "Injector",
            "Injector",
            schema,
            ModuleSettingsEditor.ParseSettings(saved));
        var field = editor.Sections[0].Fields.Single();
        var drug = field.ColFields.Single();
        Assert.Equal("drugName", drug.Id);
        Assert.Equal("药品名称", drug.DisplayTitle);
        Assert.True(drug.IsLocked);
        Assert.False(drug.CanEdit);
        Assert.Equal(["物资名称", "药品名称"], drug.ParseHeaders());

        field.RemoveColFieldCommand.Execute(drug);
        Assert.Single(field.ColFields);

        drug.HeadersText = "物资名称 / 药品名称 / 品名";
        field.AddColFieldCommand.Execute(null);
        Assert.Equal(2, field.ColFields.Count);
        var custom = field.ColFields[0];
        Assert.False(custom.IsLocked);
        Assert.True(custom.CanEdit);
        Assert.StartsWith("col_", custom.Id);
        Assert.Equal("自定义", custom.DisplayTitle);
        custom.Id = "myExtra";
        custom.HeadersText = "额外列";

        var rows = editor.ToJsonObject()["ColFields"]!.AsArray();
        Assert.Equal(2, rows.Count);
        Assert.Equal("myExtra", rows[0]!["id"]!.GetValue<string>());
        Assert.Equal("自定义", rows[0]!["label"]!.GetValue<string>());
        Assert.Null(rows[0]!["locked"]);
        Assert.Equal(["额外列"], rows[0]!["headers"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Equal("药品名称", rows[1]!["label"]!.GetValue<string>());
        Assert.True(rows[1]!["locked"]!.GetValue<bool>());
        Assert.Equal(
            ["物资名称", "药品名称", "品名"],
            rows[1]!["headers"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Null(editor.Validate());
    }

    [Fact]
    public void Rejects_col_field_with_blank_id_without_rewriting()
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
                            Key = "ColFields",
                            Type = ModuleSettingsFieldTypes.ColFieldList,
                            Label = "列映射",
                        },
                    ],
                },
            ],
        };
        var editor = new ModuleSettingsEditor(
            "Injector",
            "Injector",
            schema,
            ModuleSettingsEditor.ParseSettings(
                """{"ColFields":[{"id":"custom","headers":["A"],"required":false,"asInt":false}]}"""));
        var row = editor.Sections[0].Fields.Single().ColFields.Single();
        row.Id = "   ";

        var json = editor.ToJsonObject()["ColFields"]!.AsArray().Single()!;
        Assert.Equal("", json["id"]!.GetValue<string>());
        var error = editor.Validate();
        Assert.NotNull(error);
        Assert.Contains("id", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_col_field_with_empty_headers_instead_of_dropping_row()
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
                            Key = "ColFields",
                            Type = ModuleSettingsFieldTypes.ColFieldList,
                            Label = "列映射",
                        },
                    ],
                },
            ],
        };
        const string saved = """
            {
              "ColFields": [
                {
                  "id": "drugName",
                  "label": "药品名称",
                  "headers": ["物资名称"],
                  "required": true,
                  "asInt": false,
                  "locked": true
                }
              ]
            }
            """;
        var editor = new ModuleSettingsEditor(
            "Injector",
            "Injector",
            schema,
            ModuleSettingsEditor.ParseSettings(saved));
        var drug = editor.Sections[0].Fields.Single().ColFields.Single();
        drug.HeadersText = "   ";

        var rows = editor.ToJsonObject()["ColFields"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("drugName", rows[0]!["id"]!.GetValue<string>());
        Assert.Empty(rows[0]!["headers"]!.AsArray());
        Assert.NotNull(editor.Validate());
        Assert.Contains("headers", editor.Validate()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_incomplete_col_field_rows_when_restoring()
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
                            Key = "ColFields",
                            Type = ModuleSettingsFieldTypes.ColFieldList,
                            Label = "列映射",
                            AllowEmpty = true,
                        },
                    ],
                },
            ],
        };
        const string saved = """
            {
              "ColFields": [
                {
                  "id": "broken",
                  "label": "坏行",
                  "headers": [],
                  "required": true,
                  "asInt": false,
                  "locked": true
                }
              ]
            }
            """;
        var editor = new ModuleSettingsEditor(
            "Injector",
            "Injector",
            schema,
            ModuleSettingsEditor.ParseSettings(saved));
        var field = editor.Sections[0].Fields.Single();
        Assert.Single(field.ColFields);
        Assert.Equal("broken", field.ColFields[0].Id);
        Assert.Empty(field.ColFields[0].ParseHeaders());
        Assert.NotNull(editor.Validate());
    }

    [Fact]
    public void Round_trips_injector_default_module_settings()
    {
        var moduleDir = Path.Combine(RepoRoot(), "runtime", "agents", "modules", "injector");
        var schemaPath = Path.Combine(moduleDir, "settings.schema.json");
        var settingsPath = Path.Combine(moduleDir, "settings.json");
        Assert.True(File.Exists(schemaPath), schemaPath);
        Assert.True(File.Exists(settingsPath), settingsPath);

        var schema = ModuleSettingsEditor.ParseSchema(File.ReadAllText(schemaPath));
        Assert.NotNull(schema);
        var settings = ModuleSettingsEditor.ParseSettings(File.ReadAllText(settingsPath));
        var editor = new ModuleSettingsEditor("injector", "Injector", schema!, settings);

        Assert.Null(editor.Validate());

        var colField = editor.Sections
            .SelectMany(static s => s.Fields)
            .Single(static f => f.Key == "ColFields");
        Assert.True(colField.IsColFieldList);
        Assert.Equal(9, colField.ColFields.Count);
        Assert.Contains(colField.ColFields, static r => r.Id == "billNo" && r.IsLocked);
        Assert.Contains(colField.ColFields, static r => r.Id == "batchNo");

        var appWin = editor.Sections
            .SelectMany(static s => s.Fields)
            .Single(static f => f.Key == "AppWin");
        Assert.True(appWin.IsStringList);
        Assert.False(appWin.IsStringFlagMap);
        Assert.Equal(2, appWin.ListItems.Count);

        var policy = editor.Sections
            .SelectMany(static s => s.Fields)
            .Single(static f => f.Key == "CodePickPolicy");
        Assert.Equal("MAX_LEVEL", policy.SelectedOption?.Value);
        Assert.Equal("按最大码", policy.SelectedOption?.Display);

        var json = editor.ToJsonObject();
        Assert.Null(json["ColSpecs"]);
        Assert.Null(json["IntCols"]);
        Assert.Null(json["WarehouseTaskIdentifier"]);
        Assert.True(json["AppWin"] is JsonArray);
        Assert.Equal(9, json["ColFields"]!.AsArray().Count);
        Assert.Contains(
            json["ColFields"]!.AsArray(),
            static n => n?["id"]?.GetValue<string>() == "drugName"
                && n?["locked"]?.GetValue<bool>() == true);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PacToolkits.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found from " + AppContext.BaseDirectory);
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
