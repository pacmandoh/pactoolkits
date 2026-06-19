using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public sealed class AppLogger : IAppLogger, IDisposable
{
    private readonly ILoggingSettingsService _settings;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly Channel<PendingLog> _queue = Channel.CreateUnbounded<PendingLog>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _workerCts = new();
    private readonly Task _worker;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private DateTimeOffset _lastCleanupAt = DateTimeOffset.MinValue;

    public AppLogger(ILoggingSettingsService settings, IReleaseVersionService releaseVersion)
    {
        _settings = settings;
        _releaseVersion = releaseVersion;
        _worker = Task.Run(() => ProcessQueueAsync(_workerCts.Token));
    }

    public string LogDirectory => ResolveLogDirectory(_settings.Current);
    public string CurrentLogPath => BuildLogPath(DateTimeOffset.Now, 0);

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

        var cutoff = DateTimeOffset.Now.Subtract(window);
        var sourceFiles = GetCandidateFiles(cutoff).ToList();

        if (sourceFiles.Count == 0)
        {
            throw new FileNotFoundException("未找到可导出的日志文件");
        }

        var exportDir = Path.Combine(LogDirectory, "exports");
        Directory.CreateDirectory(exportDir);

        var exportPath = Path.Combine(exportDir, $"desktop-recent-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");

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

        if (level < ParseLevel(settings.MinimumLevel))
        {
            return;
        }

        var record = new AppLogRecord
        {
            Ts = DateTimeOffset.Now,
            Level = level.ToString(),
            Module = (module ?? string.Empty).Trim(),
            Event = (eventName ?? string.Empty).Trim(),
            Message = (message ?? string.Empty).Trim(),
            TraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim(),
            Version = _releaseVersion.Current.DesktopVersion,
            Context = context,
            Exception = ex is null ? null : new AppExceptionRecord
            {
                Type = ex.GetType().FullName ?? ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            }
        };

        if (!_queue.Writer.TryWrite(new PendingLog(record, settings)))
        {
            System.Diagnostics.Debug.WriteLine("Log queue write failed: channel is closed");
        }
    }

    private async Task WriteRecordAsync(AppLogRecord record, LoggingOptions settings)
    {
        var payload = JsonSerializer.Serialize(record, _jsonOptions);

        await _ioGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var path = BuildLogPath(record.Ts, ResolveSuffix(record.Ts, settings.MaxFileSizeMb));
            await File.AppendAllTextAsync(path, payload + Environment.NewLine).ConfigureAwait(false);

            if (DateTimeOffset.Now - _lastCleanupAt > TimeSpan.FromHours(6))
            {
                CleanupExpiredFiles(settings.RetentionDays);
                _lastCleanupAt = DateTimeOffset.Now;
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private int ResolveSuffix(DateTimeOffset now, int maxFileSizeMb)
    {
        var maxBytes = maxFileSizeMb * 1024L * 1024L;

        for (var i = 0; i < 10; i++)
        {
            var path = BuildLogPath(now, i);
            if (!File.Exists(path))
            {
                return i;
            }

            var size = new FileInfo(path).Length;
            if (size < maxBytes)
            {
                return i;
            }
        }

        return 9;
    }

    private void CleanupExpiredFiles(int retentionDays)
    {
        var thresholdUtc = DateTime.UtcNow.Date.AddDays(-retentionDays);

        foreach (var pattern in new[] { "desktop-*.log", "ui-*.log" })
        {
            foreach (var file in Directory.EnumerateFiles(LogDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < thresholdUtc)
                    {
                        info.Delete();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Log cleanup failed: {ex}");
                }
            }
        }
    }

    private IEnumerable<string> GetCandidateFiles(DateTimeOffset cutoff)
    {
        var start = cutoff.Date;
        var end = DateTimeOffset.Now.Date;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            for (var suffix = 0; suffix < 10; suffix++)
            {
                var path = BuildLogPath(d, suffix);
                if (File.Exists(path))
                {
                    yield return path;
                }
            }
        }
    }

    private string BuildLogPath(DateTime at, int suffix)
        => BuildLogPath(new DateTimeOffset(at), suffix);

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

    private string BuildLogPath(DateTimeOffset at, int suffix)
    {
        var baseName = $"desktop-{at:yyyy-MM-dd}";
        var name = suffix == 0 ? $"{baseName}.log" : $"{baseName}.{suffix}.log";
        return Path.Combine(LogDirectory, name);
    }

    private static AppLogLevel ParseLevel(string? level)
    {
        var raw = (level ?? string.Empty).Trim().ToLowerInvariant();
        return raw switch
        {
            "debug" => AppLogLevel.Debug,
            "info" => AppLogLevel.Info,
            "warn" => AppLogLevel.Warn,
            "error" => AppLogLevel.Error,
            "fatal" => AppLogLevel.Fatal,
            _ => AppLogLevel.Info
        };
    }

    private static string ResolveLogDirectory(LoggingOptions options)
        => DesktopLogDirectoryResolver.Resolve(options.LogDirectory).RuntimeDirectory;

    private sealed class AppLogRecord
    {
        public DateTimeOffset Ts { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? TraceId { get; set; }
        public string Version { get; set; } = string.Empty;
        public object? Context { get; set; }
        public AppExceptionRecord? Exception { get; set; }
    }

    private sealed class AppExceptionRecord
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
    }

    private readonly record struct PendingLog(AppLogRecord Record, LoggingOptions Settings);

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

        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        try
        {
            _workerCts.Cancel();
        }
        catch
        {
        }

        _workerCts.Dispose();
        _ioGate.Dispose();
    }
}
