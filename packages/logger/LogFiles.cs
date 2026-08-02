namespace PacToolkits.Logger;

/// <summary>
/// 按日日志文件名、单文件滚动与保留清理
/// </summary>
public static class LogFiles
{
    public const int MaxSuffix = 9;

    public static string BuildDailyPath(string directory, DateTimeOffset at, int suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var baseName = $"{at:yyyy-MM-dd}";
        var name = suffix <= 0 ? $"{baseName}.log" : $"{baseName}.{suffix}.log";
        return Path.Combine(directory, name);
    }

    public static int ResolveSuffix(string directory, DateTimeOffset at, int maxFileSizeMb)
    {
        var maxBytes = Math.Max(1, maxFileSizeMb) * 1024L * 1024L;

        for (var i = 0; i <= MaxSuffix; i++)
        {
            var path = BuildDailyPath(directory, at, i);
            if (!File.Exists(path))
            {
                return i;
            }

            if (new FileInfo(path).Length < maxBytes)
            {
                return i;
            }
        }

        return MaxSuffix;
    }

    public static void CleanupExpired(string directory, IEnumerable<string> patterns, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var days = Math.Clamp(retentionDays <= 0 ? 14 : retentionDays, 1, 180);
        var thresholdUtc = DateTime.UtcNow.Date.AddDays(-days);

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < thresholdUtc)
                    {
                        info.Delete();
                    }
                }
                catch
                {
                    // 清理失败不得影响写日志主路径
                }
            }
        }
    }
}
