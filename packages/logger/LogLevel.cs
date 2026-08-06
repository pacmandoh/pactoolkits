namespace PacToolkits.Logger;

/// <summary>
/// 日志级别规范化与序比较；写入值为 Debug / Info / Warn / Error / Fatal
/// </summary>
public static class LogLevel
{
    public const string Debug = "Debug";
    public const string Info = "Info";
    public const string Warn = "Warn";
    public const string Error = "Error";
    public const string Fatal = "Fatal";

    public static string Canonical(string? raw, string fallback = Error)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "debug" => Debug,
            "info" => Info,
            "warn" or "warning" => Warn,
            "error" or "err" => Error,
            "fatal" => Fatal,
            _ => fallback,
        };
    }

    public static int Rank(string? level)
        => Canonical(level) switch
        {
            Debug => 0,
            Info => 1,
            Warn => 2,
            Error => 3,
            Fatal => 4,
            _ => 3,
        };

    public static bool ShouldWrite(string? level, string? minimumLevel)
        => Rank(level) >= Rank(minimumLevel);
}
