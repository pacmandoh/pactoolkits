using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Barcode;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.UiTests;

public sealed class BarcodeGenPartialPickTests
{
    [AvaloniaFact]
    public async Task Generate_shows_all_available_previews_when_one_queue_item_has_gap()
    {
        var traceBarcode = new PartialTraceBarcode();
        var toast = new RecordingToast();
        var page = new PacToolkits.Desktop.Avalonia.ViewModels.Pages.BarcodeGen(
            traceBarcode,
            null!,
            null!,
            new FakeSettings(),
            new TraceCodeBarcodeService(),
            null!,
            toast,
            null!);
        page.TestInjectServices(apiAvailability: new ReadyApi());
        var fullRow = new PoolRow("drug-1", "spec-1", 1);
        var gapRow = new PoolRow("drug-2", "spec-2", 10);
        page.PoolRows.Add(fullRow);
        page.PoolRows.Add(gapRow);

        try
        {
            await page.GenerateCommand.ExecuteAsync(null);

            Assert.True(page.PreviewCards.Count == 10, toast.LastError);
            Assert.Equal(PoolPickStatus.Ok, fullRow.PickStatus);
            Assert.Equal(PoolPickStatus.Gap, gapRow.PickStatus);
            Assert.Contains("缺口 1", gapRow.StatusHint);
            Assert.Equal("已生成 10 张条码，缺口 1 张", page.GenerateSummary);
        }
        finally
        {
            await page.DisposePageAsync();
        }
    }

    private sealed class PartialTraceBarcode : ITraceBarcodeService
    {
        public event Action? AuditCleared
        {
            add { }
            remove { }
        }

        public Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct)
        {
            Assert.Equal(2, request.Items.Count);
            var full = request.Items[0];
            var gap = request.Items[1];
            var codes = Enumerable.Range(1, 9)
                .Select(i => new TraceBarcodePickedRow(
                    $"8000000000000000000{i}",
                    gap.DrugId,
                    gap.Spec,
                    1,
                    1,
                    null))
                .ToArray();
            return Task.FromResult(new TraceBarcodePickResult(
                [
                    new TraceBarcodePickGroup(
                        full.DrugId,
                        full.Spec,
                        1,
                        1,
                        0,
                        [new TraceBarcodePickedRow("70000000000000000000", full.DrugId, full.Spec, 1, 1, null)]),
                    new TraceBarcodePickGroup(gap.DrugId, gap.Spec, 10, 9, 1, codes)
                ]));
        }

        public Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
            => Task.FromResult(request.Items.Count);

        public Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null)
            => Task.FromResult(0);
    }

    private sealed class FakeSettings : IBarcodeGenSettingsService
    {
        public BarcodeGenOptions Current { get; } = new();

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void Apply(BarcodeGenOptions options)
        {
        }

        public Task SaveAsync(BarcodeGenOptions options, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingToast : IToastService
    {
        public string? LastError { get; private set; }

        public void Success(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
            LastError = $"{title}: {message}";
        }

        public void Warn(string title, string message)
        {
        }

        public void Info(string title, string message)
        {
        }
    }

    private sealed class ReadyApi : IApiAvailabilityService
    {
        public ApiAvailabilitySnapshot Current { get; } = new(
            ApiAvailabilityState.Ready,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true);

        public bool IsConfigured => true;
        public string? LastApiVersion => null;
        public string? LastContractVersion => null;
        public string? LastDatabase => null;
        public string? LastSchema => null;
        public string? LastSchemaVersion => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public Task ProbeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
