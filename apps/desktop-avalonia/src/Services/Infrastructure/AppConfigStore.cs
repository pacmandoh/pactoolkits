using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public sealed class TraceCodeValidationOptions
{
    public int RequiredLength { get; set; } = 20;

    public string Pattern { get; set; } = "^8\\d+$";
}

public sealed class AppConfigRoot
{
    public int SchemaVersion { get; set; } = 1;
    public string LastDbMigrationAppVersion { get; set; } = string.Empty;
    public PgOptions Postgres { get; set; } = new();
    public Dictionary<string, string> ClientAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TraceCodeValidationOptions TraceCodeValidation { get; set; } = new();
    public AutomationToolsOptions AutomationTools { get; set; } = new();
    public MsfxApiOptions MsfxApi { get; set; } = new();
    public UiBehaviorOptions UiBehavior { get; set; } = new();
    public UpdateOptions Update { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

public sealed class MsfxApiOptions
{
    public string GatewayUrl { get; set; } = "https://eco.taobao.com/router/rest";
    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string RefEntId { get; set; } = string.Empty;
    public string DefaultMethod { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}

public sealed class UiBehaviorOptions
{
    public bool MinimizeToTrayOnClose { get; set; } = true;
}

public sealed class UpdateOptions
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = "stable";
    public string FeedUrl { get; set; } = "https://updates.pacdocs.com/feed/pactoolkits";
    public int AutoCheckIntervalMinutes { get; set; } = 0;
    public string IgnoredVersion { get; set; } = string.Empty;
}

public sealed class LoggingOptions
{
    public bool Enabled { get; set; } = true;
    public string MinimumLevel { get; set; } = "Error";
    public int RetentionDays { get; set; } = 14;
    public int MaxFileSizeMb { get; set; } = 20;
    public string LogDirectory { get; set; } = string.Empty;
}

public interface IAppConfigStore
{
    string ConfigPath { get; }
    AppConfigRoot Load();
    void Save(AppConfigRoot config);
    Task SaveAsync(AppConfigRoot config, CancellationToken ct = default);
    void Update(Action<AppConfigRoot> mutator);
    Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default);
}

public sealed class AppConfigStore : IAppConfigStore, IPostgresConfigStore
{
    private const string UnifiedConfigFileName = "PacToolkits.Desktop.Avalonia.config.json";
    private const string LegacyConfigFileName = "pactoolkits-ui.config.json";
    private static readonly string[] SupportedUpdateChannels = ["stable", "beta"];
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private readonly string _configDir;

    public string ConfigPath { get; }

    public AppConfigStore()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configDir = Path.Combine(baseDir, "PacToolkits");
        Directory.CreateDirectory(_configDir);
        ConfigPath = Path.Combine(_configDir, UnifiedConfigFileName);
        MigrateLegacyConfigIfNeeded(_configDir);

        EnsureConfigInitialized();
    }

    public AppConfigRoot Load()
    {
        lock (_gate)
        {
            var normalized = Normalize(ReadUnifiedOrDefault());
            PersistNormalizedIfNeeded(normalized);
            return normalized;
        }
    }

