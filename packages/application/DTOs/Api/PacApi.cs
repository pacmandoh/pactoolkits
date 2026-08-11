namespace PacToolkits.Application.DTOs;

public static class PacApiHeaders
{
    /// <summary>写命令幂等键</summary>
    public const string CommandId = "X-Command-Id";
}

public sealed record PacApiTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string ClientId);

public sealed record PacApiSystemInfo(
    string Product,
    string ApiVersion,
    string ContractVersion,
    DateTimeOffset Utc);
