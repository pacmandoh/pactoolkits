using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsSnapshotProjectionTests
{
    [Fact]
    public void Empty_projection_hydrates_catalog_from_status_file()
    {
        using var dir = new TempAgentsDir();
        var projection = new AgentsSnapshotProjection(new object());
        using var link = new AgentsLink();
        AgentsStatus.Write(
            dir.Root,
            AgentsStatus.Create(1, [new AgentsStatusModule { Id = "Injector" }]));

        Assert.Empty(projection.Modules);
        Assert.True(projection.TryApplyCatalogFromCaches(dir.Root, link, TimeSpan.FromMinutes(30)));
        Assert.Contains(projection.Modules, m => m.Id == "Injector");
    }

    private sealed class TempAgentsDir : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "pac-agents-catalog-" + Guid.NewGuid().ToString("N"));

        public TempAgentsDir()
            => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
