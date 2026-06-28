namespace PacToolkits.Application.Abstractions;

public interface IDbConfigService
{
    PgOptions Current { get; }
    string ConfigPath { get; }

    event EventHandler? Applied;

    Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct);
    Task ApplyAsync(PgOptions opt, CancellationToken ct = default);
}
