using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class PageRegistrationTests
{
    private static readonly Type[] ExpectedPages =
    [
        typeof(Dashboard),
        typeof(InventoryOverview),
        typeof(DrugIndex),
        typeof(ScanCode),
        typeof(MsfxLink),
        typeof(Settings),
    ];

    [Fact]
    public void AddPageViewModels_registers_complete_singleton_catalog()
    {
        var services = new ServiceCollection();

        services.AddPageViewModels();

        var pageEntries = services
            .Where(descriptor => descriptor.ServiceType == typeof(AppPageBase))
            .ToArray();

        Assert.Equal(ExpectedPages.Length, pageEntries.Length);
        Assert.All(pageEntries, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));

        foreach (var pageType in ExpectedPages)
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == pageType);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.Equal(pageType, descriptor.ImplementationType);
        }
    }

    [Fact]
    public void AddAppPage_reuses_concrete_singleton_in_page_collection()
    {
        var services = new ServiceCollection();
        services.AddAppPage<TestPage>();
        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<TestPage>();
        var page = Assert.Single(provider.GetServices<AppPageBase>());

        Assert.Same(concrete, page);
    }

    private sealed class TestPage : AppPageBase
    {
        public override string DisplayName => "Test";
        public override string Icon => "Test";
        public override int Index => 0;
    }
}
