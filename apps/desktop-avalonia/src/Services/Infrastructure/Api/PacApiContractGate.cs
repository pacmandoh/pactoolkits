using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>设置热应用后作废上次协议检查结果</summary>
    void Reset();
}

/// <summary>
/// 第一次业务请求前：用清单 minApiContract/maxApiContract 对照 API 的 contractVersion
///
/// 不兼容直接抛错，别靠重试硬撑
/// </summary>
public sealed class PacApiContractGate : IPacApiContractGate
{
    private readonly PacApiClient _api;
    private readonly IReleaseVersionService _versions;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // 整份结果一次发布；null = 尚未检查
    private Outcome? _outcome;

    private sealed record Outcome(string? BlockReason);

    public PacApiContractGate(
        PacApiClient api,
        IReleaseVersionService versions,
        IAppLogger logger)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Reset()
        => Volatile.Write(ref _outcome, null);

    public async Task EnsureCompatibleAsync(CancellationToken ct = default)
    {
        if (!_api.IsConfigured)
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
            _logger.Error(
                "PacApi",
                "contract.range_missing",
                "PacApi contract range missing from Desktop ReleaseManifest (minApiContract/maxApiContract)");
            return "客户端缺少 PacApi 服务协议版本范围，请更新客户端";
        }

        var info = await _api.GetAvailabilityJsonAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/system/info"));
                    req.Options.Set(PacApiClient.SkipContractGateKey, true);
                    return req;
                },
                PacJsonContext.Default.PacApiSystemInfo,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("PacApi 服务返回空协议信息");

        return ClassifyBlockReason(info.ContractVersion, min, max, _logger);
    }

    /// <summary>协议区间对照；兼容返回 null，否则返回阻断文案</summary>
    internal static string? ClassifyBlockReason(
        string? contractVersion,
        string min,
        string max,
        IAppLogger? logger = null)
    {
        var match = SemVerRange.Classify(contractVersion, min, max, allowPrerelease: false);
        if (match.IsCompatible)
        {
            logger?.Info(
                "PacApi",
                "contract.ok",
                $"API contract {contractVersion} within [{min}, {max}]");
            return null;
        }

        var incompatible =
            $"与 PacApi 服务协议版本不兼容（服务端 {contractVersion}，客户端要求 {min}–{max}）";
        logger?.Error(
            "PacApi",
            "contract.incompatible",
            $"API contract {contractVersion} incompatible with Desktop range [{min}, {max}] ({match.Status})");
        return incompatible;
    }
}
