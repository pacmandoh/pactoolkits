using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 按 PgOptions 探测连接是否可用
/// </summary>
public interface IDbConnectionTester
{
    Task<DbTestResult> TestAsync(PgOptions opt, CancellationToken ct = default);
}
