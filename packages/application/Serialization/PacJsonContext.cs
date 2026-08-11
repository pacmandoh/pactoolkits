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
public partial class PacJsonContext : JsonSerializerContext;
