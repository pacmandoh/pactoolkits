using System.Data;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 当前异步流上的外层库事务；内层 WithConnection 与 CreateCommand 自动加入
/// </summary>
internal static class AmbientDbScope
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static IDbConnection? Connection => Current.Value?.Connection;

    public static IDbTransaction? Transaction => Current.Value?.Transaction;

    public static IDisposable Push(IDbConnection connection, IDbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        var previous = Current.Value;
        Current.Value = new Scope(connection, transaction);
        return new Pop(previous);
    }

    private sealed record Scope(IDbConnection Connection, IDbTransaction Transaction);

    private sealed class Pop(Scope? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = previous;
        }
    }
}
