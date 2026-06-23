using System.Text.Json;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Infrastructure.Database;

public sealed class DbEnvSettingsService : IDbEnvSettingsService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;

    public DbEnvSettingsService(IDbConfigService dbConfig, IAppLogger logger)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DbEnvSettings> TryReadAsync(CancellationToken ct)
        => TryReadAsync(_dbConfig.Current, ct);

    public async Task<DbEnvSettings> TryReadAsync(PgOptions options, CancellationToken ct)
    {
        try
        {
            await using var conn = await PgConnectionFactory.OpenAsync(options, ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                select setting_key, setting_value::text
                from public.app_environment_settings
                where setting_key = any(@keys)
                """;
            cmd.Parameters.AddWithValue(
                "keys",
                new[] { "Database.Environment", "Database.AllowBetaMigrations" });

            var environment = "production";
            var allowBetaMigrations = false;

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                var value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (string.Equals(key, "Database.Environment", StringComparison.Ordinal))
                {
                    environment = ParseJsonScalar(value);
                }
                else if (string.Equals(key, "Database.AllowBetaMigrations", StringComparison.Ordinal))
                {
                    allowBetaMigrations = IsTruthy(ParseJsonScalar(value));
                }
            }

            return new DbEnvSettings(environment, allowBetaMigrations);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.Info(
                "DatabaseEnvironment",
                "environment_settings.missing",
                "app_environment_settings table not found; using production defaults");
            return DbEnvSettings.ProductionDefaults;
        }
        catch (Exception ex)
        {
            _logger.Warn(
                "DatabaseEnvironment",
                "environment_settings.read_fail",
                "Failed reading app_environment_settings; using production defaults",
                ex);
            return DbEnvSettings.ProductionDefaults;
        }
    }

    private static string ParseJsonScalar(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => doc.RootElement.GetRawText(),
                _ => raw.Trim(),
            };
        }
        catch (JsonException)
        {
            return raw.Trim().Trim('"');
        }
    }

    private static bool IsTruthy(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("1", StringComparison.Ordinal)
               || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
