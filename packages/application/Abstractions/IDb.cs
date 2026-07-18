using System.Data;

namespace PacToolkits.Application.Abstractions;

public interface IDb
{
    Task<IAsyncDisposable?> TryAcquireSessionLockAsync(
        string key,
        CancellationToken ct = default);

    Task<T> WithConnection<T>(
        Func<IDbConnection, CancellationToken, Task<T>> work,
        CancellationToken ct = default);

    Task WithConnection(
        Func<IDbConnection, CancellationToken, Task> work,
        CancellationToken ct = default);

    Task<T> WithTransaction<T>(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);

    Task WithTransaction(
        Func<IDbConnection, IDbTransaction, CancellationToken, Task> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);
}
