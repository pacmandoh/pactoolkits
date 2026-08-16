using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PacToolkits.Logger;

/// <summary>
/// 统一 JSON Lines 落盘：级别门控、按日滚动、保留清理
/// </summary>
public sealed class JsonLogWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly object _gate = new();
    private DateTimeOffset _lastCleanupAt = DateTimeOffset.MinValue;
    private string? _openPath;
    private StreamWriter? _writer;

    public void Write(string directory, JsonLogRecord record, JsonLogWriteOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return;
        }

        var level = LogLevel.Canonical(record.Level);
        if (!LogLevel.ShouldWrite(level, options.MinimumLevel))
        {
            return;
        }

        var normalized = new JsonLogRecord
        {
            Ts = record.Ts == default ? DateTimeOffset.Now : record.Ts,
            Level = level,
            Module = (record.Module ?? string.Empty).Trim(),
            Event = (record.Event ?? string.Empty).Trim(),
            Message = (record.Message ?? string.Empty).Trim(),
            TraceId = string.IsNullOrWhiteSpace(record.TraceId) ? null : record.TraceId.Trim(),
            SpanId = string.IsNullOrWhiteSpace(record.SpanId) ? null : record.SpanId.Trim(),
            Version = record.Version ?? string.Empty,
            Context = record.Context,
            Exception = record.Exception,
        };

        var line = JsonSerializer.Serialize(normalized, JsonOptions);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = LogFiles.BuildDailyPath(
                    directory,
                    normalized.Ts,
                    LogFiles.ResolveSuffix(directory, normalized.Ts, options.MaxFileSizeMb));
                EnsureWriter(path);
                _writer!.WriteLine(line);
                _writer.Flush();

                var now = DateTimeOffset.Now;
                if (options.CleanupPatterns.Count > 0
                    && now - _lastCleanupAt > options.CleanupInterval)
                {
                    CloseWriter();
                    LogFiles.CleanupExpired(directory, options.CleanupPatterns, options.RetentionDays);
                    _lastCleanupAt = now;
                }
            }
            catch
            {
                // 日志失败不得拖垮调用方
                CloseWriter();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseWriter();
        }
    }

    private void EnsureWriter(string path)
    {
        if (_writer is not null && string.Equals(_openPath, path, StringComparison.Ordinal))
        {
            return;
        }

        CloseWriter();
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _openPath = path;
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        _writer = null;
        _openPath = null;
    }
}
