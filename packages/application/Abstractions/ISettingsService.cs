using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>定义设置页数据库配置保存、连接测试和 schema 兼容性检查契约</summary>
public interface ISettingsService
{
    PgOptions AppliedDb { get; }

    Task SaveDbConfigAsync(PgOptions options, CancellationToken ct);

    Task<DbConnectionValidation> ValidateDbConnectionAsync(
        PgOptions options,
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> GetSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> GetSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct);

    Task<ClientAliasSources> GetClientAliasSourcesAsync(
        PgOptions options,
        CancellationToken ct);
}
