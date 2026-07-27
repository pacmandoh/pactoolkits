using System.Text.Json.Nodes;
using PacToolkits.Agents.Contracts.Settings;
using PacToolkits.Agents.Contracts.Validation;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class ModuleSettingsValidatorTests
{
    [Fact]
    public void Validates_all_module_default_settings()
    {
        var modulesRoot = Path.Combine(RepoRoot(), "runtime", "agents", "modules");
        var moduleDirs = Directory.GetDirectories(modulesRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "module.json")))
            .OrderBy(static dir => dir, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(moduleDirs);

        foreach (var moduleDir in moduleDirs)
        {
            var schemaPath = Path.Combine(moduleDir, "settings.schema.json");
            var settingsPath = Path.Combine(moduleDir, "settings.json");
            Assert.True(File.Exists(schemaPath), schemaPath);
            Assert.True(File.Exists(settingsPath), settingsPath);

            var result = ModuleSettingsValidator.Validate(
                File.ReadAllText(schemaPath),
                File.ReadAllText(settingsPath));
            Assert.True(result.Ok, $"{Path.GetFileName(moduleDir)}: {result.Message}");
        }
    }

    [Fact]
    public void Rejects_empty_string_and_out_of_range_int()
    {
        var schema = new ModuleSettingsSchema
        {
            SchemaVersion = 1,
            Sections =
            [
                new ModuleSettingsSection
                {
                    Title = "t",
                    Fields =
                    [
                        new ModuleSettingsField
                        {
                            Key = "Name",
                            Type = ModuleSettingsFieldTypes.String,
                            Label = "名称",
                        },
                        new ModuleSettingsField
                        {
                            Key = "Timeout",
                            Type = ModuleSettingsFieldTypes.Int,
                            Label = "超时",
                            Min = 100,
                            Max = 1000,
                        },
                    ],
                },
            ],
        };

        var emptyName = ModuleSettingsValidator.Validate(
            schema,
            new JsonObject { ["Name"] = "  ", ["Timeout"] = 200 });
        Assert.False(emptyName.Ok);
        Assert.Contains("名称", emptyName.Message, StringComparison.Ordinal);

        var badInt = ModuleSettingsValidator.Validate(
            schema,
            new JsonObject { ["Name"] = "ok", ["Timeout"] = 50 });
        Assert.False(badInt.Ok);
        Assert.Contains("超时", badInt.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_invalid_enum_and_empty_list()
    {
        var schema = new ModuleSettingsSchema
        {
            SchemaVersion = 1,
            Sections =
            [
                new ModuleSettingsSection
                {
                    Title = "t",
                    Fields =
                    [
                        new ModuleSettingsField
                        {
                            Key = "Mode",
                            Type = ModuleSettingsFieldTypes.Enum,
                            Label = "模式",
                            Options = ["A", "B"],
                        },
                        new ModuleSettingsField
                        {
                            Key = "Items",
                            Type = ModuleSettingsFieldTypes.StringList,
                            Label = "列表",
                        },
                    ],
                },
            ],
        };

        var badEnum = ModuleSettingsValidator.Validate(
            schema,
            new JsonObject
            {
                ["Mode"] = "C",
                ["Items"] = new JsonArray("x"),
            });
        Assert.False(badEnum.Ok);

        var emptyList = ModuleSettingsValidator.Validate(
            schema,
            new JsonObject
            {
                ["Mode"] = "A",
                ["Items"] = new JsonArray(),
            });
        Assert.False(emptyList.Ok);
    }

    [Fact]
    public void Allows_empty_string_list_when_allowEmpty()
    {
        var schema = new ModuleSettingsSchema
        {
            SchemaVersion = 1,
            Sections =
            [
                new ModuleSettingsSection
                {
                    Title = "t",
                    Fields =
                    [
                        new ModuleSettingsField
                        {
                            Key = "IntCols",
                            Type = ModuleSettingsFieldTypes.StringList,
                            Label = "IntCols",
                            AllowEmpty = true,
                        },
                    ],
                },
            ],
        };

        var empty = ModuleSettingsValidator.Validate(
            schema,
            new JsonObject { ["IntCols"] = new JsonArray() });
        Assert.True(empty.Ok, empty.Message);

        var missing = ModuleSettingsValidator.Validate(schema, new JsonObject());
        Assert.False(missing.Ok);
        Assert.Contains("IntCols", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_int_values_created_for_string_flag_map()
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
                            Key = "Targets",
                            Type = ModuleSettingsFieldTypes.StringFlagMap,
                            Label = "目标进程",
                        },
                    ],
                },
            ],
        };
        var settings = new JsonObject
        {
            ["Targets"] = new JsonObject
            {
                ["enabled.exe"] = 1,
                ["disabled.exe"] = 0,
            },
        };

        var result = ModuleSettingsValidator.Validate(schema, settings);

        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public void Rejects_missing_bool_key()
    {
        var schema = new ModuleSettingsSchema
        {
            SchemaVersion = 1,
            Sections =
            [
                new ModuleSettingsSection
                {
                    Title = "t",
                    Fields =
                    [
                        new ModuleSettingsField
                        {
                            Key = "WarehouseEnabled",
                            Type = ModuleSettingsFieldTypes.Bool,
                            Label = "仓库模式",
                        },
                    ],
                },
            ],
        };

        var missing = ModuleSettingsValidator.Validate(schema, new JsonObject());
        Assert.False(missing.Ok);
        Assert.Contains("仓库模式", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_wrong_json_types_without_throwing()
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
                            Key = "Items",
                            Type = ModuleSettingsFieldTypes.StringList,
                            Label = "列表",
                        },
                    ],
                },
            ],
        };

        var result = ModuleSettingsValidator.Validate(
            schema,
            new JsonObject { ["Items"] = new JsonArray(1, "ok") });

        Assert.False(result.Ok);
        Assert.Contains("只能包含字符串", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_invalid_or_duplicate_schema_fields()
    {
        var invalidType =
            """
            {"schemaVersion":1,"sections":[{"fields":[{"key":"Mode","type":"integer"}]}]}
            """;
        var duplicate =
            """
            {"schemaVersion":1,"sections":[{"fields":[{"key":"Mode","type":"string"},{"key":"Mode","type":"string"}]}]}
            """;

        Assert.Null(ModuleSettingsValidator.TryParseSchema(invalidType));
        Assert.Null(ModuleSettingsValidator.TryParseSchema(duplicate));
    }

    [Fact]
    public void Rejects_empty_schema_sections()
    {
        const string noSections = """
            {"schemaVersion":1,"sections":[]}
            """;
        const string emptySection = """
            {"schemaVersion":1,"sections":[{"title":"空分组","fields":[]}]}
            """;

        Assert.Null(ModuleSettingsValidator.TryParseSchema(noSections));
        Assert.Null(ModuleSettingsValidator.TryParseSchema(emptySection));
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
}
