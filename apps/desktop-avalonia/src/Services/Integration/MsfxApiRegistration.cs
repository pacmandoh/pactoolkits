using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PacToolkits.Application.Abstractions;
using Polly;

namespace PacToolkits.Desktop.Avalonia.Services.Integration;

/// <summary>
/// 注册 MSFX 命名 HttpClient 与传输层 Resilience
///
/// 默认不重试 POST；只有 allowlist 里的查询 method 才重试
/// 业务失败仍是 HTTP 200，不会进 retry
/// </summary>
internal static class MsfxApiRegistration
{
    public static IServiceCollection AddMsfxApiClient(this IServiceCollection services)
    {
        services.AddHttpClient(MsfxApiClient.HttpClientName)
            .ConfigureHttpClient(static client => client.Timeout = MsfxApiClient.HttpTimeout)
            .AddResilienceHandler("msfx-transport", static builder =>
            {
                builder.AddRetry(CreateTransportRetryOptions());
                builder.AddTimeout(CreateAttemptTimeoutOptions());
            });

        services.AddSingleton<IMsfxApiClient, MsfxApiClient>();
        return services;
    }

    // Timeout 策略没有 ShouldHandle；非幂等请求给 Infinite，避免被单次 attempt 截断
    internal static HttpTimeoutStrategyOptions CreateAttemptTimeoutOptions()
    {
        var attemptTimeout = MsfxApiClient.AttemptTimeout;
        return new HttpTimeoutStrategyOptions
        {
            Timeout = attemptTimeout,
            TimeoutGenerator = args =>
            {
                var request = args.Context.GetRequestMessage();
                if (request is null
                    || !request.Options.TryGetValue(MsfxApiClient.IdempotentKey, out var idempotent)
                    || !idempotent)
                {
                    return ValueTask.FromResult(Timeout.InfiniteTimeSpan);
                }

                return ValueTask.FromResult(attemptTimeout);
            },
        };
    }

    // 带幂等标记的查询才重试瞬时网络、408、429、5xx；写路径不重试
    internal static HttpRetryStrategyOptions CreateTransportRetryOptions()
    {
        var options = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = MsfxApiClient.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(300),
            BackoffType = DelayBackoffType.Exponential,
        };

        var transient = options.ShouldHandle;
        options.ShouldHandle = args =>
        {
            var request = args.Context.GetRequestMessage();
            if (request is null
                || !request.Options.TryGetValue(MsfxApiClient.IdempotentKey, out var idempotent)
                || !idempotent)
            {
                return PredicateResult.False();
            }

            return transient(args);
        };

        return options;
    }
}
