using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Hosting;

/// <summary>IAppLogger 接到 ASP.NET ILogger；不写 JSON Lines 文件</summary>
public sealed class HostAppLogger : IAppLogger
{
    private readonly ILoggerFactory _factory;

    public HostAppLogger(ILoggerFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public string LogDirectory => string.Empty;

    public string CurrentLogPath => string.Empty;

    public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null)
        => Log(LogLevel.Debug, module, eventName, message, ex: null, traceId);

    public void Info(string module, string eventName, string message, object? context = null, string? traceId = null)
        => Log(LogLevel.Information, module, eventName, message, ex: null, traceId);

    public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        => Log(LogLevel.Warning, module, eventName, message, ex, traceId);

    public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        => Log(LogLevel.Error, module, eventName, message, ex, traceId);

    public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        => Log(LogLevel.Critical, module, eventName, message, ex, traceId);

    public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    private void Log(LogLevel level, string module, string eventName, string message, Exception? ex, string? traceId)
    {
        var logger = _factory.CreateLogger(module);
        if (ex is null)
        {
            logger.Log(level, "{Event} {Message} traceId={TraceId}", eventName, message, traceId);
        }
        else
        {
            logger.Log(level, ex, "{Event} {Message} traceId={TraceId}", eventName, message, traceId);
        }
    }
}

/// <summary>
/// API 无别名文件：Load 空表；Save 回传入参，由 ClientAliasService 进程内持有
/// </summary>
public sealed class EmptyClientAliasStore : IClientAliasStore
{
    public IReadOnlyDictionary<string, string> Load()
        => new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Save(IEnumerable<KeyValuePair<string, string>> items)
        => items.ToDictionary(static kv => kv.Key, static kv => kv.Value, StringComparer.Ordinal);
}

/// <summary>追溯码规则仅进程内内存（无桌面配置文件）</summary>
public sealed class MemoryTraceCodeRuleStore : ITraceCodeRuleStore
{
    private TraceCodeValidationOptions _current = new();

    public TraceCodeValidationOptions Load() => new()
    {
        RequiredLength = _current.RequiredLength,
        Pattern = _current.Pattern,
    };

    public Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = new TraceCodeValidationOptions
        {
            RequiredLength = options.RequiredLength,
            Pattern = options.Pattern,
        };
        return Task.CompletedTask;
    }
}

/// <summary>更新设置仅进程内内存（无桌面配置文件）</summary>
public sealed class MemoryUpdateSettingsStore : IUpdateSettingsStore
{
    private UpdateOptions _current = new();

    public UpdateOptions Load() => Clone(_current);

    public Task SaveAsync(UpdateOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = Clone(options);
        return Task.CompletedTask;
    }

    private static UpdateOptions Clone(UpdateOptions src) => new()
    {
        AutoCheckOnStartup = src.AutoCheckOnStartup,
        Channel = src.Channel,
        FeedUrl = src.FeedUrl,
        AutoCheckIntervalMinutes = src.AutoCheckIntervalMinutes,
        IgnoredVersion = src.IgnoredVersion,
        SeenInstalledChannel = src.SeenInstalledChannel,
    };
}

/// <summary>API 宿主不发起 MSFX（码上放心）HTTP</summary>
public sealed class UnsupportedMsfxApiClient : IMsfxApiClient
{
    private static MsfxApiCallResult Reject()
        => new(
            Ok: false,
            HttpStatusCode: null,
            Summary: "msfx_client_unavailable",
            BizCode: string.Empty,
            BizMessage: "MSFX client is not configured on the API host",
            RequestId: string.Empty,
            ResponseText: string.Empty,
            RequestTrace: string.Empty);

    public Task<MsfxApiCallResult> ExecuteRawAsync(
        MsfxApiOptions options,
        string method,
        IReadOnlyDictionary<string, string?> bizParams,
        CancellationToken ct)
        => Task.FromResult(Reject());

    public Task<MsfxListUpoutResult> GetYljgListUpoutAsync(
        MsfxApiOptions options,
        MsfxListUpoutRequest request,
        CancellationToken ct)
        => Task.FromResult(new MsfxListUpoutResult(Reject(), Total: 0, Items: []));

    public Task<MsfxListUpoutDetailResult> GetYljgListUpoutDetailAsync(
        MsfxApiOptions options,
        MsfxListUpoutDetailRequest request,
        CancellationToken ct)
        => Task.FromResult(new MsfxListUpoutDetailResult(
            Reject(),
            BillCode: string.Empty,
            DrugItems: [],
            MinimalSalesTraceCodes: [],
            RelationErrorHint: string.Empty));
}
