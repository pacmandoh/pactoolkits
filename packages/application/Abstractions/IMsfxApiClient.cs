using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

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
