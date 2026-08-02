using System.Text.Json;
using PacToolkits.Logger;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class JsonLogWriterTests
{
    [Fact]
    public void Write_emits_camelCase_json_line()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-jsonlog-").FullName;
        try
        {
            var writer = new JsonLogWriter();
            var day = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            writer.Write(
                dir,
                new JsonLogRecord
                {
                    Ts = day,
                    Level = "info",
                    Module = "Test",
                    Event = "unit.write",
                    Message = "hello",
                    Version = "1.0.0",
                },
                new JsonLogWriteOptions
                {
                    Enabled = true,
                    MinimumLevel = LogLevel.Debug,
                    CleanupPatterns = ["*.log"],
                });

            var path = LogFiles.BuildDailyPath(dir, day, 0);
            Assert.True(File.Exists(path));
            var line = File.ReadAllText(path).Trim();
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            Assert.Equal("Info", root.GetProperty("level").GetString());
            Assert.Equal("Test", root.GetProperty("module").GetString());
            Assert.Equal("unit.write", root.GetProperty("event").GetString());
            Assert.Equal("hello", root.GetProperty("message").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_respects_minimum_level_gate()
    {
        var dir = Directory.CreateTempSubdirectory("pactoolkits-jsonlog-").FullName;
        try
        {
            var writer = new JsonLogWriter();
            writer.Write(
                dir,
                new JsonLogRecord
                {
                    Level = LogLevel.Info,
                    Module = "Test",
                    Event = "skip",
                    Message = "nope",
                },
                new JsonLogWriteOptions
                {
                    Enabled = true,
                    MinimumLevel = LogLevel.Error,
                });

            Assert.Empty(Directory.EnumerateFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
