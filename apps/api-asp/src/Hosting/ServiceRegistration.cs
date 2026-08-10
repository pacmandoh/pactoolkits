using Microsoft.Extensions.DependencyInjection.Extensions;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Changes;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Core;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Api.Hosting;

/// <summary>API 宿主 DI 组装入口</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPacToolkitsApi(this IServiceCollection services, IConfiguration config)
    {
        services.AddPacToolkitsProblemDetails();
        services.AddPacToolkitsAuth(config);

        services
            .AddOptions<SchemaBoundsOptions>()
            .Bind(config.GetSection(SchemaBoundsOptions.SectionName))
            .Validate(
                o => IsReleaseSemVer(o.MinDbSchema) && IsReleaseSemVer(o.MaxDbSchema),
                "SchemaBounds:MinDbSchema and MaxDbSchema must be valid release SemVer (X.Y.Z)")
            .Validate(
                o => IsOrderedRange(o.MinDbSchema, o.MaxDbSchema),
                "SchemaBounds:MinDbSchema must be less than or equal to MaxDbSchema")
            .ValidateOnStart();

        services
            .AddOptions<ChangeListenOptions>()
            .Bind(config.GetSection(ChangeListenOptions.SectionName));

        services.AddSingleton<IAppLogger, HostAppLogger>();
        services.AddSingleton<IDbOptionsStore, ConfigDbOptionsStore>();
        services.AddSingleton<IClientAliasStore, EmptyClientAliasStore>();
        services.AddSingleton<ITraceCodeRuleStore, MemoryTraceCodeRuleStore>();
        services.AddSingleton<IUpdateSettingsStore, MemoryUpdateSettingsStore>();
        services.AddSingleton<IMsfxApiClient, UnsupportedMsfxApiClient>();

        services.AddPacToolkitsInfrastructure(config);
        services.AddPacToolkitsApplication();

        // API：数据面默认 not_ready，直至 SchemaBoundsAccessHost 首检；Desktop 仍用 Application 默认 Clear
        services.Replace(ServiceDescriptor.Singleton<IDbAccessGuard>(_ =>
        {
            var guard = new DbAccessGuard();
            guard.Block(SchemaBoundsAccessHost.NotReadyReason);
            return guard;
        }));

        services.AddSingleton<IApiHealth, ApiHealth>();
        services.AddHostedService<SchemaBoundsAccessHost>();
        services.AddSingleton<ChangeBus>();
        services.AddHostedService<PostgresNotifyListener>();

        return services;
    }

    private static bool IsReleaseSemVer(string? text)
        => SemVer.TryParse(text, out var value) && value.PreRelease is null;

    private static bool IsOrderedRange(string? minText, string? maxText)
    {
        if (!SemVer.TryParse(minText, out var min) || !SemVer.TryParse(maxText, out var max))
        {
            return false;
        }

        return SemVer.Compare(min, max) <= 0;
    }
}
