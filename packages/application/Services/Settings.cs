using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>执行设置页数据库连接校验和结构兼容状态查询</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionTester _tester;
    private readonly IDbSchemaVersionService _schemaVersion;
    private readonly IClientIdReadRepo _clientRepo;
    private readonly IDbAccessGuard _accessGuard;

    public SettingsService(
        IDbConfigService dbConfig,
        IDbConnectionTester tester,
        IDbSchemaVersionService schemaVersion,
        IClientIdReadRepo clientRepo,
        IDbAccessGuard accessGuard)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
        _schemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
        _clientRepo = clientRepo ?? throw new ArgumentNullException(nameof(clientRepo));
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
    }

    public PgOptions AppliedDb => _dbConfig.Current;

    public Task SaveDbConfigAsync(PgOptions options, CancellationToken ct)
        => _dbConfig.ApplyAsync(options, ct);

    public async Task<DbConnectionValidation> ValidateDbConnectionAsync(
        PgOptions options,
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
    {
        var test = await _tester.TestAsync(options, ct).ConfigureAwait(false);
        if (!test.Ok)
        {
            return new DbConnectionValidation(
                ConnectionOk: false,
                ConnectionSummary: test.Summary,
                SchemaCompatible: false,
                IncompatibleMessage: null);
        }

        var compat = await CheckSchemaCompatibilityAsync(schemaContext, options, ct).ConfigureAwait(false);
        return new DbConnectionValidation(
            ConnectionOk: true,
            ConnectionSummary: test.Summary,
            SchemaCompatible: compat.Compatible,
            IncompatibleMessage: compat.IncompatibleMessage);
    }

    public async Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct)
    {
        var snapshot = await ReadSchemaStatusAsync(
            schemaContext,
            connectionOptions,
            ct).ConfigureAwait(false);
        if (snapshot.Satisfied)
        {
            return (true, null);
        }

        return (false, snapshot.IncompatibleMessage ?? BuildIncompatibleMessage(schemaContext, snapshot));
    }

    public Task<DbSchemaStatusSnapshot> GetSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
        => ReadSchemaStatusAsync(
            schemaContext,
            connectionOptions: null,
            ct);

    public Task<DbSchemaStatusSnapshot> GetSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct)
        => ReadSchemaStatusAsync(
            schemaContext,
            connectionOptions,
            ct);

    private async Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var uiMin = DbSchemaCompat.NormalizeBound(schemaContext.DesktopMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var uiMax = DbSchemaCompat.NormalizeBound(schemaContext.DesktopMaxDbSchema, schemaContext.TargetDbSchemaVersion);
        var localTarget = DbSchemaCompat.NormalizeBound(
            schemaContext.TargetDbSchemaVersion,
            schemaContext.TargetDbSchemaVersion);
        var schema = connectionOptions is null
            ? await _schemaVersion.TryReadSchemaVersionAsync(ct).ConfigureAwait(false)
            : await _schemaVersion.TryReadSchemaVersionAsync(connectionOptions, ct).ConfigureAwait(false);
        var compatibility = BuildCompatibility(schema, uiMin, uiMax);

        if (MatchesLocalDb(connectionOptions))
        {
            ApplyDbGuard(compatibility);
        }

        var current = schema.Value ?? string.Empty;
        var snapshot = new DbSchemaStatusSnapshot(
            SchemaOk: schema.Ok,
            CurrentVersion: current,
            Reason: schema.Ok ? null : schema.Reason ?? "读取失败",
            TargetVersion: localTarget,
            RequiredMinVersion: uiMin,
            RequiredMaxVersion: uiMax,
            Compatibility: compatibility.Status,
            Satisfied: compatibility.IsCompatible,
            IncompatibleMessage: null);
        return snapshot with
        {
            IncompatibleMessage = snapshot.Satisfied
                ? null
                : BuildIncompatibleMessage(schemaContext, snapshot)
        };
    }

    private static DbSchemaCompatibilityResult BuildCompatibility(
        DbSchemaVersionRead schema,
        string requiredMin,
        string requiredMax)
    {
        if (schema.Ok)
        {
            return DbSchemaCompat.Evaluate(schema.Value, requiredMin, requiredMax);
        }

        if (schema.IsMetadataMissing)
        {
            return new DbSchemaCompatibilityResult(
                DbSchemaCompatibility.MetadataMissing,
                schema.Value ?? string.Empty,
                requiredMin,
                requiredMax,
                schema.Reason ?? "数据库版本元数据缺失，需要通过外部部署工具初始化");
        }

        return new DbSchemaCompatibilityResult(
            DbSchemaCompatibility.Unknown,
            schema.Value ?? string.Empty,
            requiredMin,
            requiredMax,
            schema.Reason ?? "读取失败");
    }

    public async Task<ClientAliasSources> GetClientAliasSourcesAsync(PgOptions options, CancellationToken ct)
    {
        try
        {
            var clients = await _clientRepo.GetDistinctClientIdsAsync(options, ct).ConfigureAwait(false);
            var machines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in clients)
            {
                var machine = ExtractMachine(raw);
                if (machine.Length > 0)
                {
                    machines.Add(machine);
                }
            }

            return new ClientAliasSources(
                IsDbConnected: true,
                ClientMachines: machines.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch
        {
            return new ClientAliasSources(
                IsDbConnected: false,
                ClientMachines: Array.Empty<string>());
        }
    }

    private void ApplyDbGuard(DbSchemaCompatibilityResult compatibility)
    {
        if (compatibility.IsCompatible)
        {
            _accessGuard.Clear();
        }
        else
        {
            _accessGuard.Block(compatibility.Message);
        }
    }

    private bool MatchesLocalDb(PgOptions? connectionOptions)
    {
        if (connectionOptions is null)
        {
            return true;
        }

        var current = _dbConfig.Current;
        return string.Equals(connectionOptions.Host, current.Host, StringComparison.OrdinalIgnoreCase)
               && connectionOptions.Port == current.Port
               && string.Equals(connectionOptions.Database, current.Database, StringComparison.Ordinal)
               && string.Equals(connectionOptions.Username, current.Username, StringComparison.Ordinal)
               && string.Equals(connectionOptions.Password, current.Password, StringComparison.Ordinal);
    }

    private static string BuildIncompatibleMessage(
        DbSchemaVersionContext schemaContext,
        DbSchemaStatusSnapshot snapshot)
    {
        var uiMin = DbSchemaCompat.NormalizeBound(schemaContext.DesktopMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var uiMax = DbSchemaCompat.NormalizeBound(schemaContext.DesktopMaxDbSchema, schemaContext.TargetDbSchemaVersion);
        return DbSchemaCompat.BuildIncompatibleMessage(
            snapshot.SchemaOk,
            snapshot.CurrentVersion,
            snapshot.Reason,
            uiMin,
            uiMax);
    }

    private static string ExtractMachine(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var parsed = ClientParser.Parse(text);
        return (parsed.Machine ?? text).Trim();
    }
}
