using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Tests.ViewModels.Pages;

public sealed class BarcodeGenAuditClearTests
{
    [Fact]
    public async Task ClearAuditLog_resets_session_code_exclusions()
    {
        var traceBarcode = new FakeTraceBarcode();
        var page = new PacToolkits.Desktop.Avalonia.ViewModels.Pages.BarcodeGen(
            traceBarcode,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        page.TestTrackSessionCode("code-1");

        await traceBarcode.ClearAuditLogAsync(TestContext.Current.CancellationToken);

        Assert.Empty(page.TestSessionCodes);
    }

    private sealed class FakeTraceBarcode : ITraceBarcodeService
    {
        public event Action? AuditCleared;

        public Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct)
            => Task.FromResult(new TraceBarcodePickResult([]));

        public Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
            => Task.FromResult(0);

        public Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null)
        {
            AuditCleared?.Invoke();
            return Task.FromResult(1);
        }
    }
}
