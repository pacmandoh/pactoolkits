using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Connectivity;

/// <summary>
/// 哪些异常只做页面 Stale 重试，哪些才通知 DB 监控断线
///
/// PacApi 瞬时失败与 Resilience 超时只重试页面；本机 Pg（Agents 等）才 Signal monitor
/// Aggregate 里 PacApi / HttpRequestException 与裸 Socket 同批时，不让 Socket 抢成库断；
/// 同批若另有 Npgsql 提供方异常，仍保留 SignalsDb（本机 Pg 真断）
/// 裸 HttpRequestException（含内层 Socket）常见于 MSFX 或更新源，不当成库断
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
            var flat = aggregate.Flatten().InnerExceptions;
            var flags = default(Flags);
            var anyRemote = false;
            foreach (var inner in flat)
            {
                flags = Or(flags, Classify(inner));
                if (HasRemoteMarker(inner))
                {
                    anyRemote = true;
                }
            }

            // PacApi / HTTP 与裸 Socket 同批：压掉 Socket 的库断；Npgsql 提供方仍保留
            if (!anyRemote)
            {
                return flags;
            }

            var signalsDb = false;
            foreach (var inner in flat)
            {
                if (HasNpgsqlProvider(inner))
                {
                    signalsDb = true;
                    break;
                }
            }

            return new Flags(flags.IsTransport, signalsDb);
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

    // Aggregate 入口已 Flatten；只扫本支 Inner 链
    private static bool HasRemoteMarker(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is PacApiException or HttpRequestException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNpgsqlProvider(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (IsPgProviderException(cur))
            {
                return true;
            }
        }

        return false;
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
