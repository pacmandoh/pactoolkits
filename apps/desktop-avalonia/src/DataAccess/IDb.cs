using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.DataAccess;

public interface IDb
{
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
