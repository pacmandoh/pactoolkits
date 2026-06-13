using System;
using System.Data;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.DataAccess;

public sealed class PgDb : IDb
{
    private readonly IPgDataSourceFactory _factory;

    public PgDb(IPgDataSourceFactory factory)
    {
        _factory = factory;
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
        try
        {
            await using var conn = await _factory.Get().OpenConnectionAsync(ct).ConfigureAwait(false);
            return await work(conn, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientDisconnect(ex, ct))
        {
            AppLog.Warn("PgDb", "conn.transient_disconnect.retry", "Transient disconnect detected, retrying connection", ex);
            // Reason: Clear stale pooled connectors before a single retry.
            SafeClearPools();

            await using var conn = await _factory.Get().OpenConnectionAsync(ct).ConfigureAwait(false);
            return await work(conn, ct).ConfigureAwait(false);
        }
    }

    private async Task<T> WithTransactionCore<T>(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> work,
        IsolationLevel isolation,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _factory.Get().OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(isolation, ct).ConfigureAwait(false);

            try
            {
                var result = await work(conn, tx, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch
            {
                try { await tx.RollbackAsync(ct).ConfigureAwait(false); }
                catch (System.Exception rollbackEx)
                {
                    AppLog.Warn("PgDb", "tx.rollback.fail", "Transaction rollback failed", rollbackEx);
                }

                throw;
            }
        }
        catch (Exception ex) when (IsTransientDisconnect(ex, ct))
        {
            AppLog.Warn("PgDb", "tx.transient_disconnect.retry", "Transient disconnect detected, retrying transaction", ex);
            // Reason: Clear stale pooled connectors before a single retry.
            SafeClearPools();

            await using var conn = await _factory.Get().OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(isolation, ct).ConfigureAwait(false);

            try
            {
                var result = await work(conn, tx, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch
            {
                try { await tx.RollbackAsync(ct).ConfigureAwait(false); }
                catch (System.Exception rollbackEx)
                {
                    AppLog.Warn("PgDb", "tx.retry.rollback.fail", "Transaction rollback failed on retry path", rollbackEx);
                }

                throw;
            }
        }
    }

    private static bool IsTransientDisconnect(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;

        if (ex is NpgsqlException) return true;
        if (ex is EndOfStreamException) return true;
        if (ex is IOException) return true;
        if (ex is SocketException) return true;

        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is NpgsqlException or EndOfStreamException or IOException or SocketException)
                return true;
            inner = inner.InnerException;
        }

        return false;
    }

    private static void SafeClearPools()
    {
        try
        {
            NpgsqlConnection.ClearAllPools();
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("PgDb", "pool.clear.fail", "Failed to clear Npgsql pools", ex);
        }
    }
}
