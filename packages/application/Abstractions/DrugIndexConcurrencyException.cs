using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 药品索引乐观锁冲突；可选附带当前行快照供 UI 刷新
/// </summary>
public sealed class DrugIndexConcurrencyException : Exception
{
    public DrugIndexDto? Current { get; }

    public DrugIndexConcurrencyException(string message, DrugIndexDto? current = null)
        : base(message)
    {
        Current = current;
    }
}
