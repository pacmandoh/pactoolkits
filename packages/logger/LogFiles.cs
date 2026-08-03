using System.Globalization;
using System.Text.RegularExpressions;

namespace PacToolkits.Logger;

/// <summary>
/// 按日日志文件名、单文件滚动与保留清理。
/// 单文件达到 MaxFileSizeMb 后递增 N（YYYY-MM-DD.N.log），不设固定上界，以免封顶分片无限增长。
/// </summary>
public static partial class LogFiles
{
    /// <summary>异常保护：同日分片过多时停止探测（正常负载不会触达）</summary>
    public const int MaxShardsPerDay = 100_000;

    public static string BuildDailyPath(string directory, DateTimeOffset at, int suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(suffix);

        var baseName = $"{at:yyyy-MM-dd}";
        var name = suffix <= 0 ? $"{baseName}.log" : $"{baseName}.{suffix}.log";
        return Path.Combine(directory, name);
    }

    public static int ResolveSuffix(string directory, DateTimeOffset at, int maxFileSizeMb)
    {
        var maxBytes = Math.Max(1, maxFileSizeMb) * 1024L * 1024L;

        for (var i = 0; i < MaxShardsPerDay; i++)
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

        // 不应触达；返回下一序号，仍避免向已满的最后一片追加
        return MaxShardsPerDay;
    }

    /// <summary>列出某日存在的日志分片（含 N≥10），供阅读/导出扫描</summary>
    public static IEnumerable<string> EnumerateDailyPaths(string directory, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        var day = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var path in Directory.EnumerateFiles(directory, $"{day}*.log", SearchOption.TopDirectoryOnly))
        {
            if (TryGetDailySuffix(Path.GetFileName(path), day, out _))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// 解析按日文件名上的分片序号：yyyy-MM-dd.log → 0，yyyy-MM-dd.N.log → N
    /// </summary>
    public static bool TryGetDailySuffix(string? fileName, string dayStamp, out int suffix)
    {
        suffix = 0;
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(dayStamp))
        {
            return false;
        }

        if (string.Equals(fileName, $"{dayStamp}.log", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var m = DailySuffixRegex().Match(fileName);
        if (!m.Success || !string.Equals(m.Groups["date"].Value, dayStamp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            return false;
        }

        suffix = n;
        return true;
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

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2})\.(?<n>\d+)\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DailySuffixRegex();
}
