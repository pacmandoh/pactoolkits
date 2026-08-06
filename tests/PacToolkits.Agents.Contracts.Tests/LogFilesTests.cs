using PacToolkits.Logger;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class LogFilesTests
{
    [Fact]
    public void BuildDailyPath_uses_suffix_zero_without_dot()
    {
        var path = LogFiles.BuildDailyPath("/tmp/logs", new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), 0);
        Assert.Equal(Path.Combine("/tmp/logs", "2026-08-02.log"), path);
    }

    [Fact]
    public void BuildDailyPath_adds_suffix_segment()
    {
        var path = LogFiles.BuildDailyPath("/tmp/logs", new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), 2);
        Assert.Equal(Path.Combine("/tmp/logs", "2026-08-02.2.log"), path);
    }

    [Fact]
    public void ResolveSuffix_prefers_existing_file_under_limit()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-logfiles-").FullName;
        try
        {
            var day = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            var path0 = LogFiles.BuildDailyPath(dir, day, 0);
            File.WriteAllText(path0, "x");

            Assert.Equal(0, LogFiles.ResolveSuffix(dir, day, maxFileSizeMb: 20));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSuffix_rolls_when_file_at_limit()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-logfiles-").FullName;
        try
        {
            var day = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            var path0 = LogFiles.BuildDailyPath(dir, day, 0);
            File.WriteAllBytes(path0, new byte[1024 * 1024]);

            Assert.Equal(1, LogFiles.ResolveSuffix(dir, day, maxFileSizeMb: 1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSuffix_continues_past_former_cap_when_shards_full()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-logfiles-").FullName;
        try
        {
            var day = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i <= 9; i++)
            {
                File.WriteAllBytes(LogFiles.BuildDailyPath(dir, day, i), chunk);
            }

            Assert.Equal(10, LogFiles.ResolveSuffix(dir, day, maxFileSizeMb: 1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDailyPaths_includes_high_suffixes()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-logfiles-").FullName;
        try
        {
            var day = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            File.WriteAllText(LogFiles.BuildDailyPath(dir, day, 0), "a");
            File.WriteAllText(LogFiles.BuildDailyPath(dir, day, 10), "b");
            File.WriteAllText(Path.Combine(dir, "2026-08-02-extra.log"), "x");
            File.WriteAllText(Path.Combine(dir, "2026-08-03.log"), "y");

            var found = LogFiles.EnumerateDailyPaths(dir, day)
                .Select(p => Path.GetFileName(p)!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "2026-08-02.10.log", "2026-08-02.log" }, found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanupExpired_deletes_old_files()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-logfiles-").FullName;
        try
        {
            var keep = Path.Combine(dir, "2026-08-02.log");
            var drop = Path.Combine(dir, "2026-07-01.log");
            File.WriteAllText(keep, "k");
            File.WriteAllText(drop, "d");
            File.SetLastWriteTimeUtc(drop, DateTime.UtcNow.Date.AddDays(-30));

            LogFiles.CleanupExpired(dir, ["*.log"], retentionDays: 14);

            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(drop));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
