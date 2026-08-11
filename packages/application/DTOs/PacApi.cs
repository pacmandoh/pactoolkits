namespace PacToolkits.Application.DTOs;

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
