using System.Text.Json;
using PacToolkits.Agent.Contracts.Agents;

namespace PacToolkits.Agent.Contracts.Models;

public enum AgentTaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Discarded
}

/// <summary>
/// Runtime launch parameters shared between desktop UI and the AHK agent process.
/// </summary>
public sealed class AgentRuntimeConfig
{
    public string ExecutablePath { get; set; } = AgentPaths.InjectorAhkExecutable;

    public string ProcessName { get; set; } = string.Empty;

    public string UnifiedConfigPath { get; set; } = string.Empty;

    public string AgentVersion { get; set; } = string.Empty;
}

public sealed class AgentInstanceConfig
{
    public bool Enabled { get; set; } = true;

    public string ExecutablePath { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public Dictionary<string, object?> Runtime { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Settings { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AutomationToolsOptions
{
    public AhkToolOptions Ahk { get; set; } = new();
    public AgentToolOptions Agent { get; set; } = new();
}

public sealed class AhkToolOptions
{
    public string ExecutablePath { get; set; } = AgentPaths.InjectorAhkExecutable;
    public string ProcessName { get; set; } = string.Empty;
}

public sealed class AgentToolOptions
{
    public string PgDriver { get; set; } = "PostgreSQL Unicode(x64)";
    public string PgSsl { get; set; } = "disable";
    public string OptWindowClass { get; set; } = "TFrm_mzcffy";
    public string IptWindowClass { get; set; } = "Tfrm_wzzsm";
    public Dictionary<string, int> AppWin { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["互慧软件.exe"] = 1,
        ["ProjectMain.exe"] = 1,
    };
    public int ConfirmTimeoutMs { get; set; } = 2500;
    public List<string> ColSpecs { get; set; } =
    [
        "?追溯码",
        "物资名称||药品名称",
        "规格||药品规格",
        "数量",
        "?单位",
        "?拆零标签||拆零",
    ];
    public List<string> IntCols { get; set; } = ["数量"];
    public string OptParseGridClassNN { get; set; } = "TcxGridSite2";
    public string OptVerifyGridClassNN { get; set; } = "TcxGridSite2";
    public string IptParseGridClassNN { get; set; } = "TcxGridSite2";
    public string IptVerifyGridClassNN { get; set; } = "TcxGridSite1";
    public string OptInputClassNN { get; set; } = "TMemo2";
    public string IptInputClassNN { get; set; } = "TEdit1";
    public bool WarehouseEnabled { get; set; }
    public List<string> WarehouseAnchorTexts { get; set; } = ["患者姓名", "应扫次数"];
    public string CodePickPolicy { get; set; } = "MAX_LEVEL";
    public string WarehouseTaskIdentifier { get; set; } = "单据号||当前编号";
}

public static class AgentSettingsSync
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static bool HasData(AgentToolOptions? agent)
    {
        if (agent is null)
        {
            return false;
        }

        var defaults = new AgentToolOptions();
        return !string.Equals(agent.PgDriver, defaults.PgDriver, StringComparison.Ordinal)
               || !string.Equals(agent.PgSsl, defaults.PgSsl, StringComparison.Ordinal)
               || agent.AppWin.Count > 0
               || agent.ColSpecs.Count > 0;
    }

    public static Dictionary<string, object?> ToSettings(AgentToolOptions agent)
    {
        var json = JsonSerializer.Serialize(agent, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var settings = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            settings[prop.Name] = prop.Value.Clone();
        }

        return settings;
    }

    public static AgentToolOptions FromSettings(IReadOnlyDictionary<string, object?>? settings)
    {
        return TryFromSettings(settings, out var options)
            ? options
            : new AgentToolOptions();
    }

    public static bool TryFromSettings(
        IReadOnlyDictionary<string, object?>? settings,
        out AgentToolOptions options)
    {
        options = new AgentToolOptions();
        if (settings is null || settings.Count == 0)
        {
            return true;
        }

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            options = JsonSerializer.Deserialize<AgentToolOptions>(json, JsonOptions) ?? new AgentToolOptions();
            return true;
        }
        catch (JsonException)
        {
            options = new AgentToolOptions();
            return false;
        }
    }

    public static bool SettingsMatch(IReadOnlyDictionary<string, object?>? left, AgentToolOptions right)
    {
        if (!TryFromSettings(left, out var fromLeft))
        {
            return false;
        }

        var normalizedLeft = JsonSerializer.Serialize(fromLeft, JsonOptions);
        var normalizedRight = JsonSerializer.Serialize(right, JsonOptions);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }
}
