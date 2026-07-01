namespace PacToolkits.Application.DTOs;

public sealed record MsfxApiCallResult(
    bool Ok,
    int? HttpStatusCode,
    string Summary,
    string BizCode,
    string BizMessage,
    string RequestId,
    string ResponseText,
    string RequestTrace);

public sealed record MsfxListUpoutRequest(
    string RefEntId,
    string BeginDate,
    string EndDate,
    long Page = 1,
    long PageSize = 50);

public sealed record MsfxListUpoutItem(
    string BillCode,
    string BillType,
    string BillTime,
    string BillUploadTime,
    string PhysicName,
    string PkgSpec,
    string PrepnSpec,
    long PrepnCount,
    long CodeCount,
    string ProduceBatchNo,
    string ExpireDate,
    string FromEntName,
    string ProduceEntName,
    string FromRefUserId,
    string ToRefUserId,
    string ConfirmStatus,
    string DrugTag,
    string IsCollectDrugBill,
    string IsSpecialDrugBill,
    string IsBloodProductBill,
    string IsBiologicalProductBill,
    string IsBotulinumBill,
    string VerifyStatus,
    string RegulatedFlag,
    string LogisticsStatus,
    string Status);

public sealed record MsfxListUpoutResult(
    MsfxApiCallResult Call,
    long Total,
    IReadOnlyList<MsfxListUpoutItem> Items);

public sealed record MsfxListUpoutDetailRequest(
    string RefEntId,
    string BillCode,
    string? ToRefUserId,
    string? FromRefUserId = null);

public sealed record MsfxTraceCodeItem(
    string Code,
    string CodeLevel,
    string? Level1Code = null,
    string? Level2Code = null,
    string? Level3Code = null,
    string? Level4Code = null,
    string? Level5Code = null);

public sealed record MsfxDrugDetailItem(
    string PhysicName,
    string PackageSpec,
    string PrepnSpec,
    string ProduceBatchNo,
    IReadOnlyList<MsfxTraceCodeItem> TraceCodes);

public sealed record MsfxListUpoutDetailResult(
    MsfxApiCallResult Call,
    string BillCode,
    IReadOnlyList<MsfxDrugDetailItem> DrugItems,
    IReadOnlyList<string> MinimalSalesTraceCodes,
    string RelationErrorHint);
