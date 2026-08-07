using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 读库 schema，再对任意 [min,max] 做闭区间判定（业务 / 模块共用）
///
/// 不含 Agents×Desktop 产品 SemVer；不含 desired 副作用
/// </summary>
public interface IDbSchemaGate
{
    Task<DbSchemaVersionRead> ReadAsync(CancellationToken ct = default);

    Task<DbSchemaVersionRead> ReadAsync(PgOptions options, CancellationToken ct = default);

    /// <summary>在已有读结果上 Match（含读失败 / 元数据缺失）</summary>
    DbSchemaCompatibilityResult Match(
        DbSchemaVersionRead schema,
        string min,
        string max);
}
