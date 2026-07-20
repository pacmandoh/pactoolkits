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
        _aliases = Normalize(_store.Load());
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

    public void Apply(IReadOnlyDictionary<string, string> aliases)
    {
        var next = Normalize(aliases);
        if (MapsEqual(_aliases, next))
        {
            return;
        }

        _aliases = next;
        Changed?.Invoke();
    }

    public void Reload() => Apply(_store.Load());

    private static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string> source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            var key = (kv.Key ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = (kv.Value ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            map[key] = value;
        }

        return map;
    }

    private static bool MapsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other)
                || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
