namespace PacToolkits.Api.Auth;

/// <summary>JWT scope 与授权策略名（同名）</summary>
public static class AuthPolicies
{
    public const string ScopeClaim = "scope";

    public const string Read = "read";

    public const string Write = "write";

    public const string SystemStatus = "system.status";
}
