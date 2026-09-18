using PacToolkits.Desktop.Avalonia;

namespace PacToolkits.Desktop.Tests;

public sealed class StartupCrashTests
{
    [Fact]
    public void Write_creates_log_and_prunes_to_keep_count()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-startup-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (var i = 0; i < StartupCrash.KeepCount + 3; i++)
            {
                var stamp = new DateTimeOffset(2026, 1, 1, 0, 0, i, TimeSpan.Zero);
                var path = StartupCrash.Write(new InvalidOperationException("boom-" + i), dir, stamp);
                Assert.True(File.Exists(path));
                Assert.Contains("boom-" + i, File.ReadAllText(path), StringComparison.Ordinal);
            }

            var files = Directory.GetFiles(dir, StartupCrash.FilePrefix + "*.log");
            Assert.Equal(StartupCrash.KeepCount, files.Length);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 测试清理失败可忽略
            }
        }
    }
}
