namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 读取库中出现过的 distinct client_id（设置页别名来源）
/// </summary>
public interface IClientIdReadRepo
{
    Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct);
}
