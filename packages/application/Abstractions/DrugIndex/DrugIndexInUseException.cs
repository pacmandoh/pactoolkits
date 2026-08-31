namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 药品规格仍被追溯码池、执行事务或码上放心映射引用，无法删除
/// </summary>
public sealed class DrugIndexInUseException : Exception
{
    public const string DefaultMessage = "当前药品规格已被追溯码池、执行事务或码上放心映射引用，无法删除";

    public DrugIndexInUseException(Exception? inner = null)
        : base(DefaultMessage, inner)
    {
    }
}