    public void Save(AppConfigRoot config)
    {
        _writeGate.Wait();
        try
        {
            var normalized = Normalize(config);
            var json = JsonSerializer.Serialize(normalized, _writeOptions);
            WriteAllTextAtomic(ConfigPath, json);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task SaveAsync(AppConfigRoot config, CancellationToken ct = default)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var normalized = Normalize(config);
            var json = JsonSerializer.Serialize(normalized, _writeOptions);
            await WriteAllTextAtomicAsync(ConfigPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Update(Action<AppConfigRoot> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        _writeGate.Wait();
        try
        {
            var cfg = Normalize(ReadUnifiedOrDefault());
            mutator(cfg);
            var normalized = Normalize(cfg);
            var json = JsonSerializer.Serialize(normalized, _writeOptions);
            WriteAllTextAtomic(ConfigPath, json);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var cfg = Normalize(ReadUnifiedOrDefault());
            mutator(cfg);
            var normalized = Normalize(cfg);
            var json = JsonSerializer.Serialize(normalized, _writeOptions);
            await WriteAllTextAtomicAsync(ConfigPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private AppConfigRoot ReadUnifiedOrDefault()
    {
        var readablePath = ResolveReadableConfigPath();
        try
        {
            if (!File.Exists(readablePath))
                return new AppConfigRoot();

            var json = File.ReadAllText(readablePath);
            return JsonSerializer.Deserialize<AppConfigRoot>(json) ?? new AppConfigRoot();
        }
        catch
        {
            return new AppConfigRoot();
        }
    }

    private string ResolveReadableConfigPath()
    {
        var primaryPath = Path.Combine(_configDir, UnifiedConfigFileName);
        if (File.Exists(primaryPath))
            return primaryPath;

        var legacyPath = Path.Combine(_configDir, LegacyConfigFileName);
        if (File.Exists(legacyPath))
            return legacyPath;

        return primaryPath;
    }

    private void EnsureConfigInitialized()
    {
        lock (_gate)
        {
            var readablePath = ResolveReadableConfigPath();
            string? existingJson = null;
            AppConfigRoot raw;
            try
            {
                if (File.Exists(readablePath))
                {
                    existingJson = File.ReadAllText(readablePath);
                    raw = JsonSerializer.Deserialize<AppConfigRoot>(existingJson) ?? new AppConfigRoot();
                }
                else
                {
                    raw = new AppConfigRoot();
                }
            }
            catch
            {
                raw = new AppConfigRoot();
                existingJson = null;
            }

            var normalized = Normalize(raw);
            if (File.Exists(readablePath)
                && HasPersistedDefaults(raw)
                && HasRequiredConfigKeys(existingJson))
            {
                if (!string.Equals(readablePath, ConfigPath, StringComparison.OrdinalIgnoreCase))
                    WriteAllTextAtomic(ConfigPath, JsonSerializer.Serialize(normalized, _writeOptions));
                return;
            }

            var json = JsonSerializer.Serialize(normalized, _writeOptions);
            WriteAllTextAtomic(ConfigPath, json);
        }
    }

    private static bool HasPersistedDefaults(AppConfigRoot? root)
    {
        if (root is null) return false;
        if (root.AutomationTools?.Agent is not { } a) return false;

        if (string.IsNullOrWhiteSpace(a.PgDriver)) return false;
        if (string.IsNullOrWhiteSpace(a.PgSsl)) return false;
        if (string.IsNullOrWhiteSpace(a.OptWindowClass)) return false;
        if (string.IsNullOrWhiteSpace(a.IptWindowClass)) return false;
        if (string.IsNullOrWhiteSpace(a.OptParseGridClassNN)) return false;
        if (string.IsNullOrWhiteSpace(a.OptVerifyGridClassNN)) return false;
        if (string.IsNullOrWhiteSpace(a.IptParseGridClassNN)) return false;
        if (string.IsNullOrWhiteSpace(a.IptVerifyGridClassNN)) return false;
        if (string.IsNullOrWhiteSpace(a.OptInputClassNN)) return false;
        if (string.IsNullOrWhiteSpace(a.IptInputClassNN)) return false;
        if (a.ConfirmTimeoutMs <= 0) return false;
        if (a.AppWin is null || a.AppWin.Count == 0) return false;
        if (a.ColSpecs is null || a.ColSpecs.Count == 0) return false;
        if (a.IntCols is null) return false;
        if (string.IsNullOrWhiteSpace(a.WarehouseTaskIdentifier)) return false;

        return true;
    }

    private static bool HasRequiredConfigKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("MsfxApi", out var msfx) || msfx.ValueKind != JsonValueKind.Object)
                return false;
            if (!msfx.TryGetProperty("RefEntId", out _))
                return false;

            if (!root.TryGetProperty("AutomationTools", out var automationTools) || automationTools.ValueKind != JsonValueKind.Object)
                return false;
            if (!automationTools.TryGetProperty("Agent", out var agent) || agent.ValueKind != JsonValueKind.Object)
                return false;
            if (!agent.TryGetProperty("WarehouseEnabled", out _))
                return false;
            if (!agent.TryGetProperty("WarehouseAnchorTexts", out _))
                return false;
            if (!agent.TryGetProperty("CodePickPolicy", out _))
                return false;
            if (!agent.TryGetProperty("WarehouseTaskIdentifier", out _))
                return false;
            if (!agent.TryGetProperty("OptWindowClass", out _))
                return false;
            if (!agent.TryGetProperty("IptWindowClass", out _))
                return false;
            if (!agent.TryGetProperty("OptParseGridClassNN", out _))
                return false;
            if (!agent.TryGetProperty("OptVerifyGridClassNN", out _))
                return false;
            if (!agent.TryGetProperty("IptParseGridClassNN", out _))
                return false;
            if (!agent.TryGetProperty("IptVerifyGridClassNN", out _))
                return false;
            if (!agent.TryGetProperty("OptInputClassNN", out _))
                return false;
            if (!agent.TryGetProperty("IptInputClassNN", out _))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppConfigRoot Normalize(AppConfigRoot? source)
    {
        var root = source ?? new AppConfigRoot();
        root.SchemaVersion = 1;
        root.LastDbMigrationAppVersion = (root.LastDbMigrationAppVersion ?? string.Empty).Trim();
        root.Postgres ??= new PgOptions();
        root.TraceCodeValidation ??= new TraceCodeValidationOptions();
        root.AutomationTools ??= new AutomationToolsOptions();
        root.MsfxApi ??= new MsfxApiOptions();
        root.AutomationTools.Ahk ??= new AhkToolOptions();
        root.AutomationTools.Agent ??= new AgentToolOptions();
        root.UiBehavior ??= new UiBehaviorOptions();
        root.Update ??= new UpdateOptions();
        root.Logging ??= new LoggingOptions();

        if (root.TraceCodeValidation.RequiredLength <= 0)
            root.TraceCodeValidation.RequiredLength = 20;

        if (string.IsNullOrWhiteSpace(root.TraceCodeValidation.Pattern))
            root.TraceCodeValidation.Pattern = "^8\\d+$";

        var ahkDefaults = new AhkToolOptions();
        root.AutomationTools.Ahk.ExecutablePath = string.IsNullOrWhiteSpace(root.AutomationTools.Ahk.ExecutablePath)
            ? ahkDefaults.ExecutablePath
            : root.AutomationTools.Ahk.ExecutablePath.Trim();
        root.AutomationTools.Ahk.ProcessName = string.IsNullOrWhiteSpace(root.AutomationTools.Ahk.ProcessName)
            ? ahkDefaults.ProcessName
            : root.AutomationTools.Ahk.ProcessName.Trim();
        root.AutomationTools.Agent = NormalizeAgent(root.AutomationTools.Agent);
        root.MsfxApi = NormalizeMsfxApi(root.MsfxApi);
        root.Update = NormalizeUpdate(root.Update);
        root.Logging = NormalizeLogging(root.Logging);

        root.ClientAliases = root.ClientAliases
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(
                kv => kv.Key.Trim(),
                kv => kv.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return root;
    }

    private static MsfxApiOptions NormalizeMsfxApi(MsfxApiOptions? source)
    {
        var defaults = new MsfxApiOptions();
        var options = source ?? new MsfxApiOptions();

        options.GatewayUrl = string.IsNullOrWhiteSpace(options.GatewayUrl)
            ? defaults.GatewayUrl
            : options.GatewayUrl.Trim();
        options.AppKey = (options.AppKey ?? string.Empty).Trim();
        options.AppSecret = (options.AppSecret ?? string.Empty).Trim();
        options.SessionToken = (options.SessionToken ?? string.Empty).Trim();
        options.RefEntId = (options.RefEntId ?? string.Empty).Trim();
        options.DefaultMethod = (options.DefaultMethod ?? string.Empty).Trim();
        if (options.TimeoutSeconds <= 0)
            options.TimeoutSeconds = defaults.TimeoutSeconds;
        if (options.TimeoutSeconds > 120)
            options.TimeoutSeconds = 120;

        return options;
    }

    private static UpdateOptions NormalizeUpdate(UpdateOptions? source)
    {
        var defaults = new UpdateOptions();
        var options = source ?? new UpdateOptions();

        options.Channel = NormalizeUpdateChannel(options.Channel, defaults.Channel);
        options.FeedUrl = string.IsNullOrWhiteSpace(options.FeedUrl) ? defaults.FeedUrl : options.FeedUrl.Trim();
        options.AutoCheckIntervalMinutes = options.AutoCheckIntervalMinutes < 0
            ? defaults.AutoCheckIntervalMinutes
            : Math.Clamp(options.AutoCheckIntervalMinutes, 0, 720);
        options.IgnoredVersion = (options.IgnoredVersion ?? string.Empty).Trim();
        return options;
    }

    private static LoggingOptions NormalizeLogging(LoggingOptions? source)
    {
        var defaults = new LoggingOptions();
        var options = source ?? new LoggingOptions();

        options.MinimumLevel = NormalizeLevel(options.MinimumLevel);
        options.RetentionDays = Math.Clamp(options.RetentionDays <= 0 ? defaults.RetentionDays : options.RetentionDays, 1, 180);
        options.MaxFileSizeMb = Math.Clamp(options.MaxFileSizeMb <= 0 ? defaults.MaxFileSizeMb : options.MaxFileSizeMb, 1, 200);
        options.LogDirectory = (options.LogDirectory ?? string.Empty).Trim();
        return options;
    }

    private static string NormalizeLevel(string? level)
    {
        var raw = (level ?? string.Empty).Trim().ToLowerInvariant();
        return raw switch
        {
            "debug" => "Debug",
            "info" => "Info",
            "warn" => "Warn",
            "error" => "Error",
            "fatal" => "Fatal",
            _ => "Error"
        };
    }

    private static AgentToolOptions NormalizeAgent(AgentToolOptions? source)
    {
        var defaults = new AgentToolOptions();
        var agent = source ?? new AgentToolOptions();

        agent.PgDriver = string.IsNullOrWhiteSpace(agent.PgDriver) ? defaults.PgDriver : agent.PgDriver.Trim();
        agent.PgSsl = NormalizePgSsl(agent.PgSsl, defaults.PgSsl);
        agent.OptWindowClass = string.IsNullOrWhiteSpace(agent.OptWindowClass) ? defaults.OptWindowClass : agent.OptWindowClass.Trim();
        agent.IptWindowClass = string.IsNullOrWhiteSpace(agent.IptWindowClass) ? defaults.IptWindowClass : agent.IptWindowClass.Trim();
        agent.OptParseGridClassNN = string.IsNullOrWhiteSpace(agent.OptParseGridClassNN) ? defaults.OptParseGridClassNN : agent.OptParseGridClassNN.Trim();
        agent.OptVerifyGridClassNN = string.IsNullOrWhiteSpace(agent.OptVerifyGridClassNN) ? defaults.OptVerifyGridClassNN : agent.OptVerifyGridClassNN.Trim();
        agent.IptParseGridClassNN = string.IsNullOrWhiteSpace(agent.IptParseGridClassNN) ? defaults.IptParseGridClassNN : agent.IptParseGridClassNN.Trim();
        agent.IptVerifyGridClassNN = string.IsNullOrWhiteSpace(agent.IptVerifyGridClassNN) ? defaults.IptVerifyGridClassNN : agent.IptVerifyGridClassNN.Trim();
        agent.OptInputClassNN = string.IsNullOrWhiteSpace(agent.OptInputClassNN) ? defaults.OptInputClassNN : agent.OptInputClassNN.Trim();
        agent.IptInputClassNN = string.IsNullOrWhiteSpace(agent.IptInputClassNN) ? defaults.IptInputClassNN : agent.IptInputClassNN.Trim();
        agent.ConfirmTimeoutMs = agent.ConfirmTimeoutMs <= 0 ? defaults.ConfirmTimeoutMs : agent.ConfirmTimeoutMs;

        var appWin = (agent.AppWin ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(
                kv => kv.Key.Trim(),
                kv => kv.Value == 0 ? 0 : 1,
                StringComparer.OrdinalIgnoreCase);
        agent.AppWin = appWin.Count > 0
            ? appWin
            : new Dictionary<string, int>(defaults.AppWin, StringComparer.OrdinalIgnoreCase);

        agent.ColSpecs = NormalizeStringList(agent.ColSpecs, defaults.ColSpecs, requireNonEmpty: true);
        agent.IntCols = NormalizeStringList(agent.IntCols, defaults.IntCols, requireNonEmpty: false);
        agent.WarehouseAnchorTexts = NormalizeStringList(agent.WarehouseAnchorTexts, defaults.WarehouseAnchorTexts, requireNonEmpty: true);
        agent.CodePickPolicy = NormalizeCodePickPolicy(agent.CodePickPolicy, defaults.CodePickPolicy);
        agent.WarehouseTaskIdentifier = string.IsNullOrWhiteSpace(agent.WarehouseTaskIdentifier)
            ? defaults.WarehouseTaskIdentifier
            : agent.WarehouseTaskIdentifier.Trim();

        return agent;
    }

    private static string NormalizeCodePickPolicy(string? value, string fallback)
    {
        var policy = (value ?? string.Empty).Trim().ToUpperInvariant();
        return policy is "MAX_LEVEL" or "MIN_LEVEL"
            ? policy
            : fallback;
    }

    private static string NormalizePgSsl(string? value, string fallback)
    {
        var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
        return mode switch
        {
            "enable" => "require",
            "disable" or "allow" or "prefer" or "require" or "verify-ca" or "verify-full" => mode,
            _ => fallback
        };
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? source, IEnumerable<string> fallback, bool requireNonEmpty)
    {
        var normalized = (source ?? Array.Empty<string>())
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requireNonEmpty && normalized.Count == 0)
            return fallback.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return normalized;
    }

    private static string NormalizeUpdateChannel(string? channel, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? fallback : channel.Trim().ToLowerInvariant();
        return SupportedUpdateChannels.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : fallback;
    }

    private void PersistNormalizedIfNeeded(AppConfigRoot normalized)
    {
        var json = JsonSerializer.Serialize(normalized, _writeOptions);
        if (!File.Exists(ConfigPath))
        {
            WriteAllTextAtomic(ConfigPath, json);
            return;
        }

        string existing;
        try
        {
            existing = File.ReadAllText(ConfigPath);
        }
        catch
        {
            WriteAllTextAtomic(ConfigPath, json);
            return;
        }

        if (string.Equals(existing, json, StringComparison.Ordinal))
            return;

        WriteAllTextAtomic(ConfigPath, json);
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("配置目录无效");

        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content);
        ReplaceAtomic(tempPath, path);
    }

    private static async Task WriteAllTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("配置目录无效");

        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
        ReplaceAtomic(tempPath, path);
    }

    private static void ReplaceAtomic(string tempPath, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch
        {
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("AppConfigStore", "config.atomic_cleanup.fail", "Failed to cleanup temporary config file", ex,
                    new { tempPath, targetPath });
            }
        }
    }

    private static void MigrateLegacyConfigIfNeeded(string configDir)
    {
        var newPath = Path.Combine(configDir, UnifiedConfigFileName);
        var legacyPath = Path.Combine(configDir, LegacyConfigFileName);
        if (File.Exists(newPath) || !File.Exists(legacyPath))
            return;

        try
        {
            File.Copy(legacyPath, newPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn("AppConfigStore", "config.legacy_migrate.fail",
                "Failed to migrate legacy config file", ex,
                new { legacyPath, newPath });
        }
    }

    PgOptions IPostgresConfigStore.LoadPostgresOptions() => Load().Postgres;

    async Task IPostgresConfigStore.SavePostgresOptionsAsync(PgOptions postgres, CancellationToken ct)
    {
        var cloned = ClonePostgres(postgres);
        await UpdateAsync(cfg => cfg.Postgres = cloned, ct).ConfigureAwait(false);
    }

    private static PgOptions ClonePostgres(PgOptions src) => new()
    {
        Host = src.Host,
        Port = src.Port,
        Database = src.Database,
        Username = src.Username,
        Password = src.Password,
        ConnectTimeoutSeconds = src.ConnectTimeoutSeconds,
        CommandTimeoutSeconds = src.CommandTimeoutSeconds,
        PoolSize = src.PoolSize,
        ReconnectIntervalSeconds = src.ReconnectIntervalSeconds,
        KeepAliveSeconds = src.KeepAliveSeconds,
        MonitorPingSeconds = src.MonitorPingSeconds,
        MonitorPingTimeoutSeconds = src.MonitorPingTimeoutSeconds
    };
}
