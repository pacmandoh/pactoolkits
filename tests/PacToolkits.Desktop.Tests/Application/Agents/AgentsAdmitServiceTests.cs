using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsAdmitServiceTests
{
    [Fact]
    public async Task Admit_skips_contract_when_module_has_no_range()
    {
        var sut = new AgentsAdmitService();
        var result = await sut.AdmitAsync(
            new AgentsModuleBound("LocalOnly", null, null),
            apiReady: false,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.None, result.DenyKind);
    }

    [Fact]
    public async Task Admit_disconnect_when_module_requires_contract()
    {
        var sut = new AgentsAdmitService();
        var result = await sut.AdmitAsync(
            new AgentsModuleBound("Injector", "1.4.0", "1.4.0"),
            apiReady: false,
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.Disconnected, result.DenyKind);
    }

    [Fact]
    public async Task Admit_rejects_contract_below_min()
    {
        var sut = new AgentsAdmitService();
        var result = await sut.AdmitAsync(
            new AgentsModuleBound("Injector", "1.4.0", "1.4.0"),
            apiReady: true,
            contractVersion: "1.3.0",
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.ContractOutOfRange, result.DenyKind);
        Assert.Contains("过低", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmitMany_mixes_results_from_shared_contract()
    {
        var sut = new AgentsAdmitService();
        var results = await sut.AdmitManyAsync(
            [
                new AgentsModuleBound("A", "1.3.0", "1.4.0"),
                new AgentsModuleBound("B", null, null),
                new AgentsModuleBound("C", "1.4.0", "1.4.0"),
            ],
            apiReady: true,
            contractVersion: "1.3.0",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Ok);
        Assert.True(results[1].Ok);
        Assert.False(results[2].Ok);
        Assert.Equal(AgentsAdmitDenyKind.ContractOutOfRange, results[2].DenyKind);
    }

    [Fact]
    public async Task Admit_rejects_when_contract_unread()
    {
        var sut = new AgentsAdmitService();
        var result = await sut.AdmitAsync(
            new AgentsModuleBound("Injector", "1.4.0", "1.4.0"),
            apiReady: true,
            contractVersion: null,
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.ContractUnread, result.DenyKind);
    }
}
