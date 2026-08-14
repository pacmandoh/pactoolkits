using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Tests;

public sealed class AuthOptionsValidationTests
{
    [Fact]
    public void Dev_allows_empty_clients()
    {
        var result = AuthOptionsValidator.Validate(new AuthOptions(), requireEnabledClient: false);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Production_requires_enabled_client()
    {
        var result = AuthOptionsValidator.Validate(new AuthOptions(), requireEnabledClient: true);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static f => f.Contains("at least one enabled client", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_client_requires_hash_and_known_scope()
    {
        var options = new AuthOptions
        {
            Clients =
            {
                ["site-a"] = new ClientOptions
                {
                    Enabled = true,
                    ApiKeyHash = string.Empty,
                    Scopes = [],
                },
            },
        };

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, static f => f.Contains("ApiKeyHash is required", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, static f => f.Contains("known scope", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_unknown_scope()
    {
        var options = ValidClientOptions();
        options.Clients["site-a"].Scopes = ["read", "admin"];

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, static f => f.Contains("unknown scope 'admin'", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_client_id_with_whitespace_or_dangerous_chars()
    {
        var hash = ApiFactory.TestApiKeyHash;
        foreach (var badId in new[] { " site-a", "site a", "site/a", "site:a", "" })
        {
            var options = new AuthOptions
            {
                Clients =
                {
                    [badId] = new ClientOptions
                    {
                        Enabled = true,
                        ApiKeyHash = hash,
                        Scopes = [AuthPolicies.Read],
                    },
                },
            };

            var result = AuthOptionsValidator.Validate(options, requireEnabledClient: false);

            Assert.False(result.Succeeded, badId);
            Assert.Contains(
                result.Failures!,
                static f => f.Contains("Auth:Clients key must be a non-empty ASCII id", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Rejects_duplicate_api_key_hash()
    {
        var hash = ApiFactory.TestApiKeyHash;
        var options = new AuthOptions
        {
            Clients =
            {
                ["site-a"] = new ClientOptions
                {
                    Enabled = true,
                    ApiKeyHash = hash,
                    Scopes = [AuthPolicies.Read],
                },
                ["site-b"] = new ClientOptions
                {
                    Enabled = true,
                    ApiKeyHash = hash.ToUpperInvariant(),
                    Scopes = [AuthPolicies.Write],
                },
            },
        };

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, static f => f.Contains("duplicates", StringComparison.Ordinal));
    }

    [Fact]
    public void Accepts_valid_production_clients()
    {
        var result = AuthOptionsValidator.Validate(ValidClientOptions(), requireEnabledClient: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Disabled_client_may_omit_hash_and_scopes()
    {
        var options = new AuthOptions
        {
            Clients =
            {
                ["retired"] = new ClientOptions
                {
                    Enabled = false,
                    ApiKeyHash = string.Empty,
                    Scopes = [],
                },
            },
        };

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: false);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Rejects_invalid_unlock_password_hash()
    {
        var options = ValidClientOptions();
        options.UnlockPasswordHash = "not-a-sha256";

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: true);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static f => f.Contains("UnlockPasswordHash", StringComparison.Ordinal));
    }

    [Fact]
    public void Accepts_empty_unlock_password_hash()
    {
        var options = ValidClientOptions();
        options.UnlockPasswordHash = string.Empty;

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Accepts_valid_unlock_password_hash()
    {
        var options = ValidClientOptions();
        options.UnlockPasswordHash = ApiFactory.TestUnlockPasswordHash;

        var result = AuthOptionsValidator.Validate(options, requireEnabledClient: true);

        Assert.True(result.Succeeded);
    }

    private static AuthOptions ValidClientOptions()
        => new()
        {
            Clients =
            {
                ["site-a"] = new ClientOptions
                {
                    Enabled = true,
                    ApiKeyHash = ApiFactory.TestApiKeyHash,
                    Scopes = [AuthPolicies.Read, AuthPolicies.SystemStatus],
                },
            },
        };
}
