using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Repositories;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardRepoTests
{
    [Fact]
    public void Constructor_has_no_alias_dependency()
    {
        var parameterTypes = typeof(DashboardRepo)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IClientAliasService), parameterTypes);
    }
}
