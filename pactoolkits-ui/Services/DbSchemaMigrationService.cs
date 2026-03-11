using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace pactoolkits_ui.Services;

public interface IDbSchemaMigrationService
{
    Task<DbSchemaMigrationResult> EnsureUpToDateAsync(CancellationToken ct, string? targetVersion = null);
}

public sealed record DbSchemaMigrationResult(
    string? BeforeVersion,
    string? AfterVersion,
    int AppliedCount,
    int SkippedCount)
{
    public bool HasChanges => AppliedCount > 0;
}

public sealed class DbSchemaMigrationService : IDbSchemaMigrationService
{
    private const string Module = "DbSchemaMigration";
    private const string LockKey = "pactoolkits_schema_migration";
    private static readonly Regex MigrationNameRegex =
        new("^V(?<major>\\d+)_(?<minor>\\d+)_(?<patch>\\d+)__.+\\.sql$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;

    public DbSchemaMigrationService(IDbConfigService dbConfig, IAppLogger logger)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DbSchemaMigrationResult> EnsureUpToDateAsync(CancellationToken ct, string? targetVersion = null)
    {
        var opt = _dbConfig.Current;
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = opt.Host,
            Port = opt.Port,
            Database = opt.Database,
            Username = opt.Username,
            Password = opt.Password,
            SearchPath = "public",
            Timeout = opt.ConnectTimeoutSeconds,
            KeepAlive = opt.KeepAliveSeconds
        };

        var bootstrapScripts = LoadBootstrapScripts();
        var migrationScripts = LoadMigrationScripts();
        var hasTarget = TryParseSemVer(targetVersion, out var targetSemVer);
        if (hasTarget)
            migrationScripts = migrationScripts.Where(x => CompareSemVer(x.SemVer, targetSemVer) <= 0).ToList();
        ValidateScriptBatches(bootstrapScripts, migrationScripts);

        await using var conn = new NpgsqlConnection(csb.ToString());
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var commandTimeout = Math.Max(15, opt.CommandTimeoutSeconds);
        _logger.Info(Module, "db.migrate.start", "Start schema migration", new
        {
            opt.Host,
            opt.Port,
            opt.Database,
            searchPath = "public",
            commandTimeout,
            bootstrapCount = bootstrapScripts.Count,
            migrationCount = migrationScripts.Count,
            targetVersion = hasTarget ? $"{targetSemVer.major}.{targetSemVer.minor}.{targetSemVer.patch}" : null
        });

        await ExecuteScalarAsync(conn,
            "select pg_advisory_lock(hashtext(@k))",
            commandTimeout,
            ct,
            ("k", LockKey)).ConfigureAwait(false);

        try
        {
            await EnsurePublicSearchPathAsync(conn, commandTimeout, ct).ConfigureAwait(false);

            if (!await TableExistsAsync(conn, "schema_migrations", commandTimeout, ct).ConfigureAwait(false))
            {
                _logger.Info(Module, "db.migrate.bootstrap.begin", "Bootstrap metadata tables");
                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    await ExecuteSqlAsync(conn, "set local search_path to public", commandTimeout, ct, tx).ConfigureAwait(false);
                    foreach (var script in bootstrapScripts)
                        await ExecuteSqlAsync(conn, script.Sql, commandTimeout, ct, tx).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    throw;
                }
                _logger.Info(Module, "db.migrate.bootstrap.ok", "Bootstrap metadata tables finished");
            }

            var beforeVersion = await TryReadSchemaVersionAsync(conn, commandTimeout, ct).ConfigureAwait(false);
            var appliedMap = await ReadAppliedMigrationsAsync(conn, commandTimeout, ct).ConfigureAwait(false);

            var applied = 0;
            var skipped = 0;

            foreach (var script in migrationScripts)
            {
                if (appliedMap.TryGetValue(script.Version, out var appliedChecksum))
                {
                    if (!string.Equals(appliedChecksum, script.Checksum, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"迁移脚本校验和冲突：{script.Version}");
                    skipped++;
                    continue;
                }

                _logger.Info(Module, "db.migrate.apply.begin", "Applying migration script", new
                {
                    version = script.Version,
                    file = script.FileName
                });

                await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    await ExecuteSqlAsync(conn, "set local search_path to public", commandTimeout, ct, tx).ConfigureAwait(false);
                    await ExecuteSqlAsync(conn, script.Sql, commandTimeout, ct, tx).ConfigureAwait(false);

                    await ExecuteSqlAsync(conn, @"
insert into public.schema_migrations(version, name, checksum, success, note)
values (@v, @n, @c, true, @note)
on conflict (version) do update
set name = excluded.name,
    checksum = excluded.checksum,
    success = excluded.success,
    note = excluded.note,
    installed_at = clock_timestamp();
",
                        commandTimeout,
                        ct,
                        tx,
                        ("v", script.Version),
                        ("n", script.FileName),
                        ("c", script.Checksum),
                        ("note", "applied by pactoolkits-ui")).ConfigureAwait(false);

                    await ExecuteSqlAsync(conn, @"
insert into public.schema_version(singleton, schema_version, applied_at, note)
values (true, @v, clock_timestamp(), @note)
on conflict (singleton) do update
set schema_version = excluded.schema_version,
    applied_at = excluded.applied_at,
    note = excluded.note;
",
                        commandTimeout,
                        ct,
                        tx,
                        ("v", script.Version),
                        ("note", $"migration {script.FileName}")).ConfigureAwait(false);

                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    applied++;

                    _logger.Info(Module, "db.migrate.apply.ok", "Migration script applied", new
                    {
                        version = script.Version,
                        file = script.FileName
                    });
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    _logger.Error(Module, "db.migrate.apply.fail", "Migration script failed", ex, new
                    {
                        version = script.Version,
                        file = script.FileName
                    });
                    throw;
                }
            }

            var afterVersion = await TryReadSchemaVersionAsync(conn, commandTimeout, ct).ConfigureAwait(false);
            _logger.Info(Module, "db.migrate.finish", "Schema migration finished", new
            {
                beforeVersion,
                afterVersion,
                applied,
                skipped
            });
            return new DbSchemaMigrationResult(beforeVersion, afterVersion, applied, skipped);
        }
        finally
        {
            try
            {
                await ExecuteScalarAsync(conn,
                    "select pg_advisory_unlock(hashtext(@k))",
                    commandTimeout,
                    CancellationToken.None,
                    ("k", LockKey)).ConfigureAwait(false);
            }
            catch
            {
                // Ignore unlock failures. Session close also releases advisory locks.
            }
        }
    }

    private static async Task<object?> ExecuteScalarAsync(
        NpgsqlConnection conn,
        string sql,
        int timeoutSeconds,
        CancellationToken ct,
        params (string name, object? value)[] args)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExecuteSqlAsync(
        NpgsqlConnection conn,
        string sql,
        int timeoutSeconds,
        CancellationToken ct,
        NpgsqlTransaction? tx = null,
        params (string name, object? value)[] args)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        cmd.Transaction = tx;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection conn,
        string tableName,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var reg = $"public.{tableName}";
        var value = await ExecuteScalarAsync(conn,
            "select to_regclass(@t) is not null",
            timeoutSeconds,
            ct,
            ("t", reg)).ConfigureAwait(false);
        return value is bool ok && ok;
    }

    private static async Task<string?> TryReadSchemaVersionAsync(
        NpgsqlConnection conn,
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "schema_version", timeoutSeconds, ct).ConfigureAwait(false))
            return null;

        var value = await ExecuteScalarAsync(conn,
            "select schema_version from public.schema_version where singleton = true",
            timeoutSeconds,
            ct).ConfigureAwait(false);
        var text = value?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static async Task<Dictionary<string, string>> ReadAppliedMigrationsAsync(
        NpgsqlConnection conn,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!await TableExistsAsync(conn, "schema_migrations", timeoutSeconds, ct).ConfigureAwait(false))
            return map;

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = timeoutSeconds;
        cmd.CommandText = "select version, checksum from public.schema_migrations where success = true";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var version = reader.GetString(0);
            var checksum = reader.GetString(1);
            map[version] = checksum;
        }
        return map;
    }

    private static List<SqlScript> LoadBootstrapScripts()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Sql", "Bootstrap");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"缺少迁移目录：{root}");

        var scripts = Directory
            .EnumerateFiles(root, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SqlScript(Path.GetFileName(path), "bootstrap", ReadScript(path), (0, 0, 0)))
            .ToList();
        if (scripts.Count == 0)
            throw new InvalidOperationException($"未发现 bootstrap 脚本：{root}");
        return scripts;
    }

    private static List<SqlScript> LoadMigrationScripts()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Sql", "Migrations");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"缺少迁移目录：{root}");

        var allSqlFiles = Directory
            .EnumerateFiles(root, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allSqlFiles.Count == 0)
            throw new InvalidOperationException($"未发现迁移脚本：{root}");

        var scripts = new List<SqlScript>();
        var invalidFiles = new List<string>();
        foreach (var path in allSqlFiles)
        {
            var fileName = Path.GetFileName(path);
            var match = MigrationNameRegex.Match(fileName);
            if (!match.Success)
            {
                invalidFiles.Add(fileName);
                continue;
            }

            var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
            var patch = int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture);
            var ver = (major, minor, patch);
            scripts.Add(new SqlScript(
                fileName,
                $"{major}.{minor}.{patch}",
                ReadScript(path),
                ver));
        }

        if (invalidFiles.Count > 0)
        {
            throw new InvalidOperationException(
                $"迁移脚本命名不合法（需 Vx_y_z__name.sql）：{string.Join(", ", invalidFiles)}");
        }

        return scripts
            .OrderBy(x => x.SemVer, SemVerComparer.Instance)
            .ToList();
    }

    private static string ReadScript(string path)
    {
        var raw = File.ReadAllText(path);
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var filtered = lines
            .Where(line =>
            {
                var t = line.TrimStart();
                return !t.StartsWith("\\", StringComparison.Ordinal);
            });
        return string.Join('\n', filtered);
    }

    private static async Task EnsurePublicSearchPathAsync(
        NpgsqlConnection conn,
        int timeoutSeconds,
        CancellationToken ct)
    {
        await ExecuteSqlAsync(conn, "set search_path to public", timeoutSeconds, ct).ConfigureAwait(false);
    }

    private static void ValidateScriptBatches(IReadOnlyList<SqlScript> bootstrapScripts, IReadOnlyList<SqlScript> migrationScripts)
    {
        EnsureSqlNotEmpty(bootstrapScripts);
        EnsureSqlNotEmpty(migrationScripts);

        var dupVersions = migrationScripts
            .GroupBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupVersions.Count > 0)
            throw new InvalidOperationException($"存在重复迁移版本：{string.Join(", ", dupVersions)}");
    }

    private static void EnsureSqlNotEmpty(IEnumerable<SqlScript> scripts)
    {
        foreach (var script in scripts)
        {
            if (string.IsNullOrWhiteSpace(script.Sql))
                throw new InvalidOperationException($"迁移脚本为空：{script.FileName}");
        }
    }

    private static int CompareSemVer((int major, int minor, int patch) left, (int major, int minor, int patch) right)
    {
        if (left.major != right.major) return left.major.CompareTo(right.major);
        if (left.minor != right.minor) return left.minor.CompareTo(right.minor);
        return left.patch.CompareTo(right.patch);
    }

    private static bool TryParseSemVer(string? text, out (int major, int minor, int patch) semVer)
    {
        semVer = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var patch))
            return false;

        semVer = (major, minor, patch);
        return true;
    }

    private sealed record SqlScript(
        string FileName,
        string Version,
        string Sql,
        (int major, int minor, int patch) SemVer)
    {
        public string Checksum => ComputeSha256(Sql);
    }

    private sealed class SemVerComparer : IComparer<(int major, int minor, int patch)>
    {
        public static SemVerComparer Instance { get; } = new();

        public int Compare((int major, int minor, int patch) x, (int major, int minor, int patch) y)
            => CompareSemVer(x, y);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
