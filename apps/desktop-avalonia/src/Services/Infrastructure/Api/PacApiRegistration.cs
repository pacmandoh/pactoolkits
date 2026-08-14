using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using Polly;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>注册 PacAPI 的 HttpClient（按名称区分）；普通请求只对 GET 做 Resilience</summary>
internal static class PacApiRegistration
{
    public static IServiceCollection AddPacApiClient(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<PacApiOptions>, PacApiOptionsValidator>();
        services.AddOptions<PacApiOptions>()
            .Configure<IAppConfigStore>((opts, store) =>
            {
                var saved = store.Load().PacApi ?? new PacApiOptions();
                opts.BaseUrl = saved.BaseUrl ?? string.Empty;
                opts.ApiKey = saved.ApiKey ?? string.Empty;
                opts.HeaderName = string.IsNullOrWhiteSpace(saved.HeaderName)
                    ? "X-Api-Key"
                    : saved.HeaderName;
            });
        services.TryAddSingleton(TimeProvider.System);
        // App 用真实 Gate 覆盖；单测只 AddPacApiClient 时放行
        services.TryAddSingleton<IPacApiContractGate, AllowAllPacApiContractGate>();
        services.AddTransient<PacApiTraceHandler>();
        services.AddTransient<PacApiJwtHandler>();

        // Jwt 在外（换票时 Current 仍是页面 span）；Trace 仅包实际出站，避免换票嵌在 pacapi.http 下
        services.AddHttpClient(PacApiClient.TokenClientName)
            .ConfigureHttpClient(static client => client.Timeout = PacApiClient.ApiTimeout)
            .AddHttpMessageHandler<PacApiTraceHandler>();

        services.AddHttpClient(PacApiClient.ApiClientName)
            .ConfigureHttpClient(static client => client.Timeout = PacApiClient.ApiTimeout)
            .AddHttpMessageHandler<PacApiJwtHandler>()
            .AddHttpMessageHandler<PacApiTraceHandler>()
            .AddResilienceHandler("pac-api-get", static builder => ConfigureGetOnlyPipeline(builder));

        // 可用性探测：Jwt 同业务，但不挂 Resilience
        services.AddHttpClient(PacApiClient.AvailabilityClientName)
            .ConfigureHttpClient(static client => client.Timeout = PacApiClient.AvailabilityAttemptTimeout)
            .AddHttpMessageHandler<PacApiJwtHandler>()
            .AddHttpMessageHandler<PacApiTraceHandler>();

        services.AddHttpClient(PacApiClient.SseClientName)
            .ConfigureHttpClient(static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<PacApiJwtHandler>()
            .AddHttpMessageHandler<PacApiTraceHandler>();

        services.AddSingleton<PacApiClient>();
        return services;
    }

    private sealed class AllowAllPacApiContractGate : IPacApiContractGate
    {
        public Task EnsureCompatibleAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public void Reset()
        {
        }
    }

    // Retry、熔断、单次 Timeout 都只作用于 GET；写请求只靠外层 HttpClient.Timeout
    internal static void ConfigureGetOnlyPipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder)
        => ConfigureGetOnlyPipeline(builder, attemptTimeout: TimeSpan.FromSeconds(10));

    internal static void ConfigureGetOnlyPipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        TimeSpan attemptTimeout)
    {
        builder.AddRetry(CreateGetOnlyRetryOptions());
        builder.AddCircuitBreaker(CreateGetOnlyCircuitOptions());
        builder.AddTimeout(CreateAttemptTimeoutOptions(attemptTimeout));
    }

    internal static HttpRetryStrategyOptions CreateGetOnlyRetryOptions()
    {
        var options = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
        };

        var transient = options.ShouldHandle;
        options.ShouldHandle = args =>
        {
            var request = args.Context.GetRequestMessage();
            if (request is null || request.Method != HttpMethod.Get)
            {
                return PredicateResult.False();
            }

            return transient(args);
        };

        return options;
    }

    internal static HttpCircuitBreakerStrategyOptions CreateGetOnlyCircuitOptions()
    {
        var options = new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(15),
        };

        var transient = options.ShouldHandle;
        options.ShouldHandle = args =>
        {
            var request = args.Context.GetRequestMessage();
            if (request is null || request.Method != HttpMethod.Get)
            {
                return PredicateResult.False();
            }

            return transient(args);
        };

        return options;
    }

    internal static HttpTimeoutStrategyOptions CreateAttemptTimeoutOptions(TimeSpan attemptTimeout)
    {
        // Timeout 策略没有 ShouldHandle；非 GET 给 Infinite，避免写请求被 10s 截断
        return new HttpTimeoutStrategyOptions
        {
            Timeout = attemptTimeout,
            TimeoutGenerator = args =>
            {
                var request = args.Context.GetRequestMessage();
                if (request is null || request.Method != HttpMethod.Get)
                {
                    return ValueTask.FromResult(Timeout.InfiniteTimeSpan);
                }

                return ValueTask.FromResult(attemptTimeout);
            },
        };
    }
}
