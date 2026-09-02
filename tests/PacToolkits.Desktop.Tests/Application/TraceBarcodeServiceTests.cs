using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests.Application;

public sealed class TraceBarcodeServiceTests
{
    [Fact]
    public async Task Pick_merges_duplicate_drug_spec_and_extends_exclude_between_groups()
    {
        var repo = new RecordingTraceBarcodeRepo();
        var service = new TraceBarcodeService(repo);
        var batchId = Guid.NewGuid();

        var result = await service.PickAsync(
            new TraceBarcodePickRequest(
                batchId,
                "op@host",
                [
                    new TraceBarcodePickItem("d1", "s1", 2),
                    new TraceBarcodePickItem("d1", "s1", 3),
                    new TraceBarcodePickItem("d2", "s2", 1),
                ],
                7,
                ["seed"]),
            CancellationToken.None);

        Assert.Equal(2, repo.Calls.Count);
        Assert.Equal(("d1", "s1", 5), repo.Calls[0]);
        Assert.Equal(("d2", "s2", 1), repo.Calls[1]);
        Assert.Equal(2, result.Groups.Count);
        Assert.All(repo.BatchIds, actual => Assert.Equal(batchId, actual));
        Assert.All(repo.OperatorNames, actual => Assert.Equal("op@host", actual));
        Assert.Contains("seed", repo.ExcludeSnapshots[0]);
        Assert.Contains("code-1", repo.ExcludeSnapshots[1]);
    }

    [Fact]
    public async Task Pick_rejects_total_count_above_limit()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());
        var items = Enumerable
            .Range(0, TraceBarcodeService.MaxPickItems)
            .Select(i => new TraceBarcodePickItem($"d{i}", $"s{i}", TraceBarcodeService.MaxCountPerItem))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PickAsync(new TraceBarcodePickRequest(Guid.NewGuid(), "op@host", items, 0, null), CancellationToken.None));
    }

    [Fact]
    public async Task Pick_rejects_item_count_above_limit()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());
        var items = Enumerable
            .Range(0, TraceBarcodeService.MaxPickItems + 1)
            .Select(i => new TraceBarcodePickItem($"d{i}", $"s{i}", 1))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PickAsync(new TraceBarcodePickRequest(Guid.NewGuid(), "op@host", items, 0, null), CancellationToken.None));
    }

    [Fact]
    public async Task Pick_requires_batch_and_operator_for_preview_reservation()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());
        var items = new[] { new TraceBarcodePickItem("d1", "s1", 1) };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PickAsync(new TraceBarcodePickRequest(Guid.Empty, "op@host", items, 0, null), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PickAsync(new TraceBarcodePickRequest(Guid.NewGuid(), " ", items, 0, null), CancellationToken.None));
    }

    [Fact]
    public async Task Pick_rejects_non_positive_or_merged_count_above_limit()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PickAsync(
                new TraceBarcodePickRequest(
                    Guid.NewGuid(),
                    "op@host",
                    [new TraceBarcodePickItem("d1", "s1", 0)],
                    0,
                    null),
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PickAsync(
                new TraceBarcodePickRequest(
                    Guid.NewGuid(),
                    "op@host",
                    [
                        new TraceBarcodePickItem("d1", "s1", TraceBarcodeService.MaxCountPerItem),
                        new TraceBarcodePickItem("d1", "s1", 1),
                    ],
                    0,
                    null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Pick_clamps_exclude_recent_days()
    {
        var repo = new RecordingTraceBarcodeRepo();
        var service = new TraceBarcodeService(repo);

        await service.PickAsync(
            new TraceBarcodePickRequest(
                Guid.NewGuid(),
                "op@host",
                [new TraceBarcodePickItem("d1", "s1", 1)],
                9999,
                null),
            CancellationToken.None);

        Assert.Equal(BarcodeGenOptions.MaxExcludeRecentDays, repo.LastExcludeRecentDays);
    }

    [Fact]
    public async Task Audit_rejects_item_count_above_limit()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());
        var items = Enumerable
            .Range(0, TraceBarcodeService.MaxAuditItems + 1)
            .Select(i => new TraceBarcodeAuditItem($"code-{i}", null, null, null))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuditAsync(
                new TraceBarcodeAuditRequest(Guid.NewGuid(), "preview", "op", items),
                CancellationToken.None));
    }

    [Fact]
    public async Task Audit_requires_trace_code_and_error_on_failure()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuditAsync(
                new TraceBarcodeAuditRequest(
                    Guid.NewGuid(),
                    "export",
                    "op",
                    [new TraceBarcodeAuditItem(" ", null, null, "a.png", false, null)]),
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuditAsync(
                new TraceBarcodeAuditRequest(
                    Guid.NewGuid(),
                    "export",
                    "op",
                    [new TraceBarcodeAuditItem("code", null, null, "a.png", false, null)]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Audit_rejects_trace_code_above_limit()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());
        var longCode = new string('a', TraceBarcodeService.MaxTraceCodeLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuditAsync(
                new TraceBarcodeAuditRequest(
                    Guid.NewGuid(),
                    "preview",
                    "op",
                    [new TraceBarcodeAuditItem(longCode, null, null, null)]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Audit_rejects_empty_batch_id()
    {
        var service = new TraceBarcodeService(new RecordingTraceBarcodeRepo());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuditAsync(
                new TraceBarcodeAuditRequest(
                    Guid.Empty,
                    "preview",
                    "op",
                    [new TraceBarcodeAuditItem("code", null, null, null)]),
                CancellationToken.None));
    }

    private sealed class RecordingTraceBarcodeRepo : ITraceBarcodeRepo
    {
        public List<(string DrugId, string Spec, int Count)> Calls { get; } = new();
        public List<IReadOnlyList<string>> ExcludeSnapshots { get; } = new();
        public List<Guid> BatchIds { get; } = new();
        public List<string> OperatorNames { get; } = new();
        public int? LastExcludeRecentDays { get; private set; }

        public Task<TraceBarcodePickGroup> PickAsync(
            Guid batchId,
            string operatorName,
            string drugId,
            string spec,
            int count,
            int? excludeRecentDays,
            IReadOnlyList<string> excludeTraceCodes,
            CancellationToken ct)
        {
            BatchIds.Add(batchId);
            OperatorNames.Add(operatorName);
            Calls.Add((drugId, spec, count));
            ExcludeSnapshots.Add(excludeTraceCodes.ToArray());
            LastExcludeRecentDays = excludeRecentDays;
            var code = $"code-{Calls.Count}";
            return Task.FromResult(new TraceBarcodePickGroup(
                drugId,
                spec,
                count,
                1,
                Math.Max(0, count - 1),
                [new TraceBarcodePickedRow(code, drugId, spec, 1, 1, null)]));
        }

        public Task<int> InsertAuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
            => Task.FromResult(request.Items.Count);

        public Task<int> ClearAuditLogAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
