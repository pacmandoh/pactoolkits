namespace PacToolkits.Application.Abstractions;

public interface IDbOptionsStore
{
    string ConfigPath { get; }
    PgOptions LoadPgOptions();
    Task SavePgOptionsAsync(PgOptions options, CancellationToken ct = default);
}
