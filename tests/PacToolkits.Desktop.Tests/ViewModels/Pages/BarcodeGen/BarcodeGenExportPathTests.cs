using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Platform;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests.ViewModels.Pages;

public sealed class BarcodeGenExportPathTests
{
    [Fact]
    public void ResolveExportPath_rejects_parent_directory_escape()
    {
        var folder = Path.GetTempPath();

        Assert.Throws<InvalidOperationException>(() =>
            BarcodeGen.ResolveExportPath(folder, "../escape.png"));
    }

    [Fact]
    public void ResolveExportPath_allows_file_inside_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pactoolkits-export-test");
        Directory.CreateDirectory(folder);
        try
        {
            var path = BarcodeGen.ResolveExportPath(folder, "label.png");
            Assert.StartsWith(folder, path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("label.png", path, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ResolveExportPath_rejects_case_changed_sibling_on_non_windows_platforms()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows paths are case-insensitive");
        var folder = Path.Combine(Path.GetTempPath(), "PacToolkitsExportRoot");

        Assert.Throws<InvalidOperationException>(() =>
            BarcodeGen.ResolveExportPath(folder, "../pactoolkitsexportroot/escape.png"));
    }

    [Fact]
    public async Task Separate_export_attempts_use_separate_audit_batches()
    {
        var folder = CreateTempFolder();
        var traceBarcode = new RecordingTraceBarcode();
        var picker = new RecordingFolderPicker(folder);
        var page = CreatePage(traceBarcode, picker);
        AddPreview(page, "code-1");

        try
        {
            await page.ExportAllCommand.ExecuteAsync(null);
            await page.ExportAllCommand.ExecuteAsync(null);

            Assert.Equal(2, traceBarcode.Audits.Count);
            Assert.NotEqual(traceBarcode.Audits[0].BatchId, traceBarcode.Audits[1].BatchId);
        }
        finally
        {
            await page.DisposePageAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_does_not_turn_unwritten_files_into_audit_items()
    {
        var folder = CreateTempFolder();
        var traceBarcode = new RecordingTraceBarcode();
        var picker = new RecordingFolderPicker(folder);
        var page = CreatePage(traceBarcode, picker);
        AddPreview(page, "code-1");
        picker.AfterPick = () => page.OnPageDeactivatedAsync();

        try
        {
            await page.ExportAllCommand.ExecuteAsync(null);

            Assert.Empty(traceBarcode.Audits);
        }
        finally
        {
            await page.DisposePageAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Audit_clear_discards_pending_export_retry()
    {
        var folder = CreateTempFolder();
        var traceBarcode = new RecordingTraceBarcode { FailNextAudit = true };
        var picker = new RecordingFolderPicker(folder);
        var page = CreatePage(traceBarcode, picker);
        AddPreview(page, "code-1");

        try
        {
            await page.ExportAllCommand.ExecuteAsync(null);
            await traceBarcode.ClearAuditLogAsync(TestContext.Current.CancellationToken);
            await page.ExportAllCommand.ExecuteAsync(null);

            Assert.Equal(2, picker.Calls);
        }
        finally
        {
            await page.DisposePageAsync();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static BarcodeGen CreatePage(ITraceBarcodeService traceBarcode, IFolderPickerService picker)
    {
        var page = new BarcodeGen(
            traceBarcode,
            null!,
            null!,
            null!,
            null!,
            picker,
            NoopToast.Instance,
            null!);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());
        return page;
    }

    private static void AddPreview(BarcodeGen page, string traceCode)
        => page.PreviewCards.Add(new PreviewCard(
            traceCode,
            "drug",
            "spec",
            "title",
            [1, 2, 3],
            $"{traceCode}.png",
            Array.Empty<InfoDetailItem>()));

    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pactoolkits-barcode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class RecordingFolderPicker(string folder) : IFolderPickerService
    {
        public int Calls { get; private set; }
        public Func<Task>? AfterPick { get; set; }

        public void Bind(Func<global::Avalonia.Platform.Storage.IStorageProvider?> providerFactory)
        {
        }

        public async Task<string?> PickFolderAsync(CancellationToken ct = default)
        {
            Calls++;
            if (AfterPick is not null)
            {
                await AfterPick();
            }

            return folder;
        }
    }

    private sealed class RecordingTraceBarcode : ITraceBarcodeService
    {
        public event Action? AuditCleared;
        public List<TraceBarcodeAuditRequest> Audits { get; } = [];
        public bool FailNextAudit { get; set; }

        public Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct)
            => Task.FromResult(new TraceBarcodePickResult([]));

        public Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
        {
            if (FailNextAudit)
            {
                FailNextAudit = false;
                throw new InvalidOperationException("audit unavailable");
            }

            Audits.Add(request);
            return Task.FromResult(request.Items.Count);
        }

        public Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null)
        {
            AuditCleared?.Invoke();
            return Task.FromResult(0);
        }
    }

    private sealed class NoopToast : IToastService
    {
        public static readonly NoopToast Instance = new();

        public void Success(string title, string message) { }
        public void Error(string title, string message) { }
        public void Warn(string title, string message) { }
        public void Info(string title, string message) { }
    }
}
