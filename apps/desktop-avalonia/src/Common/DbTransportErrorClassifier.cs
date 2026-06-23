using System;
using System.Net.Sockets;
using System.Text;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class DbTransportErrorClassifier
{
    public static bool IsTransportError(Exception? ex)
    {
        if (ex is null)
        {
            return false;
        }

        if (IsTransportErrorCore(ex))
        {
            return true;
        }

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                if (IsTransportError(inner))
                {
                    return true;
                }
            }
        }

        return LooksLikeEndpointUnreachable(ex);
    }

    private static bool IsTransportErrorCore(Exception ex)
    {
        if (IsPgProviderException(ex))
        {
            return true;
        }

        if (ex is SocketException or TimeoutException or System.IO.EndOfStreamException or System.IO.IOException)
        {
            return true;
        }

        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (IsTransportErrorCore(inner))
            {
                return true;
            }

            inner = inner.InnerException;
        }

        return false;
    }

    private static bool LooksLikeEndpointUnreachable(Exception ex)
    {
        var text = CollectMessages(ex);
        if (text.Length == 0)
        {
            return false;
        }

        return text.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
               || text.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
               || text.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
               || text.Contains("could not connect", StringComparison.OrdinalIgnoreCase)
               || text.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
               || text.Contains("broken pipe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPgProviderException(Exception ex)
        => ex.GetType().FullName?.StartsWith("Npgsql.", StringComparison.Ordinal) == true;

    private static string CollectMessages(Exception ex)
    {
        var sb = new StringBuilder();
        var cur = ex;
        while (cur is not null)
        {
            if (!string.IsNullOrWhiteSpace(cur.Message))
            {
                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(cur.Message);
            }

            cur = cur.InnerException;
        }

        return sb.ToString();
    }
}
