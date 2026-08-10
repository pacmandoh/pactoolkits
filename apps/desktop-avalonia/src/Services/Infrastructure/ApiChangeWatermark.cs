#if false
// 暂缓接入：迁到 API 变更流前不编译；注册 DI 后再启用
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>
/// 经 API 的变更水位：SSE 只作唤醒；version 以 GET watermarks 为准
///
/// 不直连 Pg NOTIFY；SSE 断了只重连流，不把整站判为断开
/// `ready`/`change` 均补查 watermark；首见 topic 仅 `change` 可刷页
/// 与 Infrastructure 的 ChangeWatermarkService 互斥，二选一注册为 IChangeWatermarkService
/// </summary>
public sealed class ApiChangeWatermark : IChangeWatermarkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly PacApiClient _api;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(20);
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _notifyCoalesceWindow = TimeSpan.FromMilliseconds(120);

    // 脉冲载荷：emitOnBootstrap（change=true，ready=false）
    private readonly Channel<bool> _pulses = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private Task? _streamLoop;
    private Task? _pollLoop;
    private Task? _dispatchLoop;

    public event Action<string>? TopicChanged;

    public ApiChangeWatermark(PacApiClient api, IAppLogger logger)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        if (!_api.IsConfigured)
        {
            _logger.Error(
                "ChangeStream",
                "start.unconfigured",
                "Change API baseUrl/apiKey required; change stream not started");
            return;
        }

        _dispatchLoop ??= Task.Run(() => DispatchLoopAsync(_cts.Token));
        _pollLoop ??= Task.Run(() => PollLoopAsync(_cts.Token));
        _streamLoop ??= Task.Run(() => StreamLoopAsync(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (Exception ex)
        {
            _logger.Warn("ChangeStream", "dispose.cancel_fail", "Failed to cancel change stream", ex);
        }

        try { _pulses.Writer.TryComplete(); }
        catch (Exception ex)
        {
            _logger.Warn("ChangeStream", "dispose.channel_fail", "Failed to close change stream channel", ex);
        }

        _cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshFromWatermarkAsync(emitOnBootstrap: false, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn("ChangeStream", "watermark.poll.fail", "Watermark poll failed", ex);
            }

            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task StreamLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await _api.SendSseAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/changes/stream")),
                    ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn(
                        "ChangeStream",
                        "sse.http_fail",
                        $"SSE HTTP {(int)response.StatusCode}");
                    await Task.Delay(_reconnectDelay, ct).ConfigureAwait(false);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                _logger.Info("ChangeStream", "sse.up", "Change SSE connected");

                string? eventName = null;
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        eventName = null;
                        continue;
                    }

                    if (line.StartsWith("event:", StringComparison.Ordinal))
                    {
                        eventName = line["event:".Length..].Trim();
                        continue;
                    }

                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.Equals(eventName, "change", StringComparison.OrdinalIgnoreCase))
                    {
                        _pulses.Writer.TryWrite(true);
                    }
                    else if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        // ready：立刻 GET watermark；首见 topic 不刷页（与冷启动 poll 一致）
                        _pulses.Writer.TryWrite(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn("ChangeStream", "sse.retry", "SSE disconnected; reconnecting", ex);
                try
                {
                    await Task.Delay(_reconnectDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool emitOnBootstrap;
            try
            {
                emitOnBootstrap = await _pulses.Reader.ReadAsync(ct).ConfigureAwait(false);
                await Task.Delay(_notifyCoalesceWindow, ct).ConfigureAwait(false);
                while (_pulses.Reader.TryRead(out var flag))
                {
                    emitOnBootstrap |= flag;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RefreshFromWatermarkAsync(emitOnBootstrap, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn("ChangeStream", "watermark.dispatch.fail", "Watermark dispatch failed", ex);
            }
        }
    }

    private async Task RefreshFromWatermarkAsync(bool emitOnBootstrap, CancellationToken ct)
    {
        var rows = await FetchWatermarksAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            var shouldEmit = false;
            lock (_gate)
            {
                if (_versions.TryGetValue(row.Topic, out var prev))
                {
                    if (row.Version > prev)
                    {
                        _versions[row.Topic] = row.Version;
                        shouldEmit = true;
                    }
                }
                else
                {
                    _versions[row.Topic] = row.Version;
                    shouldEmit = emitOnBootstrap;
                }
            }

            if (shouldEmit)
            {
                TopicChanged?.Invoke(row.Topic);
            }
        }
    }

    private async Task<IReadOnlyList<ChangeWatermarkItem>> FetchWatermarksAsync(CancellationToken ct)
    {
        using var response = await _api.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/changes/watermarks")),
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var body = await JsonSerializer.DeserializeAsync<WatermarksResponse>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        return body?.Items ?? Array.Empty<ChangeWatermarkItem>();
    }

    private sealed record WatermarksResponse(IReadOnlyList<ChangeWatermarkItem> Items);
}
#endif
