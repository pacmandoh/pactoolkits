using System.Data;
using System.Net.Sockets;
using Npgsql;
using PacToolkits.Application.Abstractions;


namespace PacToolkits.Infrastructure.Database;

// All business database access should go through IDb. Compatibility blocking is enforced
// here via IDatabaseAccessGuard so Application services do not need per-call checks.
public sealed class PgDb : IDb
{
    private readonly IPgDataSourceFactory _factory;
    private readonly IAppLogger _logger;
    private readonly IDatabaseAccessGuard _accessGuard;

    public PgDb(
        IPgDataSourceFactory factory,
        IAppLogger logger,
        IDatabaseAccessGuard accessGuard)
    {
        _factory = factory;
        _logger = logger;
        _accessGuard = accessGuard;
    }

    public async Task<T> WithConnection<T>(
        Func<IDbConnection, CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        return await WithConnectionCore(async (conn, token) => await work(conn, token).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    public async Task WithConnection(
        Func<IDbConnection, CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        await WithConnectionCore(async (conn, token) => { await work(conn, token).ConfigureAwait(false); return 0; }, ct)
            .ConfigureAwait(false);
    }

    public async Task<T> WithTransaction<T>(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        return await WithTransactionCore(
                async (conn, tx, token) => await work(conn, tx, token).ConfigureAwait(false),
                isolation, ct)
            .ConfigureAwait(false);
    }

    public async Task WithTransaction(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        await WithTransactionCore(
                async (conn, tx, token) => { await work(conn, tx, token).ConfigureAwait(false); return 0; },
                isolation, ct)
            .ConfigureAwait(false);
    }


    private async Task<T> WithConnectionCore<T>(
        Func<IDbConnection, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        _accessGuard.ThrowIfBlocked();
        await using var conn = await OpenConnectionWithRetryAsync(ct).ConfigureAwait(false);
        return await work(conn, ct).ConfigureAwait(false);
    }

    private async Task<T> WithTransactionCore<T>(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> work,
        IsolationLevel isolation,
        CancellationToken ct)
    {
        _accessGuard.ThrowIfBlocked();
        await using var conn = await OpenConnectionWithRetryAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(isolation, ct).ConfigureAwait(false);

        try
        {
            var result = await work(conn, tx, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (System.Exception rollbackEx)
            {
                _logger.Warn("PgDb", "tx.rollback.fail", "Transaction rollback failed", rollbackEx);
            }

            throw;
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionWithRetryAsync(CancellationToken ct)
    {
        try
        {
            return await _factory.Get().OpenConnectionAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientDisconnect(ex, ct))
        {
            _logger.Warn("PgDb", "conn.open.transient_disconnect.retry", "Transient disconnect detected while opening connection, retrying once", ex);
            // Clear stale pooled connectors before a single open retry.
            SafeClearPools();
            return await _factory.Get().OpenConnectionAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool IsTransientDisconnect(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return false;
        }

        if (ex is NpgsqlException)
        {
            return true;
        }

        if (ex is EndOfStreamException)
        {
            return true;
        }

        if (ex is IOException)
        {
            return true;
        }

        if (ex is SocketException)
        {
            return true;
        }

        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is NpgsqlException or EndOfStreamException or IOException or SocketException)
            {
                return true;
            }

            inner = inner.InnerException;
        }

        return false;
    }

    private void SafeClearPools()
    {
        try
        {
            NpgsqlConnection.ClearAllPools();
        }
        catch (System.Exception ex)
        {
            _logger.Warn("PgDb", "pool.clear.fail", "Failed to clear Npgsql pools", ex);
        }
    }
}
