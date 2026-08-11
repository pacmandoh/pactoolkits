using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class ClientDisplayResolverTests
{
    [Fact]
    public void AggregateByDisplay_merges_raw_machines_that_share_an_alias()
    {
        var aliases = new MapAliasService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Desktop-fgds\\adminxyf"] = "西药房",
            ["Desktop-fgds"] = "西药房"
        });

        var aggregated = ClientDisplayResolver.AggregateByDisplay(
            [
                ("Desktop-fgds\\adminxyf|adminxyf|ip=1.1.1.1", 10),
                ("Desktop-fgds|admin|ip=1.1.1.2", 7)
            ],
            aliases);

        Assert.Single(aggregated);
        Assert.Equal("西药房", aggregated[0].Client.Display);
        Assert.Equal(17, aggregated[0].Value);
        Assert.Equal("Desktop-fgds\\adminxyf|adminxyf|ip=1.1.1.1", aggregated[0].Client.Raw);
        Assert.Equal(2, aggregated[0].Client.MachineKeys.Count);
        Assert.Contains("Desktop-fgds\\adminxyf", aggregated[0].Client.MachineKeys);
        Assert.Contains("Desktop-fgds", aggregated[0].Client.MachineKeys);
    }

    [Fact]
    public void OptionsByDisplay_keeps_one_entry_per_alias_with_all_machines()
    {
        var aliases = new MapAliasService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Desktop-fgds\\adminxyf"] = "西药房",
            ["Desktop-fgds"] = "西药房",
            ["Other"] = "中药房"
        });

        var options = ClientDisplayResolver.OptionsByDisplay(
            ["Desktop-fgds\\adminxyf", "Desktop-fgds", "Other"],
            aliases);

        Assert.Equal(2, options.Count);
        Assert.Equal(["中药房", "西药房"], options.Select(option => option.Display).ToArray());

        var pharmacy = options.Single(option => option.Display == "西药房");
        Assert.Equal("Desktop-fgds", pharmacy.Raw);
        Assert.Equal(2, pharmacy.MachineKeys.Count);
        Assert.Contains("Desktop-fgds\\adminxyf", pharmacy.MachineKeys);
        Assert.Contains("Desktop-fgds", pharmacy.MachineKeys);
        Assert.True(pharmacy.ContainsMachine("Desktop-fgds\\adminxyf"));
    }

    [Fact]
    public void AggregateByDisplay_keeps_distinct_aliases_separate()
    {
        var aliases = new MapAliasService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "西药房",
            ["B"] = "中药房"
        });

        var aggregated = ClientDisplayResolver.AggregateByDisplay(
            [
                ("A|u1", 3),
                ("B|u2", 5)
            ],
            aliases);

        Assert.Equal(2, aggregated.Count);
        Assert.Equal("中药房", aggregated[0].Client.Display);
        Assert.Equal(5, aggregated[0].Value);
        Assert.Equal("西药房", aggregated[1].Client.Display);
        Assert.Equal(3, aggregated[1].Value);
    }

    private sealed class MapAliasService(IReadOnlyDictionary<string, string> aliases) : IClientAliasService
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyDictionary<string, string> GetAll() => aliases;

        public string Resolve(string? machine)
        {
            var key = (machine ?? string.Empty).Trim();
            return aliases.TryGetValue(key, out var alias) && !string.IsNullOrWhiteSpace(alias)
                ? alias
                : key;
        }

        public void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items)
            => throw new NotSupportedException();

        public void Apply(IReadOnlyDictionary<string, string> next)
        {
        }

        public void Reload()
        {
        }
    }
}
