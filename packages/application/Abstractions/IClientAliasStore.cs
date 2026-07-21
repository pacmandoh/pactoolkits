namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 客户端别名持久化读写（不含业务解析）
/// </summary>
public interface IClientAliasStore
{
    IReadOnlyDictionary<string, string> Load();
    IReadOnlyDictionary<string, string> Save(IEnumerable<KeyValuePair<string, string>> items);
}
