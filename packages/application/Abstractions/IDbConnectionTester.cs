using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbConnectionTester
{
    Task<DbTestResult> TestAsync(PgOptions opt, CancellationToken ct = default);
}
