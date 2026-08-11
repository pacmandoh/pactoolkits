using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class ScanCodeSubmitAtomicTests
{
    [Fact]
    public async Task SubmitAsync_rethrows_when_entry_log_fails()
    {
        var repo = new FakeScanRepo();
        var log = new FakeEntryLog { ThrowOnWrite = true };
        var service = new ScanCodeService(new FakeDrugIndexRepo(), repo, log);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(
                new ScanCodeSubmitRequest(
                    "d1",
                    "s1",
                    ["T1"],
                    new CodeAnalysis(1, 0, 0, 0, ["T1"]),
                    "c1"),
                CancellationToken.None));

        Assert.Equal(1, repo.InsertCalls);
        Assert.Equal(1, log.WriteCalls);
    }

    [Fact]
    public async Task SubmitAsync_accepts_pool_duplicate_in_total()
    {
        var repo = new FakeScanRepo();
        var log = new FakeEntryLog();
        var service = new ScanCodeService(new FakeDrugIndexRepo(), repo, log);

        // 1 池内重复 + 1 有效：Total=2, Duplicate=0, PoolDuplicate=1, codes=[T2]
        var result = await service.SubmitAsync(
            new ScanCodeSubmitRequest(
                "d1",
                "s1",
                ["T2"],
                new CodeAnalysis(2, 0, 0, 1, ["T2"]),
                "c1"),
            CancellationToken.None);

        Assert.True(result.DrugFound);
        Assert.Equal("success", result.EntryResult);
        Assert.Equal(1, repo.InsertCalls);
        Assert.Equal(1, log.WriteCalls);
        Assert.Equal(0, log.LastFailedCount);
        Assert.Contains("poolDuplicate=1", result.EntryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_pool_duplicate_only_is_success()
    {
        var repo = new FakeScanRepo();
        var log = new FakeEntryLog();
        var service = new ScanCodeService(new FakeDrugIndexRepo(), repo, log);

        var result = await service.SubmitAsync(
            new ScanCodeSubmitRequest(
                "d1",
                "s1",
                [],
                new CodeAnalysis(1, 0, 0, 1, []),
                "c1"),
            CancellationToken.None);

        Assert.True(result.DrugFound);
        Assert.Equal("success", result.EntryResult);
        Assert.Equal(0, log.LastFailedCount);
        Assert.Equal(1, repo.InsertCalls);
    }

    [Fact]
    public async Task SubmitAsync_rejects_mismatched_analysis_codes()
    {
        var repo = new FakeScanRepo();
        var service = new ScanCodeService(new FakeDrugIndexRepo(), repo, new FakeEntryLog());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(
                new ScanCodeSubmitRequest(
                    "d1",
                    "s1",
                    ["T1"],
                    new CodeAnalysis(0, 0, 0, 0, []),
                    "c1"),
                CancellationToken.None));

        Assert.Contains("Total", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, repo.InsertCalls);
    }

    [Fact]
    public async Task SubmitAsync_rejects_omitted_pool_duplicate_in_total()
    {
        var repo = new FakeScanRepo();
        var service = new ScanCodeService(new FakeDrugIndexRepo(), repo, new FakeEntryLog());

        // Total 未计入 PoolDuplicate 时与 ValidUniqueCodes 对不齐
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(
                new ScanCodeSubmitRequest(
                    "d1",
                    "s1",
                    ["T2"],
                    new CodeAnalysis(2, 0, 0, 0, ["T2"]),
                    "c1"),
                CancellationToken.None));

        Assert.Equal(0, repo.InsertCalls);
    }

    [Fact]
    public async Task SubmitAsync_rejects_divergent_valid_unique_lists()
    {
        var repo = new FakeScanRepo();
        var service = new ScanCodeService(new FakeDrugIndexRepo(), repo, new FakeEntryLog());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(
                new ScanCodeSubmitRequest(
                    "d1",
                    "s1",
                    ["T1"],
                    new CodeAnalysis(1, 0, 0, 0, ["T2"]),
                    "c1"),
                CancellationToken.None));

        Assert.Equal(0, repo.InsertCalls);
    }

    private sealed class FakeDrugIndexRepo : IDrugIndexRepo
    {
        public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> CountAsync(string? keyword, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DrugIndexDto>> ListCatalogAsync(int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
            => Task.FromResult<DrugIndexDto?>(new DrugIndexDto(
                drugId,
                spec,
                1,
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch,
                null,
                1));

        public Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();

        public Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
            string sourceDrugId,
            string sourceSpec,
            string targetDrugId,
            string targetSpec,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
            DrugIndexDto source,
            DrugIndexDto target,
            string reason,
            string operatorName,
            string sourceTag,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeScanRepo : IScanCodeRepo
    {
        public int InsertCalls { get; private set; }

        public Task<ScanCodeInsertResult> InsertTraceCodesAsync(
            string drugId,
            string spec,
            int qty,
            IReadOnlyList<string> codes,
            CancellationToken ct)
        {
            InsertCalls++;
            return Task.FromResult(new ScanCodeInsertResult(codes.Count, codes.Count, 0));
        }

        public Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(
            IReadOnlyList<string> traceCodes,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FakeEntryLog : ITraceEntryLogService
    {
        public int WriteCalls { get; private set; }

        public int LastFailedCount { get; private set; }

        public bool ThrowOnWrite { get; init; }

        public Task WriteAsync(TraceEntryLogDto dto, CancellationToken ct = default)
        {
            WriteCalls++;
            LastFailedCount = dto.FailedCount;
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("log failed");
            }

            return Task.CompletedTask;
        }
    }
}
