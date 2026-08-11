using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiContractGateTests
{
    [Fact]
    public async Task Compatible_contract_allows_business_send()
    {
        var apiHits = 0;
        await using var sp = BuildProvider(
            min: "1.0.0",
            max: "1.0.0",
            onApi: req =>
            {
                Interlocked.Increment(ref apiHits);
                if (req.RequestUri!.AbsolutePath.Contains("/v1/system/info", StringComparison.Ordinal))
                {
                    return Info("1.0.0");
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"),
                };
            });

        var api = sp.GetRequiredService<PacApiClient>();
        using var response = await api.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, api.Resolve("/v1/business")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(apiHits >= 2);
    }

    [Fact]
    public async Task Incompatible_contract_blocks_business_send()
    {
        await using var sp = BuildProvider(
            min: "1.0.0",
            max: "1.0.0",
            onApi: req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/v1/system/info", StringComparison.Ordinal))
                {
                    return Info("2.0.0");
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var api = sp.GetRequiredService<PacApiClient>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, api.Resolve("/v1/business")),
                TestContext.Current.CancellationToken));

        Assert.Contains("incompatible", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Concurrent_incompatible_check_publishes_once()
    {
        var infoHits = 0;
        await using var sp = BuildProvider(
            min: "1.0.0",
            max: "1.0.0",
            onApi: req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/v1/system/info", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref infoHits);
                    return Info("2.0.0");
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var gate = sp.GetRequiredService<IPacApiContractGate>();
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await gate.EnsureCompatibleAsync(TestContext.Current.CancellationToken);
                    return false;
                }
                catch (InvalidOperationException ex)
                    when (ex.Message.Contains("incompatible", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }))
            .ToArray();

        var blocked = await Task.WhenAll(tasks);
        Assert.All(blocked, Assert.True);
        Assert.Equal(1, infoHits);
    }

    private static ServiceProvider BuildProvider(
        string min,
        string max,
        Func<HttpRequestMessage, HttpResponseMessage> onApi)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PacApi:BaseUrl"] = "http://127.0.0.1:9",
                ["PacApi:ApiKey"] = "test-key",
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IAppLogger, NullLogger>();
        services.AddSingleton<IReleaseVersionService>(new StubVersions(min, max));
        services.AddSingleton(TimeProvider.System);
        services.AddPacApiClient(config);
        services.AddSingleton<IPacApiContractGate, PacApiContractGate>();

        services.AddHttpClient(PacApiClient.TokenClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FixedHandler(
                HttpStatusCode.OK,
                """{"accessToken":"tok","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}"""));

        services.AddHttpClient(PacApiClient.ApiClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new ScriptHandler(onApi));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Info(string contractVersion)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"product":"PacToolkits.Api","apiVersion":"1.0.0","contractVersion":"{{contractVersion}}","utc":"2026-01-01T00:00:00Z"}""",
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class StubVersions(string min, string max) : IReleaseVersionService
    {
        public ReleaseVersionInfo Current { get; } = new(
            ProductVersion: "1.0.0",
            DesktopVersion: "1.0.0",
            AgentsVersion: "1.0.0",
            DbSchemaVersion: "1.0.0",
            BuildChannel: "beta",
            BuildDate: "2026-01-01",
            DesktopMinDbSchema: "1.0.0",
            DesktopMaxDbSchema: "1.0.0",
            MinApiContract: min,
            MaxApiContract: max);
    }

    private sealed class FixedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ScriptHandler(Func<HttpRequestMessage, HttpResponseMessage> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(next(request));
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => string.Empty;
        public string CurrentLogPath => string.Empty;

        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Warn(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Error(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Fatal(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
