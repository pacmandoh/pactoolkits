using System.Text.Json;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Logger;

namespace PacToolkits.Agents.Host;

/// <summary>
/// Host JSON Lines 文件日志；走 PacToolkits.Logger 统一落盘
/// 门控策略读取 Desktop 配置 Logging（按文件变更缓存）
/// </summary>
internal static class HostLog
{
    // mtime 不可用时的占位：表示已加载且仅在 Init/force 时重读
    private static readonly DateTime UnknownWriteUtc = DateTime.MaxValue;

    private static readonly JsonLogWriter Writer = new();
    private static readonly object Gate = new();
    private static readonly JsonLogWriteOptions Defaults = CreateDefaultOptions();

    private static string? _directory;
    private static string? _desktopConfigPath;
    private static string? _configuredRoot;
    private static JsonLogWriteOptions _options = Defaults;
    private static DateTime _configWriteUtc = DateTime.MinValue;

    public static void Init(string? desktopConfigPath = null)
    {
        if (!string.IsNullOrWhiteSpace(desktopConfigPath))
        {
            _desktopConfigPath = desktopConfigPath.Trim();
        }

        lock (Gate)
        {
            ReloadOptionsUnlocked(force: true);
            ApplyDirectoryUnlocked();
        }
    }

    public static void Debug(string eventName, string message, object? context = null)
        => Write(LogLevel.Debug, eventName, message, context);

    public static void Info(string eventName, string message, object? context = null)
        => Write(LogLevel.Info, eventName, message, context);

    public static void Warn(string eventName, string message, object? context = null)
        => Write(LogLevel.Warn, eventName, message, context);

    public static void Error(string eventName, string message, object? context = null)
        => Write(LogLevel.Error, eventName, message, context);

    public static void Fatal(string eventName, string message, object? context = null)
        => Write(LogLevel.Fatal, eventName, message, context);

    private static void Write(string level, string eventName, string message, object? context)
    {
        string dir;
        JsonLogWriteOptions options;
        lock (Gate)
        {
            ReloadOptionsUnlocked(force: false);
            ApplyDirectoryUnlocked();
            dir = _directory!;
            options = _options;
        }

        Writer.Write(
            dir,
            new JsonLogRecord
            {
                Ts = DateTimeOffset.Now,
                Level = level,
                Module = "Host",
                Event = eventName.Trim(),
                Message = message,
                Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? string.Empty,
                Context = context,
            },
            options);
    }

    private static void ApplyDirectoryUnlocked()
    {
        var next = AgentsLogPaths.HostDir(_configuredRoot);
        if (string.Equals(_directory, next, StringComparison.Ordinal))
        {
            return;
        }

        _directory = next;
        Directory.CreateDirectory(next);
    }

    // Desktop 配置解析仅 Host 需要；Desktop 走 DI
    private static void ReloadOptionsUnlocked(bool force)
    {
        var path = _desktopConfigPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _options = Defaults;
            _configuredRoot = null;
            _configWriteUtc = DateTime.MinValue;
            return;
        }

        DateTime writeUtc;
        try
        {
            writeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            writeUtc = DateTime.MinValue;
        }

        if (!force)
        {
            // mtime 正常：仅在变更时重读
            if (writeUtc != DateTime.MinValue && writeUtc == _configWriteUtc)
            {
                return;
            }

            // mtime 不可用：已有缓存则沿用，避免每条日志重读整个 Desktop 配置
            if (writeUtc == DateTime.MinValue && _configWriteUtc != DateTime.MinValue)
            {
                return;
            }
        }

        try
        {
            var parsed = ParseDesktopLogging(File.ReadAllText(path));
            _options = parsed.Options;
            _configuredRoot = parsed.ConfiguredRoot;
            _configWriteUtc = writeUtc == DateTime.MinValue ? UnknownWriteUtc : writeUtc;
        }
        catch
        {
            _options = Defaults;
            _configuredRoot = null;
            _configWriteUtc = DateTime.MinValue;
        }
    }

    private static (JsonLogWriteOptions Options, string? ConfiguredRoot) ParseDesktopLogging(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Logging", out var logging))
        {
            return (Defaults, null);
        }

        var enabled = true;
        if (logging.TryGetProperty("Enabled", out var enabledNode)
            && (enabledNode.ValueKind is JsonValueKind.True or JsonValueKind.False))
        {
            enabled = enabledNode.GetBoolean();
        }

        var minimumLevel = LogLevel.Error;
        if (logging.TryGetProperty("MinimumLevel", out var levelNode)
            && levelNode.ValueKind == JsonValueKind.String)
        {
            minimumLevel = LogLevel.Canonical(levelNode.GetString(), LogLevel.Error);
        }

        var retentionDays = 14;
        if (logging.TryGetProperty("RetentionDays", out var retentionNode)
            && retentionNode.TryGetInt32(out var retentionRaw))
        {
            retentionDays = Math.Clamp(retentionRaw <= 0 ? 14 : retentionRaw, 1, 180);
        }

        var maxFileSizeMb = 20;
        if (logging.TryGetProperty("MaxFileSizeMb", out var sizeNode)
            && sizeNode.TryGetInt32(out var sizeRaw))
        {
            maxFileSizeMb = Math.Clamp(sizeRaw <= 0 ? 20 : sizeRaw, 1, 200);
        }

        string? configuredRoot = null;
        if (logging.TryGetProperty("LogDirectory", out var dirNode)
            && dirNode.ValueKind == JsonValueKind.String)
        {
            var raw = dirNode.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                configuredRoot = raw.Trim();
            }
        }

        return (
            new JsonLogWriteOptions
            {
                Enabled = enabled,
                MinimumLevel = minimumLevel,
                RetentionDays = retentionDays,
                MaxFileSizeMb = maxFileSizeMb,
                CleanupPatterns = Defaults.CleanupPatterns,
            },
            configuredRoot);
    }

    private static JsonLogWriteOptions CreateDefaultOptions()
        => new()
        {
            Enabled = true,
            MinimumLevel = LogLevel.Error,
            RetentionDays = 14,
            MaxFileSizeMb = 20,
            CleanupPatterns = ["*.log"],
        };
}
