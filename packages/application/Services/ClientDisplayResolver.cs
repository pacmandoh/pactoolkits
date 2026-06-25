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
        {
            display = machine.Length > 0 ? machine : ci.Display;
        }

        return new ClientInfo(
            Raw: ci.Raw,
            Display: display,
            Machine: ci.Machine,
            User: ci.User,
            Ip: ci.Ip,
            Os: ci.Os,
            Version: ci.Version);
    }
}
