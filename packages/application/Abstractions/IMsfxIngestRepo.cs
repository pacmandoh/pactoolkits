using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 单据与明细入库
/// </summary>
public interface IMsfxIngestRepo
{
    Task<long> UpsertInboundBillAsync(
        long batchId,
        string billCode,
        string billType,
        string billTime,
        string billUploadTime,
        string fromRefUserId,
        string fromEntName,
        string toRefUserId,
        string status,
        string rawJson,
        CancellationToken ct);

    Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<(
            string DrugName,
            string PackageSpec,
            string PrepnSpec,
            string BatchNo,
            IReadOnlyList<(
                string Code,
                string CodeLevel,
                string? Level1Code,
                string? Level2Code,
                string? Level3Code,
                string? Level4Code,
                string? Level5Code)> Codes)> drugs,
        CancellationToken ct);
}
