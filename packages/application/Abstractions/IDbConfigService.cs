namespace PacToolkits.Application.Abstractions;

public interface IDbConfigService
{
    PgOptions Current { get; }
    string ConfigPath { get; }

    event EventHandler? Applied;

    Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct);
    Task SaveAndApplyAsync(PgOptions opt, CancellationToken ct = default);
}
