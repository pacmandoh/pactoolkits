using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Serialization;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>核对 Desktop 清单与 API 的 contractVersion 是否兼容</summary>
public interface IPacApiContractGate
{
    /// <summary>未配置则跳过；已配置则拉 /v1/system/info 对照清单区间</summary>
    Task EnsureCompatibleAsync(CancellationToken ct = default);
}

/// <summary>
/// 第一次业务请求前：用清单 minApiContract/maxApiContract 对照 API 的 contractVersion
///
/// 不兼容直接抛错，别靠重试硬撑
/// </summary>
public sealed class PacApiContractGate : IPacApiContractGate
{
    private readonly PacApiClient _api;
    // 与 PacApiClient 一样用启动快照；改配置后需重启
    private readonly PacApiOptions _options;
    private readonly IReleaseVersionService _versions;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // 整份结果一次发布；null = 尚未检查
    private Outcome? _outcome;

    private sealed record Outcome(string? BlockReason);

    public PacApiContractGate(
        PacApiClient api,
        IOptions<PacApiOptions> options,
        IReleaseVersionService versions,
        IAppLogger logger)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new PacApiOptions();
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureCompatibleAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            return;
        }

        var existing = Volatile.Read(ref _outcome);
        if (existing is not null)
        {
            ThrowIfBlocked(existing);
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            existing = Volatile.Read(ref _outcome);
            if (existing is not null)
            {
                ThrowIfBlocked(existing);
                return;
            }

            var blockReason = await CheckOnceAsync(ct).ConfigureAwait(false);
            var published = new Outcome(blockReason);
            Volatile.Write(ref _outcome, published);
            ThrowIfBlocked(published);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ThrowIfBlocked(Outcome outcome)
    {
        if (outcome.BlockReason is not null)
        {
            throw new InvalidOperationException(outcome.BlockReason);
        }
    }

    private async Task<string?> CheckOnceAsync(CancellationToken ct)
    {
        var ver = _versions.Current;
        var min = ver.MinApiContract;
        var max = ver.MaxApiContract;
        if (string.IsNullOrWhiteSpace(min)
            || string.IsNullOrWhiteSpace(max)
            || string.Equals(min, "unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(max, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            var missing = "PacApi contract range missing from Desktop ReleaseManifest (minApiContract/maxApiContract)";
            _logger.Error("PacApi", "contract.range_missing", missing);
            return missing;
        }

        var info = await _api.GetJsonAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/system/info"));
                    req.Options.Set(PacApiClient.SkipContractGateKey, true);
                    return req;
                },
                PacJsonContext.Default.PacApiSystemInfo,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty /v1/system/info response");

        var match = SemVerRange.Classify(info.ContractVersion, min, max, allowPrerelease: false);
        if (match.IsCompatible)
        {
            _logger.Info(
                "PacApi",
                "contract.ok",
                $"API contract {info.ContractVersion} within [{min}, {max}]");
            return null;
        }

        var incompatible =
            $"API contract {info.ContractVersion} incompatible with Desktop range [{min}, {max}] ({match.Status})";
        _logger.Error("PacApi", "contract.incompatible", incompatible);
        return incompatible;
    }
}
