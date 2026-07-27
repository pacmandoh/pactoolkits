using System.Net.Sockets;
using System.Text;
using Npgsql;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 将 Npgsql 和网络异常归类为设置页可展示的连接失败原因
/// 不抛出、不记录日志
/// </summary>
internal static class DbConnectionDiagnostics
{
    public static (string reason, string? sqlState) Classify(Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        if (root is SocketException se)
        {
            var reason = se.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "连接被拒绝：Postgres 未启动/端口未监听/被防火墙拦截",
                SocketError.HostNotFound => "找不到主机：Host/DNS 配置错误",
                SocketError.TimedOut => "连接超时：网络不通/路由问题/服务器无响应",
                _ => $"网络错误：{se.SocketErrorCode}"
            };
            return (reason, null);
        }

        if (root is TimeoutException)
        {
            return ("连接超时：网络不通/服务器无响应", null);
        }

        var pe = FindInChain<PostgresException>(ex);
        if (pe is not null)
        {
            var reason = pe.SqlState switch
            {
                "28P01" => "认证失败：用户名或密码错误",
                "28000" => "认证失败：无效授权（用户/认证配置问题）",
                "57P03" => "数据库启动中：稍后重试",
                "53300" => "连接数已满：too many connections",
                _ => $"数据库错误：{pe.SqlState}（详见日志）"
            };
            return (reason, pe.SqlState);
        }

        if (ex is NpgsqlException)
        {
            var msgAll = CollectMessages(ex);

            if (msgAll.Contains("no pg_hba.conf entry", StringComparison.OrdinalIgnoreCase))
            {
                return ("访问被拒绝：pg_hba.conf 未允许该来源/用户/库", null);
            }

            if (msgAll.Contains("pg_hba.conf rejects", StringComparison.OrdinalIgnoreCase))
            {
                return ("访问被拒绝：pg_hba.conf 拒绝连接", null);
            }

            if (msgAll.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase))
            {
                return ("认证失败：用户名或密码错误", "28P01");
            }

            if (msgAll.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || msgAll.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || msgAll.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || msgAll.Contains("handshake", StringComparison.OrdinalIgnoreCase))
            {
                return ("SSL/TLS 协商失败：检查 SSL 选项/证书/服务器要求", null);
            }

            return ("连接失败：数据库返回错误（详见日志）", null);
        }

        return ("未知错误：连接失败（详见日志）", null);
    }

    private static T? FindInChain<T>(Exception ex) where T : Exception
    {
        var cur = ex;
        while (cur is not null)
        {
            if (cur is T hit)
            {
                return hit;
            }

            cur = cur.InnerException;
        }
        return null;
    }

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
