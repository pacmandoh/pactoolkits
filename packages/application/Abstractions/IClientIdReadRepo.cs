namespace PacToolkits.Application.Abstractions;

public interface IClientIdReadRepo
{
    Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct);
}
