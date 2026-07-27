using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// 实现数据库结构不兼容时的共享访问限制，供 PgDb 与独立连接路径共同使用
/// </summary>
public sealed class DbAccessGuard : IDbAccessGuard
{
    private readonly object _gate = new();
    private string? _blockReason;

    public bool IsBlocked
    {
        get
        {
            lock (_gate)
            {
                return _blockReason is not null;
            }
        }
    }

    public string? BlockReason
    {
        get
        {
            lock (_gate)
            {
                return _blockReason;
            }
        }
    }

    public void Block(string reason)
    {
        lock (_gate)
        {
            _blockReason = string.IsNullOrWhiteSpace(reason)
                ? "数据库版本不兼容，业务操作已阻断"
                : reason.Trim();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _blockReason = null;
        }
    }

    public void ThrowIfBlocked()
    {
        var reason = BlockReason;
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
