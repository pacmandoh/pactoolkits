namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 定义客户端别名持久化契约，不负责解析客户端标识
/// </summary>
public interface IClientAliasStore
{
    IReadOnlyDictionary<string, string> Load();
    IReadOnlyDictionary<string, string> Save(IEnumerable<KeyValuePair<string, string>> items);
}
