namespace PacToolkits.Application.Abstractions;

public interface IClientAliasService
{
    event Action? Changed;

    IReadOnlyDictionary<string, string> GetAll();
    string Resolve(string? machine);

    void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items);
    void Reload();
}
