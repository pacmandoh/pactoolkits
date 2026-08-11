using System.Net.Sockets;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class TransportErrorsTests
{
    [Fact]
    public void IsTransport_false_for_bare_http_request_exception()
        => Assert.False(TransportErrors.IsTransport(new HttpRequestException("boom")));

    [Fact]
    public void SignalsDbDisconnect_false_for_bare_http_request_exception()
        => Assert.False(TransportErrors.SignalsDbDisconnect(new HttpRequestException("boom")));

    [Fact]
    public void Http_wrapped_socket_does_not_signal_db_or_count_as_transport()
    {
        var ex = new HttpRequestException("upstream", new SocketException((int)SocketError.ConnectionRefused));
        Assert.False(TransportErrors.IsTransport(ex));
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Bare_socket_exception_is_local_db_transport()
    {
        var ex = new SocketException((int)SocketError.ConnectionRefused);
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Bare_io_exception_is_local_db_transport()
    {
        var ex = new IOException("Unable to read data from the transport connection");
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Bare_end_of_stream_is_local_db_transport()
    {
        var ex = new EndOfStreamException();
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Bare_timeout_exception_is_local_db_transport()
    {
        var ex = new TimeoutException("command timeout");
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void IsTransport_true_for_failed_to_connect_on_any_exception()
    {
        var ex = new Exception("Failed to connect to [::1]:5432");
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void IsTransport_true_for_transient_pac_api_exception()
    {
        var ex = new PacApiException(new PacApiProblem(
            Status: 503,
            Code: "service_unavailable",
            Title: "down",
            Detail: null,
            TraceId: "abc",
            RetryAfter: null));
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void TryFindPacApiException_walks_wrapper_and_aggregate()
    {
        var inner = new PacApiException(new PacApiProblem(
            Status: 429,
            Code: "rate_limited",
            Title: "slow",
            Detail: null,
            TraceId: "t",
            RetryAfter: TimeSpan.FromSeconds(9)));
        var wrapped = new InvalidOperationException("wrap", inner);
        var aggregate = new AggregateException(new Exception("other"), wrapped);

        Assert.True(TransportErrors.TryFindPacApiException(aggregate, out var found));
        Assert.Same(inner, found);
        Assert.Equal(TimeSpan.FromSeconds(9), found.RetryAfter);
    }

    [Fact]
    public void PacApi_with_inner_socket_does_not_signal_db()
    {
        var ex = new PacApiException(
            new PacApiProblem(
                Status: 503,
                Code: "transport",
                Title: "body cut",
                Detail: null,
                TraceId: "t",
                RetryAfter: null),
            new IOException("cut", new SocketException((int)SocketError.ConnectionReset)));

        Assert.True(TransportErrors.IsTransport(ex));
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void IsTransport_false_for_business_conflict()
    {
        var ex = new PacApiException(new PacApiProblem(
            Status: 409,
            Code: "conflict",
            Title: "conflict",
            Detail: null,
            TraceId: null,
            RetryAfter: null));
        Assert.False(TransportErrors.IsTransport(ex));
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void IsTransport_true_for_pg_connect_message()
    {
        var ex = new InvalidOperationException("Failed to connect to 127.0.0.1");
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void IsTransport_true_for_aggregate_wrapping_connection_message()
    {
        var ex = new AggregateException(
            new InvalidOperationException("wrapper"),
            new InvalidOperationException("Failed to connect"));
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void IsTransport_false_for_unrelated_business_error()
        => Assert.False(TransportErrors.IsTransport(
            new InvalidOperationException("duplicate key value violates unique constraint")));

    [Fact]
    public void Mixed_aggregate_pac_api_and_socket_still_signals_db()
    {
        var api = new PacApiException(new PacApiProblem(
            Status: 503,
            Code: "service_unavailable",
            Title: "down",
            Detail: null,
            TraceId: "t",
            RetryAfter: null));
        var ex = new AggregateException(api, new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Mixed_aggregate_http_and_socket_still_signals_db()
    {
        var http = new HttpRequestException(
            "upstream",
            new SocketException((int)SocketError.ConnectionReset));
        var ex = new AggregateException(
            http,
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void PacApi_wrapping_aggregate_sockets_does_not_signal_db()
    {
        var ex = new PacApiException(
            new PacApiProblem(
                Status: 503,
                Code: "transport",
                Title: "body cut",
                Detail: null,
                TraceId: "t",
                RetryAfter: null),
            new AggregateException(
                new SocketException((int)SocketError.ConnectionReset),
                new SocketException((int)SocketError.ConnectionRefused)));

        Assert.True(TransportErrors.IsTransport(ex));
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Npgsql_wrapping_aggregate_still_signals_db()
    {
        var ex = new Npgsql.FakeNpgsqlException(
            "connection broken",
            new AggregateException(new InvalidOperationException("business")));

        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }

    [Fact]
    public void Connect_message_wrapping_aggregate_still_signals_db()
    {
        var ex = new InvalidOperationException(
            "Failed to connect to 127.0.0.1",
            new AggregateException(new InvalidOperationException("business")));

        Assert.True(TransportErrors.IsTransport(ex));
        Assert.True(TransportErrors.SignalsDbDisconnect(ex));
    }
}
