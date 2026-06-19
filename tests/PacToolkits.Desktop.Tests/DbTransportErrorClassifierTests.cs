using System.Net.Sockets;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class DbTransportErrorClassifierTests
{
    [Fact]
    public void IsTransportError_returns_true_for_failed_to_connect_message()
    {
        var ex = new Exception("Failed to connect to [::1]:5432");

        Assert.True(DbTransportErrorClassifier.IsTransportError(ex));
    }

    [Fact]
    public void IsTransportError_returns_true_for_socket_exception()
    {
        var ex = new SocketException((int)SocketError.ConnectionRefused);

        Assert.True(DbTransportErrorClassifier.IsTransportError(ex));
    }

    [Fact]
    public void IsTransportError_returns_true_for_aggregate_wrapping_connection_message()
    {
        var inner = new Exception("Failed to connect to [::1]:5432");
        var ex = new AggregateException(inner);

        Assert.True(DbTransportErrorClassifier.IsTransportError(ex));
    }

    [Fact]
    public void IsTransportError_returns_false_for_unrelated_business_error()
    {
        var ex = new InvalidOperationException("duplicate key value violates unique constraint");

        Assert.False(DbTransportErrorClassifier.IsTransportError(ex));
    }
}
