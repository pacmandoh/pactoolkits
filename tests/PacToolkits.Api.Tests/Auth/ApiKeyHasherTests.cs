using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Tests;

public sealed class ApiKeyHasherTests
{
    [Fact]
    public void Hash_is_stable_lowercase_hex()
    {
        var hash = ApiKeyHasher.Hash("abc");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, ApiKeyHasher.Hash("abc"));
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    [Fact]
    public void FixedTimeEqualsHex_accepts_matching_hashes()
    {
        var hash = ApiKeyHasher.Hash("secret");
        Assert.True(ApiKeyHasher.FixedTimeEqualsHex(hash, hash.ToUpperInvariant()));
        Assert.False(ApiKeyHasher.FixedTimeEqualsHex(hash, ApiKeyHasher.Hash("other")));
    }

    [Fact]
    public void IsSha256Hex_requires_64_hex_chars()
    {
        Assert.True(ApiKeyHasher.IsSha256Hex(ApiKeyHasher.Hash("k")));
        Assert.True(ApiKeyHasher.IsSha256Hex(ApiKeyHasher.Hash("k").ToUpperInvariant()));
        Assert.False(ApiKeyHasher.IsSha256Hex(new string('g', 64)));
        Assert.False(ApiKeyHasher.IsSha256Hex(new string('a', 63)));
        Assert.False(ApiKeyHasher.IsSha256Hex(null));
    }
}
