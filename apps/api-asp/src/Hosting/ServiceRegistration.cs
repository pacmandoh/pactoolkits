using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Changes;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Application.Services;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Api.Hosting;

/// <summary>API 宿主 DI 组装入口</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPacToolkitsApi(this IServiceCollection services, IConfiguration config)
    {
        PacActivities.EnsureListening();
        services.AddPacToolkitsProblemDetails();
        services.AddPacToolkitsAuth(config);

        services.AddSingleton<IValidateOptions<SchemaBoundsOptions>, SchemaBoundsOptionsValidator>();
        services
            .AddOptions<SchemaBoundsOptions>()
            .Bind(config.GetSection(SchemaBoundsOptions.SectionName))
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

        // API：首检前默认 Block；Desktop 用 Application 默认的 Clear
        services.Replace(ServiceDescriptor.Singleton<IDbAccessGuard>(_ =>
        {
            var guard = new DbAccessGuard();
            guard.Block(SchemaBoundsAccessHost.NotReadyReason);
            return guard;
        }));

        // 业务写幂等落库；MemoryCommandDedup 仅测试用
        services.AddSingleton<ICommandDedup, PgCommandDedup>();

        services.AddSingleton<IApiHealth, ApiHealth>();
        services.AddHostedService<SchemaBoundsAccessHost>();
        services.AddSingleton<ChangeBus>();
        services.AddHostedService<PostgresNotifyListener>();

        return services;
    }
}
