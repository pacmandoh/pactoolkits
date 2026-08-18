using System.IO.Pipes;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsLinkTests
{
    [Fact]
    public async Task Reconnect_does_not_dispose_the_new_pipe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-agents-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var name = AgentsIpc.PipeName(dir);
            await using var serverA = CreateServer(name);
            var acceptA = serverA.WaitForConnectionAsync(ct);
            using var link = new AgentsLink();
            var connectedA = link.ConnectAsync(dir, TimeSpan.FromSeconds(3), ct);
            await acceptA;
            Assert.True(await connectedA);
            Assert.True(link.IsLinkConnected);

            await using var serverB = CreateServer(name);
            var acceptB = serverB.WaitForConnectionAsync(ct);
            var connectedB = link.ConnectAsync(dir, TimeSpan.FromSeconds(3), ct);
            await acceptB;
            Assert.True(await connectedB);

            await Task.Delay(200, ct);
            Assert.True(link.IsLinkConnected);
            Assert.True(link.TrySendDesired(["injector"]));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 临时目录删不掉就忽略
            }
        }
    }

    private static NamedPipeServerStream CreateServer(string name)
        => new(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
}
