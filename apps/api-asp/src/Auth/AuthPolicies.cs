namespace PacToolkits.Api.Auth;

/// <summary>JWT scope 与授权策略名（同名）</summary>
public static class AuthPolicies
{
    public const string ScopeClaim = "scope";

    public const string Read = "read";

    public const string Write = "write";

    public const string SystemStatus = "system.status";

    public static readonly IReadOnlySet<string> KnownScopes =
        new HashSet<string>(StringComparer.Ordinal) { Read, Write, SystemStatus };

    public static bool IsKnownScope(string? scope)
        => !string.IsNullOrWhiteSpace(scope) && KnownScopes.Contains(scope.Trim());

    public static IReadOnlyList<string> NormalizeScopes(IEnumerable<string>? scopes)
    {
        if (scopes is null)
        {
            return [];
        }

        return scopes
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
