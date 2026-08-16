using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;
using PacToolkits.Logger;
using LogDirectoryResolver = PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging.LogDirectory;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;

/// <summary>桌面结构化日志；有界 Channel 写盘，不堵 UI</summary>
public sealed class AppLogger : IAppLogger, IDisposable
{
    private const int QueueCapacity = 8192;

    private readonly ILoggingSettingsService _settings;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly TimeProvider _time;
    private readonly JsonLogWriter _writer = new();
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    // SingleReader 后台刷盘；满时 TryWrite 返回 false，据此累计 dropped
    private readonly Channel<PendingLog> _queue = Channel.CreateBounded<PendingLog>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _workerCts = new();
    private readonly Task _worker;
    private long _dropped;

    public AppLogger(
        ILoggingSettingsService settings,
        IReleaseVersionService releaseVersion,
        TimeProvider? timeProvider = null)
    {
        _settings = settings;
        _releaseVersion = releaseVersion;
        _time = timeProvider ?? TimeProvider.System;
        _worker = Task.Run(() => ProcessQueueAsync(_workerCts.Token));
    }

    public string LogDirectory => ResolveLogDirectory(_settings.Current);

    public string CurrentLogPath
    {
        get
        {
            var dir = LogDirectory;
            var now = _time.GetLocalNow();
            var suffix = LogFiles.ResolveSuffix(dir, now, _settings.Current.MaxFileSizeMb);
            return LogFiles.BuildDailyPath(dir, now, suffix);
        }
    }

    public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null)
        => Write(AppLogLevel.Debug, module, eventName, message, null, context, traceId);

    public void Info(string module, string eventName, string message, object? context = null, string? traceId = null)
        => Write(AppLogLevel.Info, module, eventName, message, null, context, traceId);

    public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        => Write(AppLogLevel.Warn, module, eventName, message, ex, context, traceId);

    public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        => Write(AppLogLevel.Error, module, eventName, message, ex, context, traceId);

    public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        => Write(AppLogLevel.Fatal, module, eventName, message, ex, context, traceId);

    public async Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
    {
        if (window <= TimeSpan.Zero)
        {
            window = TimeSpan.FromHours(24);
        }

        var now = _time.GetLocalNow();
        var cutoff = now.Subtract(window);
        var sourceFiles = GetCandidateFiles(cutoff).ToList();

        if (sourceFiles.Count == 0)
        {
            throw new FileNotFoundException("未找到可导出的日志文件");
        }

        var exportDir = Path.Combine(LogDirectory, "exports");
        Directory.CreateDirectory(exportDir);

        var exportPath = Path.Combine(exportDir, $"desktop-recent-{now:yyyyMMdd-HHmmss}.log");

        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(exportPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream);

            foreach (var file in sourceFiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(file))
                {
                    continue;
                }

                await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(source);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (!TryReadTimestamp(line, out var ts))
                    {
                        continue;
                    }

                    if (ts < cutoff)
                    {
                        continue;
                    }

                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
            return exportPath;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private void Write(AppLogLevel level, string module, string eventName, string message, Exception? ex, object? context, string? traceId)
    {
        var settings = _settings.Current;
        if (!settings.Enabled)
        {
            return;
        }

        if (!LogLevel.ShouldWrite(level.ToString(), settings.MinimumLevel))
        {
            return;
        }

        var activity = Activity.Current;
        var resolvedTrace = string.IsNullOrWhiteSpace(traceId)
            ? activity?.TraceId.ToString()
            : traceId.Trim();
        // 调用方给的 traceId（含 LogTrace）优先；没给再用 Activity.Current
        if (string.IsNullOrWhiteSpace(resolvedTrace))
        {
            resolvedTrace = null;
        }

        var record = new JsonLogRecord
        {
            Ts = _time.GetLocalNow(),
            Level = level.ToString(),
            Module = (module ?? string.Empty).Trim(),
            Event = (eventName ?? string.Empty).Trim(),
            Message = (message ?? string.Empty).Trim(),
            TraceId = resolvedTrace,
            SpanId = activity?.SpanId.ToString(),
            Version = _releaseVersion.Current.DesktopVersion,
            Context = context,
            Exception = ex is null
                ? null
                : new JsonLogException
                {
                    Type = ex.GetType().FullName ?? ex.GetType().Name,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace,
                },
        };

        // Fatal 常伴随进程即将终止；异步队列来不及刷盘，同步落盘保证能看见
        if (level == AppLogLevel.Fatal)
        {
            try
            {
                WriteRecordAsync(record, settings).GetAwaiter().GetResult();
            }
            catch (Exception writeEx)
            {
                System.Diagnostics.Debug.WriteLine($"Fatal log write failed: {writeEx}");
            }

            return;
        }

        if (_queue.Writer.TryWrite(new PendingLog(record, settings)))
        {
            return;
        }

        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 100 == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Log queue full or closed; dropped={dropped} capacity={QueueCapacity}");
        }
    }

    private async Task WriteRecordAsync(JsonLogRecord record, LoggingOptions settings)
    {
        await _ioGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _writer.Write(
                ResolveLogDirectory(settings),
                record,
                new JsonLogWriteOptions
                {
                    Enabled = settings.Enabled,
                    MinimumLevel = settings.MinimumLevel,
                    RetentionDays = settings.RetentionDays,
                    MaxFileSizeMb = settings.MaxFileSizeMb,
                    CleanupPatterns = ["*.log"],
                });
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private IEnumerable<string> GetCandidateFiles(DateTimeOffset cutoff)
    {
        var dir = LogDirectory;
        var start = cutoff.Date;
        var end = _time.GetLocalNow().Date;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            foreach (var path in LogFiles.EnumerateDailyPaths(dir, d))
            {
                yield return path;
            }
        }
    }

    private static bool TryReadTimestamp(string line, out DateTimeOffset value)
    {
        value = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ts", out var ts))
            {
                return false;
            }

            var raw = ts.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveLogDirectory(LoggingOptions options)
        => LogDirectoryResolver.Resolve(options.LogDirectory).RuntimeDirectory;

    private readonly record struct PendingLog(JsonLogRecord Record, LoggingOptions Settings);

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var pending in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await WriteRecordAsync(pending.Record, pending.Settings).ConfigureAwait(false);
                }
                catch (Exception writeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Log write failed: {writeEx}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        try
        {
            _queue.Writer.TryComplete();
        }
        catch
        {
        }

        // 限时排空；超时则放弃剩余日志，禁止无期限 Wait（慢盘会卡死 Environment.Exit）
        // worker 未退出时不要 Dispose CTS/ioGate，避免后台仍在写时踩已释放对象
        var exited = false;
        try
        {
            exited = _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        if (!exited)
        {
            try
            {
                _workerCts.Cancel();
            }
            catch
            {
            }

            try
            {
                exited = _worker.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }

        if (exited)
        {
            _workerCts.Dispose();
            _ioGate.Dispose();
            _writer.Dispose();
        }
    }
}
