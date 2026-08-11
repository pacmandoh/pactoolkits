using System.Data;
using System.Net.Sockets;
using Npgsql;
using PacToolkits.Application.Abstractions;


namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 实现应用层 PostgreSQL 会话、事务和会话锁契约
///
/// 对瞬时断线执行一次受控重试，不包含业务查询
/// 兼容阻断由 <c>IDbAccessGuard</c> 在此统一执行，Application 无需逐调用检查
/// </summary>
public sealed class PgDb : IDb
{
    private readonly IPgDataSourceFactory _factory;
    private readonly IAppLogger _logger;
    private readonly IDbAccessGuard _accessGuard;

    public PgDb(
        IPgDataSourceFactory factory,
        IAppLogger logger,
        IDbAccessGuard accessGuard)
    {
        _factory = factory;
        _logger = logger;
        _accessGuard = accessGuard;
    }

    public async Task<IAsyncDisposable?> TryAcquireSessionLockAsync(
        string key,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _accessGuard.ThrowIfBlocked();

        var conn = await OpenConnectionWithRetryAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "select pg_try_advisory_lock(hashtextextended(@key, 0))";
            cmd.Parameters.AddWithValue("key", key);
            var acquired = Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (!acquired)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new SessionLock(conn, key, _logger);
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<T> WithConnection<T>(
        Func<IDbConnection, CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        return await RunWithConnectionAsync(async (conn, token) => await work(conn, token).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    public async Task WithConnection(
        Func<IDbConnection, CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        await RunWithConnectionAsync(async (conn, token) => { await work(conn, token).ConfigureAwait(false); return 0; }, ct)
            .ConfigureAwait(false);
    }

    public async Task<T> WithTransaction<T>(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        return await RunWithTransactionAsync(
                async (conn, tx, token) => await work(conn, tx, token).ConfigureAwait(false),
                isolation, ct)
            .ConfigureAwait(false);
    }

    public async Task WithTransaction(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        await RunWithTransactionAsync(
                async (conn, tx, token) => { await work(conn, tx, token).ConfigureAwait(false); return 0; },
                isolation, ct)
            .ConfigureAwait(false);
    }


    private async Task<T> RunWithConnectionAsync<T>(
        Func<IDbConnection, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        _accessGuard.ThrowIfBlocked();
        await using var conn = await OpenConnectionWithRetryAsync(ct).ConfigureAwait(false);
        return await work(conn, ct).ConfigureAwait(false);
    }

    private async Task<T> RunWithTransactionAsync<T>(
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
            // 重试前清理连接池中的失效连接，避免立即复用相同故障连接
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

    private sealed class SessionLock(
        NpgsqlConnection connection,
        string key,
        IAppLogger logger) : IAsyncDisposable
    {
        private NpgsqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var conn = Interlocked.Exchange(ref _connection, null);
            if (conn is null)
            {
                return;
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "select pg_advisory_unlock(hashtextextended(@key, 0))";
                cmd.Parameters.AddWithValue("key", key);
                _ = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn("PgDb", "session_lock.release.fail", "PostgreSQL session lock release failed", ex);
                NpgsqlConnection.ClearPool(conn);
            }
            finally
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
