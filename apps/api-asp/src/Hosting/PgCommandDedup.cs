using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Api.Hosting;

/// <summary>写命令幂等落 PostgreSQL</summary>
public sealed class PgCommandDedup : ICommandDedup
{
    private const short StatusInProgress = 0;
    private const short StatusCompleted = 1;

    private readonly IDb _db;
    private readonly IDbOptionsStore _options;

    public PgCommandDedup(IDb db, IDbOptionsStore options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool ReleaseOnFailure => false;

    public Task<CommandDedupClaimResult> ClaimAsync(CommandDedupKey key, CancellationToken ct)
    {
        CommandDedupKeys.Validate(key);
        var timeout = _options.LoadPgOptions().CommandTimeoutSeconds;
        return _db.WithConnection(
            async (conn, token) =>
            {
                if (await TryInsertAsync(conn, key, timeout, token).ConfigureAwait(false))
                {
                    return new CommandDedupClaimResult(CommandDedupClaim.Acquired, Cached: null);
                }

                await using var select = conn.CreateCommand(
                    """
                    select request_digest, status, response_status, response_content_type, response_body
                    from api_command_dedup
                    where client_id = @client_id
                      and operation = @operation
                      and command_id = @command_id
                    """,
                    timeout);
                AddKeyParams(select, key);
                await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (await TryInsertAsync(conn, key, timeout, token).ConfigureAwait(false))
                    {
                        return new CommandDedupClaimResult(CommandDedupClaim.Acquired, Cached: null);
                    }

                    return new CommandDedupClaimResult(CommandDedupClaim.InProgress, Cached: null);
                }

                var digest = reader.GetString(0);
                if (!string.Equals(digest, key.RequestDigest, StringComparison.Ordinal))
                {
                    return new CommandDedupClaimResult(CommandDedupClaim.PayloadMismatch, Cached: null);
                }

                var status = reader.GetInt16(1);
                if (status == StatusCompleted)
                {
                    var responseStatus = reader.IsDBNull(2) ? 200 : reader.GetInt32(2);
                    var contentType = reader.IsDBNull(3) ? null : reader.GetString(3);
                    byte[]? body = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4);
                    return new CommandDedupClaimResult(
                        CommandDedupClaim.Completed,
                        new CommandDedupEntry(responseStatus, contentType, body));
                }

                // 不按时间删 in_progress：活请求被删会双写；进程崩了客户端换新 CommandId
                return new CommandDedupClaimResult(CommandDedupClaim.InProgress, Cached: null);
            },
            ct);
    }

    public Task CompleteAsync(CommandDedupKey key, CommandDedupEntry entry, CancellationToken ct)
    {
        CommandDedupKeys.Validate(key);
        ArgumentNullException.ThrowIfNull(entry);
        var timeout = _options.LoadPgOptions().CommandTimeoutSeconds;
        return _db.WithConnection(
            async (conn, token) =>
            {
                await using var cmd = conn.CreateCommand(
                    """
                    update api_command_dedup
                    set status = @completed,
                        response_status = @response_status,
                        response_content_type = @response_content_type,
                        response_body = @response_body,
                        completed_at = now()
                    where client_id = @client_id
                      and operation = @operation
                      and command_id = @command_id
                      and request_digest = @request_digest
                      and status = @in_progress
                    """,
                    timeout);
                AddKeyParams(cmd, key);
                cmd.AddParam("completed", StatusCompleted);
                cmd.AddParam("in_progress", StatusInProgress);
                cmd.AddParam("response_status", entry.StatusCode);
                cmd.AddParam("response_content_type", (object?)entry.ContentType ?? DBNull.Value);
                cmd.AddParam("response_body", (object?)entry.Body ?? DBNull.Value);
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                if (rows == 0)
                {
                    throw new InvalidOperationException("command dedup key was not claimed");
                }
            },
            ct);
    }

    public Task ReleaseAsync(CommandDedupKey key, CancellationToken ct)
    {
        CommandDedupKeys.Validate(key);
        var timeout = _options.LoadPgOptions().CommandTimeoutSeconds;
        return _db.WithConnection(
            async (conn, token) =>
            {
                await using var cmd = conn.CreateCommand(
                    """
                    delete from api_command_dedup
                    where client_id = @client_id
                      and operation = @operation
                      and command_id = @command_id
                      and request_digest = @request_digest
                      and status = @in_progress
                    """,
                    timeout);
                AddKeyParams(cmd, key);
                cmd.AddParam("in_progress", StatusInProgress);
                _ = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            ct);
    }

    private static async Task<bool> TryInsertAsync(
        System.Data.IDbConnection conn,
        CommandDedupKey key,
        int timeout,
        CancellationToken token)
    {
        await using var insert = conn.CreateCommand(
            """
            insert into api_command_dedup (
              client_id, operation, command_id, request_digest, status)
            values (
              @client_id, @operation, @command_id, @request_digest, @status)
            on conflict (client_id, operation, command_id) do nothing
            returning 1
            """,
            timeout);
        AddKeyParams(insert, key);
        insert.AddParam("status", StatusInProgress);
        var acquired = await insert.ExecuteScalarAsync(token).ConfigureAwait(false);
        return acquired is not null;
    }

    private static void AddKeyParams(NpgsqlCommand cmd, CommandDedupKey key)
    {
        cmd.AddParam("client_id", key.ClientId);
        cmd.AddParam("operation", key.Operation);
        cmd.AddParam("command_id", key.CommandId);
        cmd.AddParam("request_digest", key.RequestDigest);
    }
}
