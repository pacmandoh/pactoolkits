using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>应用配置持久化契约</summary>
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
/// 统一读取、写入并规范化应用配置，不执行业务查询
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

    private readonly SemaphoreSlim _ioGate = new(1, 1);

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

        _ioGate.Wait();
        try
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
        finally
        {
            _ioGate.Release();
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
        _ioGate.Wait();
        try
        {
            var normalized = Normalize(config);
            var json = SerializeDesktopConfig(normalized);
            AtomicFile.WriteAllText(ConfigPath, json);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveAsync(AppConfigRoot config, CancellationToken ct = default)
    {
        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var normalized = Normalize(config);
            var json = SerializeDesktopConfig(normalized);
            await AtomicFile.WriteAllTextAsync(ConfigPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public void Update(Action<AppConfigRoot> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        _ioGate.Wait();
        try
        {
            var raw = ReadUnifiedOrDefault();
            var cfg = Normalize(raw);
            mutator(cfg);
            var normalized = Normalize(cfg);
            var json = SerializeDesktopConfig(normalized);
            AtomicFile.WriteAllText(ConfigPath, json);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var raw = ReadUnifiedOrDefault();
            var cfg = Normalize(raw);
            mutator(cfg);
            var normalized = Normalize(cfg);
            var json = SerializeDesktopConfig(normalized);
            await AtomicFile.WriteAllTextAsync(ConfigPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
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
        _ioGate.Wait();
        try
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
            var json = SerializeDesktopConfig(normalized);
            if (File.Exists(readablePath)
                && HasPersistedDefaults(raw)
                && HasRequiredConfigKeys(existingJson))
            {
                // 已有配置仍需持久化规范化结果，使模块发现和路径修正跨进程重启生效
                if (!string.Equals(readablePath, ConfigPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existingJson, json, StringComparison.Ordinal))
                {
                    AtomicFile.WriteAllText(ConfigPath, json);
                }

                return;
            }

            AtomicFile.WriteAllText(ConfigPath, json);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private static bool HasPersistedDefaults(AppConfigRoot? root)
    {
        if (root?.Agents is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(root.Agents.ExecutablePath)
               || root.Agents.Modules?.Count > 0
               || !string.IsNullOrWhiteSpace(root.Postgres?.Host);
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

            return agents.TryGetProperty("ExecutablePath", out _);
        }
        catch
        {
            return false;
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
        agents.Modules = NormalizeModules(agents.Modules, agents.ExecutablePath);
        return agents;
    }

    // 仅以稳定非空目录清单收敛模块开关，避免部署替换窗口将全部模块误判为已删除
    private static Dictionary<string, ModuleOptions> NormalizeModules(
        Dictionary<string, ModuleOptions>? source,
        string executablePath)
    {
        var existing = new Dictionary<string, ModuleOptions>(StringComparer.Ordinal);
        if (source is not null)
        {
            foreach (var (id, options) in source)
            {
                if (string.IsNullOrWhiteSpace(id) || options is null)
                {
                    continue;
                }

                existing[id.Trim()] = new ModuleOptions { Enabled = options.Enabled };
            }
        }

        var resolution = AgentsPath.ResolveHost(executablePath, AppContext.BaseDirectory);
        var agentsDir = resolution.ResolvedPath is null
            ? null
            : Path.GetDirectoryName(resolution.ResolvedPath);
        if (string.IsNullOrWhiteSpace(agentsDir) || !Directory.Exists(agentsDir))
        {
            return existing;
        }

        var scanned = AgentsPath.ScanModules(agentsDir);
        if (scanned.Count == 0)
        {
            // 安装或模块更新期间目录可能短暂为空，此时保留用户已有启用状态
            return existing;
        }

        var modules = new Dictionary<string, ModuleOptions>(StringComparer.Ordinal);
        foreach (var module in scanned)
        {
            var enabled = existing.TryGetValue(module.Id, out var options) ? options.Enabled : true;
            modules[module.Id] = new ModuleOptions { Enabled = enabled };
        }

        return modules;
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
        => AppUpdatePolicy.NormalizeOptions(source);

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

    private void PersistIfChanged(AppConfigRoot normalized)
    {
        var json = SerializeDesktopConfig(normalized);
        if (!File.Exists(ConfigPath))
        {
            AtomicFile.WriteAllText(ConfigPath, json);
            return;
        }

        string existing;
        try
        {
            existing = File.ReadAllText(ConfigPath);
        }
        catch
        {
            AtomicFile.WriteAllText(ConfigPath, json);
            return;
        }

        if (string.Equals(existing, json, StringComparison.Ordinal))
        {
            return;
        }

        AtomicFile.WriteAllText(ConfigPath, json);
    }

    private string SerializeDesktopConfig(AppConfigRoot normalized)
        => JsonSerializer.Serialize(normalized, _writeOptions);

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
