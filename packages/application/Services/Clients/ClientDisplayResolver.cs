using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

/// <summary>
/// 将原始客户端标识解析为带显示别名的客户端信息和筛选选项
/// 按显示名称聚合 KPI，避免同一设备别名被重复统计
/// </summary>
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
            Version: ci.Version,
            Machines: string.IsNullOrWhiteSpace(machine) ? [] : [machine]);
    }

    public static IReadOnlyList<ClientInfo> OptionsByDisplay(
        IEnumerable<string> machines,
        IClientAliasService aliasService)
        => GroupByDisplay(
                machines.Select(static machine => (ClientRaw: machine, Value: 0L)),
                aliasService,
                pickBestByValue: false)
            .Select(static row => row.Client)
            .OrderBy(static client => client.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<(ClientInfo Client, long Value)> AggregateByDisplay(
        IEnumerable<(string ClientRaw, long Value)> rows,
        IClientAliasService aliasService)
        => GroupByDisplay(rows, aliasService, pickBestByValue: true)
            .OrderByDescending(static row => row.Value)
            .ThenBy(static row => row.Client.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<(ClientInfo Client, long Value)> GroupByDisplay(
        IEnumerable<(string ClientRaw, long Value)> rows,
        IClientAliasService aliasService,
        bool pickBestByValue)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(aliasService);

        // 同 Display 合并；pickBestByValue 决定代表机与 KPI 口径
        return rows
            .Select(row => (Client: Resolve(row.ClientRaw, aliasService), row.Value))
            .Where(row => !string.IsNullOrWhiteSpace(row.Client.Display))
            .GroupBy(row => row.Client.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sample = pickBestByValue
                    ? group
                        .OrderByDescending(row => row.Value)
                        .ThenBy(row => row.Client.Raw, StringComparer.OrdinalIgnoreCase)
                        .First()
                    : group
                        .OrderBy(row => row.Client.Raw, StringComparer.OrdinalIgnoreCase)
                        .First();

                var machines = group
                    .SelectMany(row => row.Client.MachineKeys)
                    .Where(static machine => !string.IsNullOrWhiteSpace(machine))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static machine => machine, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return (
                    Client: sample.Client with { Machines = machines },
                    Value: group.Sum(row => row.Value));
            })
            .ToList();
    }
}
