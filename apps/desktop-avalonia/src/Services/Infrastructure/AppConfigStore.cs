using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>应用配置读写与 schema 迁移入口</summary>
public interface IAppConfigStore
{
    string ConfigPath { get; }
    AppConfigRoot Load();
    void Save(AppConfigRoot config);
    Task SaveAsync(AppConfigRoot config, CancellationToken ct = default);
    void Update(Action<AppConfigRoot> mutator);
    Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default);
}

/// <summary>
/// 应用配置存储
///
/// 负责统一配置文件读写与 schema 迁移；不含业务查询
/// </summary>
public sealed class AppConfigStore : IAppConfigStore, IDbOptionsStore
{
    private const string UnifiedConfigFileName = "PacToolkits.Desktop.config.json";
    private const string LegacyConfigFileName = "pactoolkits-ui.config.json";
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
        MigrateLegacyConfig(_configDir);

        InitConfig();
    }

    public AppConfigRoot Load()
    {
        string? migratedLogDirectoryFrom = null;
        string? migratedLogDirectoryTo = null;
        AppConfigRoot normalized;

        lock (_gate)
        {
            var raw = ReadUnifiedOrDefault();
            var rawLogDirectory = raw.Logging?.LogDirectory ?? string.Empty;
            normalized = Normalize(raw);
            if (LogDirectory.IsLegacyLogsDirectory(rawLogDirectory))
            {
                migratedLogDirectoryFrom = rawLogDirectory.Trim();
                migratedLogDirectoryTo = normalized.Logging.LogDirectory;
            }

            PersistIfChanged(normalized);
        }

        if (migratedLogDirectoryFrom is not null)
        {
            AppLog.TryGetLogger()?.Info(
                "AppConfigStore",
                "logging.directory.migrate",
                "Migrating legacy desktop log directory to new standard location",
                context: new
                {
                    from = migratedLogDirectoryFrom,
                    to = migratedLogDirectoryTo
                });
        }

        return normalized;
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
            {
                return new AppConfigRoot();
            }

            var json = File.ReadAllText(readablePath);
            json = MigrateConfigJsonToV2(json);
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
        {
            return primaryPath;
        }

        var legacyPath = Path.Combine(_configDir, LegacyConfigFileName);
        return File.Exists(legacyPath) ? legacyPath : primaryPath;
    }

    private void InitConfig()
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
                    existingJson = MigrateConfigJsonToV2(existingJson);
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
            var json = JsonSerializer.Serialize(normalized, _writeOptions);
            if (File.Exists(readablePath)
                && HasPersistedDefaults(raw)
                && HasRequiredConfigKeys(existingJson))
            {
                // 即便统一配置文件已存在，仍要落盘 schema v2 / Legacy Tools→Agents host 改写
                //（旧 InitConfig 曾直接 return 不写）
                if (!string.Equals(readablePath, ConfigPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existingJson, json, StringComparison.Ordinal))
                {
                    WriteAllTextAtomic(ConfigPath, json);
                }

                return;
            }

            WriteAllTextAtomic(ConfigPath, json);
        }
    }

    private static bool HasPersistedDefaults(AppConfigRoot? root)
    {
        if (root is null)
        {
            return false;
        }

        if (root.Agents?.Injector is not { } a)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.PgDriver))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.PgSsl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.OptWindowClass))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.IptWindowClass))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.OptParseGridClassNN))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.OptVerifyGridClassNN))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.IptParseGridClassNN))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.IptVerifyGridClassNN))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.OptInputClassNN))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.IptInputClassNN))
        {
            return false;
        }

        if (a.ConfirmTimeoutMs <= 0)
        {
            return false;
        }

        if (a.AppWin is null || a.AppWin.Count == 0)
        {
            return false;
        }

        if (a.ColSpecs is null || a.ColSpecs.Count == 0)
        {
            return false;
        }

        if (a.IntCols is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.WarehouseTaskIdentifier))
        {
            return false;
        }

        return true;
    }

    private static bool HasRequiredConfigKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("MsfxApi", out var msfx) || msfx.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!msfx.TryGetProperty("RefEntId", out _))
            {
                return false;
            }

            if (!root.TryGetProperty("Agents", out var agents) || agents.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!agents.TryGetProperty("Injector", out var injector) || injector.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!injector.TryGetProperty("WarehouseEnabled", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("WarehouseAnchorTexts", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("CodePickPolicy", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("WarehouseTaskIdentifier", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("OptWindowClass", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("IptWindowClass", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("OptParseGridClassNN", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("OptVerifyGridClassNN", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("IptParseGridClassNN", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("IptVerifyGridClassNN", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("OptInputClassNN", out _))
            {
                return false;
            }

            if (!injector.TryGetProperty("IptInputClassNN", out _))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Main SchemaVersion=1 的 AutomationTools → schema-2 Agents + Injector</summary>
    internal static string MigrateConfigJsonToV2(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return json;
            }

            var schema = root["SchemaVersion"]?.GetValue<int>() ?? 0;
            var agentsNode = root["Agents"] as JsonObject;

            // Schema ≥2：只清残留，绝不回并 Legacy AutomationTools
            if (schema >= 2)
            {
                root["SchemaVersion"] = 2;
                root.Remove("AutomationTools");
                if (agentsNode is null)
                {
                    root["Agents"] = new JsonObject
                    {
                        ["ExecutablePath"] = AgentsPaths.HostExecutable,
                        ["ProcessName"] = string.Empty,
                        ["Injector"] = new JsonObject(),
                    };
                }
                else
                {
                    agentsNode.Remove("Enabled");
                    agentsNode.Remove("agents");
                    if (agentsNode["Injector"] is null)
                    {
                        agentsNode["Injector"] = new JsonObject();
                    }
                }

                return root.ToJsonString(_writeOptions);
            }

            // 仅 Legacy（schema < 2）：AutomationTools.Ahk + AutomationTools.Agent → Agents
            string? path = null;
            string? processName = null;
            JsonNode? injector = new JsonObject();

            if (root["AutomationTools"] is JsonObject tools)
            {
                if (tools["Ahk"] is JsonObject host)
                {
                    path = host["ExecutablePath"]?.GetValue<string>();
                    processName = host["ProcessName"]?.GetValue<string>();
                }

                if (tools["Agent"] is JsonNode mainInjectorSection)
                {
                    injector = mainInjectorSection.DeepClone();
                }
            }

            root["Agents"] = new JsonObject
            {
                ["ExecutablePath"] = string.IsNullOrWhiteSpace(path)
                    ? AgentsPaths.HostExecutable
                    : path.Trim(),
                ["ProcessName"] = (processName ?? string.Empty).Trim(),
                ["Injector"] = injector.DeepClone(),
            };
            root.Remove("AutomationTools");
            root["SchemaVersion"] = 2;
            return root.ToJsonString(_writeOptions);
        }
        catch
        {
            return json;
        }
    }

    internal static AppConfigRoot Normalize(AppConfigRoot? source)
    {
        var root = source ?? new AppConfigRoot();
        root.SchemaVersion = 2;
        root.Postgres ??= new PgOptions();
        root.TraceCodeValidation ??= new TraceCodeValidationOptions();
        root.Agents ??= new AgentsOptions();
        root.MsfxApi ??= new MsfxApiOptions();
        root.UiBehavior ??= new UiBehaviorOptions();
        root.Update ??= new UpdateOptions();
        root.Logging ??= new LoggingOptions();

        if (root.TraceCodeValidation.RequiredLength <= 0)
        {
            root.TraceCodeValidation.RequiredLength = 20;
        }

        if (string.IsNullOrWhiteSpace(root.TraceCodeValidation.Pattern))
        {
            root.TraceCodeValidation.Pattern = "^8\\d+$";
        }

        root.Agents = NormalizeAgents(root.Agents);
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

    private static AgentsOptions NormalizeAgents(AgentsOptions? source)
    {
        var defaults = new AgentsOptions();
        var agents = source ?? new AgentsOptions();
        agents.ExecutablePath = string.IsNullOrWhiteSpace(agents.ExecutablePath)
            ? defaults.ExecutablePath
            : agents.ExecutablePath.Trim();
        agents.ProcessName = string.IsNullOrWhiteSpace(agents.ProcessName)
            ? defaults.ProcessName
            : agents.ProcessName.Trim();
        MigrateLegacyToolsHost(agents);
        agents.Injector = NormalizeInjector(agents.Injector);
        return agents;
    }

    private static void MigrateLegacyToolsHost(AgentsOptions agents)
    {
        var path = agents.ExecutablePath ?? string.Empty;
        var processName = agents.ProcessName ?? string.Empty;
        if (!TryMigrateLegacyToolsHost(ref path, ref processName))
        {
            return;
        }

        agents.ExecutablePath = path;
        agents.ProcessName = processName;
    }

    private static bool TryMigrateLegacyToolsHost(ref string path, ref string processName)
    {
        if (!AgentsPath.IsLegacyToolsStoredPath(path))
        {
            return false;
        }

        path = AgentsPaths.HostExecutable;
        if (string.IsNullOrWhiteSpace(processName)
            || string.Equals(processName, AgentsPaths.LegacyToolsProcessName, StringComparison.OrdinalIgnoreCase))
        {
            processName = AgentsPaths.HostProcessName;
        }

        return true;
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
        {
            options.TimeoutSeconds = defaults.TimeoutSeconds;
        }

        if (options.TimeoutSeconds > 120)
        {
            options.TimeoutSeconds = 120;
        }

        return options;
    }

    private static UpdateOptions NormalizeUpdate(UpdateOptions? source)
    {
        var defaults = new UpdateOptions();
        var options = source ?? new UpdateOptions();

        options.Channel = AppUpdatePolicy.NormalizeChannel(options.Channel);
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
        options.LogDirectory = LogDirectory.Resolve(options.LogDirectory).StoredDirectory;
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

    private static InjectorOptions NormalizeInjector(InjectorOptions? source)
    {
        var defaults = new InjectorOptions();
        var injector = source ?? new InjectorOptions();

        injector.PgDriver = string.IsNullOrWhiteSpace(injector.PgDriver) ? defaults.PgDriver : injector.PgDriver.Trim();
        injector.PgSsl = NormalizePgSsl(injector.PgSsl, defaults.PgSsl);
        injector.OptWindowClass = string.IsNullOrWhiteSpace(injector.OptWindowClass) ? defaults.OptWindowClass : injector.OptWindowClass.Trim();
        injector.IptWindowClass = string.IsNullOrWhiteSpace(injector.IptWindowClass) ? defaults.IptWindowClass : injector.IptWindowClass.Trim();
        injector.OptParseGridClassNN = string.IsNullOrWhiteSpace(injector.OptParseGridClassNN) ? defaults.OptParseGridClassNN : injector.OptParseGridClassNN.Trim();
        injector.OptVerifyGridClassNN = string.IsNullOrWhiteSpace(injector.OptVerifyGridClassNN) ? defaults.OptVerifyGridClassNN : injector.OptVerifyGridClassNN.Trim();
        injector.IptParseGridClassNN = string.IsNullOrWhiteSpace(injector.IptParseGridClassNN) ? defaults.IptParseGridClassNN : injector.IptParseGridClassNN.Trim();
        injector.IptVerifyGridClassNN = string.IsNullOrWhiteSpace(injector.IptVerifyGridClassNN) ? defaults.IptVerifyGridClassNN : injector.IptVerifyGridClassNN.Trim();
        injector.OptInputClassNN = string.IsNullOrWhiteSpace(injector.OptInputClassNN) ? defaults.OptInputClassNN : injector.OptInputClassNN.Trim();
        injector.IptInputClassNN = string.IsNullOrWhiteSpace(injector.IptInputClassNN) ? defaults.IptInputClassNN : injector.IptInputClassNN.Trim();
        injector.ConfirmTimeoutMs = injector.ConfirmTimeoutMs <= 0 ? defaults.ConfirmTimeoutMs : injector.ConfirmTimeoutMs;

        var appWin = (injector.AppWin ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(
                kv => kv.Key.Trim(),
                kv => kv.Value == 0 ? 0 : 1,
                StringComparer.OrdinalIgnoreCase);
        injector.AppWin = appWin.Count > 0
            ? appWin
            : new Dictionary<string, int>(defaults.AppWin, StringComparer.OrdinalIgnoreCase);

        injector.ColSpecs = NormalizeStringList(injector.ColSpecs, defaults.ColSpecs, requireNonEmpty: true);
        injector.IntCols = NormalizeStringList(injector.IntCols, defaults.IntCols, requireNonEmpty: false);
        injector.WarehouseAnchorTexts = NormalizeStringList(injector.WarehouseAnchorTexts, defaults.WarehouseAnchorTexts, requireNonEmpty: true);
        injector.CodePickPolicy = NormalizeCodePickPolicy(injector.CodePickPolicy, defaults.CodePickPolicy);
        injector.WarehouseTaskIdentifier = string.IsNullOrWhiteSpace(injector.WarehouseTaskIdentifier)
            ? defaults.WarehouseTaskIdentifier
            : injector.WarehouseTaskIdentifier.Trim();

        return injector;
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
        {
            return fallback.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return normalized;
    }

    private void PersistIfChanged(AppConfigRoot normalized)
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
        {
            return;
        }

        WriteAllTextAtomic(ConfigPath, json);
    }

    // 先写临时文件再替换，避免半写配置被读到
    private static void WriteAllTextAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            throw new InvalidOperationException("配置目录无效");
        }

        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content);
        ReplaceAtomic(tempPath, path);
    }

    private static async Task WriteAllTextAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            throw new InvalidOperationException("配置目录无效");
        }

        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
        ReplaceAtomic(tempPath, path);
    }

    // Replace 失败时用 Move(overwrite) 兜底（跨平台/权限差异）
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
                {
                    File.Delete(tempPath);
                }
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("AppConfigStore", "config.atomic_cleanup.fail", "Failed to cleanup temporary config file", ex,
                    new { tempPath, targetPath });
            }
        }
    }

    private static void MigrateLegacyConfig(string configDir)
    {
        var newPath = Path.Combine(configDir, UnifiedConfigFileName);
        if (File.Exists(newPath))
        {
            return;
        }

        var legacyPath = Path.Combine(configDir, LegacyConfigFileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

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

    public PgOptions LoadPgOptions() => Load().Postgres;

    public async Task SavePgOptionsAsync(PgOptions options, CancellationToken ct)
    {
        var cloned = ClonePostgres(options);
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
