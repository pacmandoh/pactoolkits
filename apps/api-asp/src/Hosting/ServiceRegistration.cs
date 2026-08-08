using PacToolkits.Api.Auth;
using PacToolkits.Api.Changes;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
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
                o => !string.IsNullOrWhiteSpace(o.MinDbSchema) && !string.IsNullOrWhiteSpace(o.MaxDbSchema),
                "SchemaBounds:MinDbSchema and MaxDbSchema are required")
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

        services.AddSingleton<IApiHealth, ApiHealth>();
        services.AddHostedService<SchemaBoundsAccessHost>();
        services.AddSingleton<ChangeBus>();
        services.AddHostedService<PostgresNotifyListener>();

        return services;
    }
}
