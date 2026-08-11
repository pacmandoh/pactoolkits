using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>执行设置页数据库连接校验和结构兼容状态查询</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionTester _tester;
    private readonly IDbSchemaGate _schemaGate;
    private readonly IClientIdReadRepo _clientRepo;
    private readonly IDbAccessGuard _accessGuard;

    public SettingsService(
        IDbConfigService dbConfig,
        IDbConnectionTester tester,
        IDbSchemaGate schemaGate,
        IClientIdReadRepo clientRepo,
        IDbAccessGuard accessGuard)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
        _schemaGate = schemaGate ?? throw new ArgumentNullException(nameof(schemaGate));
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

        return (false, snapshot.IncompatibleMessage);
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
        var min = schemaContext.Min;
        var max = schemaContext.Max;
        var target = schemaContext.Target;
        var schema = connectionOptions is null
            ? await _schemaGate.ReadAsync(ct).ConfigureAwait(false)
            : await _schemaGate.ReadAsync(connectionOptions, ct).ConfigureAwait(false);
        var compatibility = _schemaGate.Match(schema, min, max);

        if (MatchesLocalDb(connectionOptions))
        {
            ApplyDbGuard(compatibility);
        }

        var current = schema.Value ?? string.Empty;
        var satisfied = compatibility.IsCompatible;
        return new DbSchemaStatusSnapshot(
            SchemaOk: schema.Ok,
            CurrentVersion: current,
            Reason: schema.Ok ? null : schema.Reason ?? "读取失败",
            TargetVersion: target,
            RequiredMinVersion: min,
            RequiredMaxVersion: max,
            Compatibility: compatibility.Status,
            Satisfied: satisfied,
            IncompatibleMessage: satisfied
                ? null
                : DbSchemaDesktop.Incompatible(
                    schema.Ok,
                    current,
                    schema.Ok ? null : schema.Reason ?? "读取失败",
                    min,
                    max,
                    compatibility.Status));
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
            _accessGuard.Block(DbSchemaDesktop.GateBlock(compatibility));
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
