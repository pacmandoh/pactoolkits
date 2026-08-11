using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Files;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;
using PacToolkits.Logger;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

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
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SemaphoreSlim _ioGate = new(1, 1);

    private readonly string _configDir;

    public string ConfigPath { get; }

    public AppConfigStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacToolkits"))
    {
    }

    internal AppConfigStore(string configDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDir);
        _configDir = configDir;
        Directory.CreateDirectory(_configDir);
        ConfigPath = Path.Combine(_configDir, UnifiedConfigFileName);

        InitConfig();
    }

    public AppConfigRoot Load()
    {
        _ioGate.Wait();
        try
        {
            var raw = ReadUnifiedOrDefault();
            var normalized = Normalize(raw);
            PersistIfChanged(normalized);
            return normalized;
        }
        finally
        {
            _ioGate.Release();
        }
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
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfigRoot();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfigRoot>(json) ?? new AppConfigRoot();
        }
        catch
        {
            return new AppConfigRoot();
        }
    }

    private void InitConfig()
    {
        _ioGate.Wait();
        try
        {
            var normalized = Normalize(ReadUnifiedOrDefault());
            PersistIfChanged(normalized);
        }
        finally
        {
            _ioGate.Release();
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
        agents.Modules = NormalizeModules(agents.Modules);
        return agents;
    }

    // 仅保留配置内模块键；catalog / 启用扩容由 Runtime 吃 Snapshot 后 Merge
    private static Dictionary<string, ModuleOptions> NormalizeModules(
        Dictionary<string, ModuleOptions>? source)
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

        return existing;
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
        => LogLevel.Canonical(level, LogLevel.Error);

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
