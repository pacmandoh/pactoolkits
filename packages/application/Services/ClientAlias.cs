using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

public sealed class ClientAliasService : IClientAliasService
{
    private readonly IClientAliasStore _store;
    private IReadOnlyDictionary<string, string> _aliases;

    public event Action? Changed;

    public ClientAliasService(IClientAliasStore store)
    {
        _store = store;
        _aliases = _store.Load();
    }

    public IReadOnlyDictionary<string, string> GetAll() => _aliases;

    public string Resolve(string? machineOrClient)
    {
        var key = (machineOrClient ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return string.Empty;
        }

        _aliases.TryGetValue(key, out var alias);
        return string.IsNullOrWhiteSpace(alias) ? key : alias;
    }

    public void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items)
    {
        _aliases = _store.Save(items);
        Changed?.Invoke();
    }

    public void Reload()
    {
        _aliases = _store.Load();
        Changed?.Invoke();
    }
}
