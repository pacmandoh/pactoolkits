using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Connectivity;

/// <summary>
/// 哪些异常走页面 Stale / 等待重试
///
/// PacAPI 瞬时失败、Resilience 超时、裸 Socket / IO 计为传输失败
/// 裸 HttpRequestException（含内层 Socket）常见于 MSFX 或更新源，不算页面传输重试
/// </summary>
public static class TransportErrors
{
    public static bool IsTransport(Exception? ex)
        => Classify(ex);

    /// <summary>从包装或 AggregateException 中取出 PacApiException</summary>
    public static bool TryFindPacApiException(Exception? ex, out PacApiException api)
    {
        api = null!;
        if (ex is null)
        {
            return false;
        }

        PacApiException? found = null;
        if (!Walk(ex, e =>
            {
                if (e is PacApiException hit)
                {
                    found = hit;
                    return true;
                }

                return false;
            }))
        {
            return false;
        }

        api = found!;
        return true;
    }

    private static bool Classify(Exception? ex)
    {
        if (ex is null)
        {
            return false;
        }

        if (ex is AggregateException aggregate)
        {
            var any = false;
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                any = any || Classify(inner);
            }

            return any;
        }

        var hasPacApi = false;
        var hasPacApiTransient = false;
        var hasHttp = false;
        var hasResilience = false;
        var hasLocalIo = false;

        for (Exception? cur = ex; cur is not null;)
        {
            if (cur is AggregateException nested)
            {
                if (hasPacApi || hasHttp)
                {
                    return hasPacApiTransient || hasResilience;
                }

                return hasLocalIo || hasResilience || Classify(nested);
            }

            if (cur is PacApiException api)
            {
                hasPacApi = true;
                if (api.IsTransient)
                {
                    hasPacApiTransient = true;
                }
            }
            else if (cur is HttpRequestException)
            {
                hasHttp = true;
            }
            else if (MatchesSocketIoLeaf(cur) || LooksLikeConnectMessageLeaf(cur))
            {
                hasLocalIo = true;
            }
            else if (IsResilienceTransient(cur))
            {
                hasResilience = true;
            }

            cur = cur.InnerException;
        }

        if (hasPacApi || hasHttp)
        {
            return hasPacApiTransient || hasResilience;
        }

        return hasLocalIo || hasResilience;
    }

    private static bool Walk(Exception ex, Func<Exception, bool> leaf)
    {
        if (leaf(ex))
        {
            return true;
        }

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                if (Walk(inner, leaf))
                {
                    return true;
                }
            }
        }

        var cur = ex.InnerException;
        while (cur is not null)
        {
            if (leaf(cur))
            {
                return true;
            }

            cur = cur.InnerException;
        }

        return false;
    }

    private static bool MatchesSocketIoLeaf(Exception ex)
        => ex is SocketException
            or TimeoutException
            or EndOfStreamException
            or IOException;

    private static bool IsResilienceTransient(Exception ex)
    {
        if (ex is TaskCanceledException { InnerException: TimeoutException })
        {
            return true;
        }

        var fullName = ex.GetType().FullName;
        return fullName is not null
               && (fullName.Contains("TimeoutRejectedException", StringComparison.Ordinal)
                   || fullName.Contains("BrokenCircuitException", StringComparison.Ordinal));
    }

    private static bool LooksLikeConnectMessageLeaf(Exception ex)
    {
        var text = ex.Message ?? string.Empty;
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
}
