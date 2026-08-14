using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>
/// 库 schema 小门：读取与任意区间 Match（<see cref="SemVerRange.Classify"/>，纯 X.Y.Z）
/// </summary>
public sealed class DbSchemaGate : IDbSchemaGate
{
    private readonly IDbSchemaVersionService _schemaVersion;

    public DbSchemaGate(IDbSchemaVersionService schemaVersion)
    {
        _schemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
    }

    public Task<DbSchemaVersionRead> ReadAsync(CancellationToken ct = default)
        => _schemaVersion.TryReadSchemaVersionAsync(ct);

    public DbSchemaCompatibilityResult Match(
        DbSchemaVersionRead schema,
        string min,
        string max)
        => Classify(schema, min, max);

    /// <summary>已有读结果上的闭区间判定（含读失败 / 元数据缺失）</summary>
    public static DbSchemaCompatibilityResult Classify(
        DbSchemaVersionRead schema,
        string min,
        string max)
    {
        var requiredMin = (min ?? string.Empty).Trim();
        var requiredMax = (max ?? string.Empty).Trim();

        if (schema.Ok)
        {
            return DbSchemaCompatibilityResult.FromRange(
                SemVerRange.Classify(schema.Value, requiredMin, requiredMax, allowPrerelease: false));
        }

        if (schema.IsMetadataMissing)
        {
            return new DbSchemaCompatibilityResult(
                DbSchemaCompatibility.MetadataMissing,
                schema.Value ?? string.Empty,
                requiredMin,
                requiredMax,
                schema.Reason ?? string.Empty);
        }

        return new DbSchemaCompatibilityResult(
            DbSchemaCompatibility.Unknown,
            schema.Value ?? string.Empty,
            requiredMin,
            requiredMax,
            schema.Reason ?? string.Empty);
    }
}
