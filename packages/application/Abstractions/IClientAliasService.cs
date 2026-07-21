namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 客户端机器名别名解析与内存缓存（Changed 通知 UI）
/// </summary>
public interface IClientAliasService
{
    event Action? Changed;

    IReadOnlyDictionary<string, string> GetAll();
    string Resolve(string? machine);

    void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items);
    void Apply(IReadOnlyDictionary<string, string> aliases);
    void Reload();
}
