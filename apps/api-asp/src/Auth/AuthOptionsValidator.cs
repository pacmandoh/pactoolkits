using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PacToolkits.Api.Auth;

/// <summary>
/// Auth:Clients 启动校验
///
/// Production 至少要有一个可用的 enabled client；非 Production 允许空 Clients 便于本地与测试
/// </summary>
public sealed partial class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    private readonly IHostEnvironment _env;

    public AuthOptionsValidator(IHostEnvironment env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
        => Validate(options, requireEnabledClient: _env.IsProduction());

    public static ValidateOptionsResult Validate(AuthOptions options, bool requireEnabledClient)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var seenHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var enabledCount = 0;

        foreach (var (clientId, client) in options.Clients)
        {
            if (!IsValidClientId(clientId))
            {
                failures.Add(
                    "Auth:Clients key must be a non-empty ASCII id "
                    + $"(letter/digit then letter/digit/._-; no whitespace); got '{clientId}'");
            }

            if (client is null)
            {
                failures.Add($"Auth:Clients:{clientId} must not be null");
                continue;
            }

            var hash = client.ApiKeyHash?.Trim() ?? string.Empty;
            if (hash.Length > 0)
            {
                if (!ApiKeyHasher.IsSha256Hex(hash))
                {
                    failures.Add($"Auth:Clients:{clientId}:ApiKeyHash must be 64-char SHA-256 hex when set");
                }
                else
                {
                    var normalized = hash.ToLowerInvariant();
                    if (seenHashes.TryGetValue(normalized, out var otherId))
                    {
                        failures.Add(
                            $"Auth:Clients:{clientId}:ApiKeyHash duplicates Auth:Clients:{otherId}");
                    }
                    else
                    {
                        seenHashes[normalized] = clientId;
                    }
                }
            }

            var scopes = AuthPolicies.NormalizeScopes(client.Scopes);
            foreach (var scope in scopes)
            {
                if (!AuthPolicies.IsKnownScope(scope))
                {
                    failures.Add($"Auth:Clients:{clientId}:Scopes contains unknown scope '{scope}'");
                }
            }

            if (!client.Enabled)
            {
                continue;
            }

            enabledCount++;

            if (!ApiKeyHasher.IsSha256Hex(hash))
            {
                failures.Add($"Auth:Clients:{clientId}:ApiKeyHash is required for enabled clients");
            }

            if (scopes.Count == 0)
            {
                failures.Add(
                    $"Auth:Clients:{clientId}:Scopes must include at least one known scope when Enabled");
            }
        }

        if (requireEnabledClient && enabledCount == 0)
        {
            failures.Add("Auth:Clients must include at least one enabled client in Production");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidClientId(string? clientId)
        => !string.IsNullOrEmpty(clientId) && ClientIdPattern().IsMatch(clientId);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
