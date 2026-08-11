using System.Text.Json.Serialization;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Serialization;

/// <summary>PacApi 协议 DTO 的 JsonSerializerContext</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChangeWatermarkItem))]
[JsonSerializable(typeof(ChangeWatermarkItem[]))]
[JsonSerializable(typeof(ChangeWatermarksResponse))]
[JsonSerializable(typeof(PacApiTokenResponse))]
[JsonSerializable(typeof(PacApiSystemInfo))]
[JsonSerializable(typeof(ClientCountDto))]
[JsonSerializable(typeof(DashboardStringListResponse))]
[JsonSerializable(typeof(DashboardSnapshotResponse))]
[JsonSerializable(typeof(DashboardKpiDto))]
[JsonSerializable(typeof(TrendRowDto))]
[JsonSerializable(typeof(TraceTxnDto))]
[JsonSerializable(typeof(TraceEntryLogDto))]
[JsonSerializable(typeof(EntryChartRowDto))]
[JsonSerializable(typeof(AbnormalRowDto))]
[JsonSerializable(typeof(PagedResult<TraceTxnDto>))]
[JsonSerializable(typeof(PagedResult<TrendRowDto>))]
[JsonSerializable(typeof(PagedResult<TraceEntryLogDto>))]
[JsonSerializable(typeof(PagedResult<AbnormalRowDto>))]
public partial class PacJsonContext : JsonSerializerContext;
