using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 码上放心（MSFX）HTTP API 客户端抽象
/// </summary>
public interface IMsfxApiClient
{
    Task<MsfxApiCallResult> ExecuteRawAsync(
        MsfxApiOptions options,
        string method,
        IReadOnlyDictionary<string, string?> bizParams,
        CancellationToken ct);

    Task<MsfxListUpoutResult> GetYljgListUpoutAsync(
        MsfxApiOptions options,
        MsfxListUpoutRequest request,
        CancellationToken ct);

    Task<MsfxListUpoutDetailResult> GetYljgListUpoutDetailAsync(
        MsfxApiOptions options,
        MsfxListUpoutDetailRequest request,
        CancellationToken ct);
}
