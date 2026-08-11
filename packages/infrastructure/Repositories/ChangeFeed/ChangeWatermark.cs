using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Infrastructure.Repositories;

/// <summary>app_change_watermark 只读</summary>
public sealed class ChangeWatermarkRepo : IChangeWatermarkRepo
{
    private readonly IDb _db;

    public ChangeWatermarkRepo(IDb db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<IReadOnlyList<ChangeWatermarkItem>> ListAsync(CancellationToken ct = default)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select topic, version
                from app_change_watermark
                order by topic
                """;

            await using var cmd = conn.CreateCommand(sql, timeoutSeconds: 4);
            var list = new List<ChangeWatermarkItem>();

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                {
                    continue;
                }

                var topic = reader.GetString(0);
                var version = reader.GetInt64(1);
                if (string.IsNullOrWhiteSpace(topic))
                {
                    continue;
                }

                list.Add(new ChangeWatermarkItem(topic.Trim(), version));
            }

            return (IReadOnlyList<ChangeWatermarkItem>)list;
        }, ct);
}
