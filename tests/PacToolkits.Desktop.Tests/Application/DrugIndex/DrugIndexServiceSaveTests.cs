using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class DrugIndexServiceSaveTests
{
    [Fact]
    public async Task SaveAsync_qty_change_uses_origin_key_for_trace_reference_check()
    {
        var dto = new DrugIndexDto(
            DrugId: "DrugA",
            Spec: "1g",
            Qty: 20,
            RuleKey: null,
            PreTc: null,
            Pos: null,
            Note: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Version: 1);

        var repo = new FakeDrugIndexRepo(
            preview: (sourceDrugId, sourceSpec, _, _, _) =>
                Task.FromResult(new DrugKeyFixPreviewDto(
                    SourceExists: true,
                    TargetExists: false,
                    TracePoolAffected: sourceDrugId == "DrugA" && sourceSpec == "1g" ? 2 : 0,
                    TraceTxnAffected: 0,
                    MsfxAffected: 0)),
            upsert: (_, _, _) => Task.FromResult(dto with { Version = 2 }));

        var service = new DrugIndexService(repo, new EmptyCatalogCache());

        var request = new DrugIndexSaveRequest(
            Dto: dto,
            OriginDrugId: "DrugA",
            OriginSpec: "1g",
            ExpectedVersion: 1,
            IsNew: false,
            HasPrimaryKeyChanges: false,
            HasQtyChanged: true);

        var result = await service.SaveAsync(request, CancellationToken.None);

        Assert.Equal(DrugSaveOutcome.BlockedQtyChange, result.Outcome);
        Assert.Null(result.Saved);
    }

    private sealed class FakeDrugIndexRepo(
        Func<string, string, string, string, CancellationToken, Task<DrugKeyFixPreviewDto>> preview,
        Func<DrugIndexDto, long?, CancellationToken, Task<DrugIndexDto>> upsert) : IDrugIndexRepo
    {
        public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> CountAsync(string? keyword, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DrugIndexDto>> ListCatalogAsync(int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct)
            => upsert(dto, expectedVersion, ct);

        public Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
            string sourceDrugId,
            string sourceSpec,
            string targetDrugId,
            string targetSpec,
            CancellationToken ct)
            => preview(sourceDrugId, sourceSpec, targetDrugId, targetSpec, ct);

        public Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
            DrugIndexDto source,
            DrugIndexDto target,
            string reason,
            string operatorName,
            string sourceTag,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class EmptyCatalogCache : IPinyinSearchCatalogCache
    {
        public Task<IReadOnlyList<DrugIndexDto>> GetCatalogRowsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<DrugIndexDto>>([]);

        public Task<IReadOnlyList<string>> GetSearchTextsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public void Invalidate()
        {
        }
    }
}
