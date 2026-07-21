namespace PacToolkits.Application.Abstractions;

/// <summary>
/// PgOptions 本地文件读写
/// </summary>
public interface IDbOptionsStore
{
    string ConfigPath { get; }
    PgOptions LoadPgOptions();
    Task SavePgOptionsAsync(PgOptions options, CancellationToken ct = default);
}
