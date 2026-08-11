using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 追溯码池乐观锁冲突；可选附带当前行快照供 UI 刷新
/// </summary>
public sealed class TracePoolConcurrencyException : Exception
{
    public TracePoolStockRowDto? Current { get; }

    public TracePoolConcurrencyException(string message, TracePoolStockRowDto? current = null)
        : base(message)
    {
        Current = current;
    }
}
