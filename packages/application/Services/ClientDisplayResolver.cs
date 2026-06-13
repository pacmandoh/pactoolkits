using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public static class ClientDisplayResolver
{
    public static ClientInfo Resolve(string raw, IClientAliasService aliasService)
    {
        var ci = ClientParser.Parse(raw);
        var machine = (ci.Machine ?? raw).Trim();

        var display = aliasService.Resolve(machine);
        if (string.IsNullOrWhiteSpace(display))
            display = machine.Length > 0 ? machine : ci.Display;

        return new ClientInfo(
            Raw: ci.Raw,
            Display: display,
            Machine: ci.Machine,
            User: ci.User,
            Ip: ci.Ip,
            Os: ci.Os,
            Version: ci.Version);
    }

    public static IReadOnlyList<ClientInfo> ResolveDistinct(
        IEnumerable<string> rawClients,
        IClientAliasService aliasService)
    {
        var list = new List<ClientInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawClients)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (!seen.Add(raw))
                continue;

            list.Add(Resolve(raw, aliasService));
        }

        return list;
    }
}
