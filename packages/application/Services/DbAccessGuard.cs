using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// Schema 不兼容共享阻断门实现；PgDb 与直连路径共用
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
