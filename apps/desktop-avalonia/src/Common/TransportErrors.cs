using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// 哪些异常只做页面 Stale 重试，哪些才通知 DB 监控断线
///
/// PacApi 瞬时失败与 Resilience 超时只重试页面；只有本机 Pg 类故障才 Signal monitor
/// 按异常分支分类：远端包装只屏蔽自己的 inner chain，不否决 Aggregate 的独立 Pg sibling
/// 裸 HttpRequestException（含内层 Socket）常见于 MSFX 或更新源，不要当成库断了
/// </summary>
public static class TransportErrors
{
    public static bool IsTransport(Exception? ex)
        => Classify(ex).IsTransport;

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

    /// <summary>仅本机 Pg 断线时通知 monitor；PacApi 与外部 HTTP 不算</summary>
    public static bool SignalsDbDisconnect(Exception? ex)
        => Classify(ex).SignalsDb;

    private readonly record struct Flags(bool IsTransport, bool SignalsDb);

    private static Flags Classify(Exception? ex)
    {
        if (ex is null)
        {
            return default;
        }

        if (ex is AggregateException aggregate)
        {
            var flags = default(Flags);
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                flags = Or(flags, Classify(inner));
            }

            return flags;
        }

        var hasPacApi = false;
        var hasPacApiTransient = false;
        var hasHttp = false;
        var hasResilience = false;
        var hasLocalDb = false;

        for (Exception? cur = ex; cur is not null;)
        {
            if (cur is AggregateException nested)
            {
                // 远端包装内的 Aggregate：整支屏蔽 DB
                if (hasPacApi || hasHttp)
                {
                    return RemoteFlags(hasPacApiTransient, hasResilience);
                }

                // 外层已识别的 Pg / Resilience 不能丢；与 nested 分支合并
                var prefix = new Flags(hasLocalDb || hasResilience, hasLocalDb);
                return Or(prefix, Classify(nested));
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
            else if (MatchesPgOrSocketLeaf(cur) || LooksLikeConnectMessageLeaf(cur))
            {
                hasLocalDb = true;
            }
            else if (IsResilienceTransient(cur))
            {
                hasResilience = true;
            }

            cur = cur.InnerException;
        }

        if (hasPacApi || hasHttp)
        {
            return RemoteFlags(hasPacApiTransient, hasResilience);
        }

        return new Flags(hasLocalDb || hasResilience, hasLocalDb);
    }

    private static Flags RemoteFlags(bool pacApiTransient, bool resilience)
        => new(pacApiTransient || resilience, SignalsDb: false);

    private static Flags Or(Flags a, Flags b)
        => new(a.IsTransport || b.IsTransport, a.SignalsDb || b.SignalsDb);

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

    private static bool MatchesPgOrSocketLeaf(Exception ex)
        => ex is SocketException
            or TimeoutException
            or EndOfStreamException
            or IOException
            || IsPgProviderException(ex);

    private static bool IsResilienceTransient(Exception ex)
    {
        // 裸 TimeoutException 归本机库；Polly / Http 超时由 PacApi 包装或走此分支
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

    private static bool IsPgProviderException(Exception ex)
        => ex.GetType().FullName?.StartsWith("Npgsql.", StringComparison.Ordinal) == true;
}
