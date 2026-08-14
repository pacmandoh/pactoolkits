using System.Text.Json;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;

namespace PacToolkits.Desktop.Tests;

public sealed class AppLoggerActivityTests
{
    [Fact]
    public async Task Write_includes_activity_traceId_and_spanId()
    {
        PacActivities.EnsureListening();
        var dir = Directory.CreateTempSubdirectory("pactoolkits-applog-act-").FullName;
        try
        {
            var logger = new AppLogger(
                new StubLoggingSettings(dir),
                new StubReleaseVersion());

            string? expectedTrace;
            string? expectedSpan;
            try
            {
                using (var activity = PacActivities.Desktop.StartActivity("test.span"))
                {
                    Assert.NotNull(activity);
                    expectedTrace = activity!.TraceId.ToString();
                    expectedSpan = activity.SpanId.ToString();
                    logger.Info("Test", "activity.write", "hello from activity");
                }
            }
            finally
            {
                logger.Dispose();
            }

            var files = Directory.GetFiles(dir, "*.log", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            var line = (await File.ReadAllLinesAsync(files[0], TestContext.Current.CancellationToken))
                .Last(static l => l.Length > 0);
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            Assert.Equal(expectedTrace, root.GetProperty("traceId").GetString());
            Assert.Equal(expectedSpan, root.GetProperty("spanId").GetString());
            Assert.Equal("activity.write", root.GetProperty("event").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class StubLoggingSettings(string dir) : ILoggingSettingsService
    {
        public LoggingOptions Current { get; } = new()
        {
            Enabled = true,
            MinimumLevel = "Debug",
            RetentionDays = 7,
            MaxFileSizeMb = 20,
            LogDirectory = dir,
        };

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task SaveAsync(LoggingOptions options, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Apply(LoggingOptions options)
        {
        }

        public void Reload()
        {
        }
    }

    private sealed class StubReleaseVersion : IReleaseVersionService
    {
        public ReleaseVersionInfo Current { get; } = new(
            ProductVersion: "0.0.0-test",
            DesktopVersion: "0.0.0-test",
            AgentsVersion: "0.0.0-test",
            DbSchemaVersion: "0.0.0",
            BuildChannel: "test",
            BuildDate: "2026-01-01",
            MinApiContract: "1.0.0",
            MaxApiContract: "1.0.0");
    }
}
