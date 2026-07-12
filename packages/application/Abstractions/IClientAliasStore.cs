namespace PacToolkits.Application.Abstractions;

public interface IClientAliasStore
{
    IReadOnlyDictionary<string, string> Load();
    IReadOnlyDictionary<string, string> Save(IEnumerable<KeyValuePair<string, string>> items);
}
