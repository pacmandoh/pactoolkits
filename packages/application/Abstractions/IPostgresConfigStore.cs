namespace PacToolkits.Application.Abstractions;

public interface IPostgresConfigStore
{
    string ConfigPath { get; }
    PgOptions LoadPostgresOptions();
    Task SavePostgresOptionsAsync(PgOptions postgres, CancellationToken ct = default);
}
