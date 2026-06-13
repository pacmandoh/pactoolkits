using System;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Collections.Generic;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed class ClientAliasService : IClientAliasService
{
    private readonly ClientAliasStore _store;

    public event Action? Changed;

    public ClientAliasService(ClientAliasStore store)
    {
        _store = store;
        _store.Load();
    }

    public IReadOnlyDictionary<string, string> GetAll() => _store.Snapshot();

    public string Resolve(string? machineOrClient)
    {
        var key = (machineOrClient ?? string.Empty).Trim();
        if (key.Length == 0) return string.Empty;

        var alias = _store.TryGet(key);
        return string.IsNullOrWhiteSpace(alias) ? key : alias;
    }

    public void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items)
    {
        _store.Save(items);
        Changed?.Invoke();
    }

    public void Reload()
    {
        _store.Load();
        Changed?.Invoke();
    }
}
