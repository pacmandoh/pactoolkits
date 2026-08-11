using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Tests;

public sealed class CommandDedupTests
{
    [Fact]
    public async Task Memory_claim_complete_replays()
    {
        var dedup = new MemoryCommandDedup();
        var key = new CommandDedupKey("c1", "op", Guid.NewGuid(), CommandDigest.Sha256Utf8("body"));

        var first = await dedup.ClaimAsync(key, CancellationToken.None);
        Assert.Equal(CommandDedupClaim.Acquired, first.Outcome);

        var entry = new CommandDedupEntry(200, "application/json", "{}"u8.ToArray());
        await dedup.CompleteAsync(key, entry, CancellationToken.None);

        var second = await dedup.ClaimAsync(key, CancellationToken.None);
        Assert.Equal(CommandDedupClaim.Completed, second.Outcome);
        Assert.Equal(200, second.Cached!.StatusCode);
    }

    [Fact]
    public async Task Memory_release_allows_reclaim()
    {
        var dedup = new MemoryCommandDedup();
        var key = new CommandDedupKey("c1", "op", Guid.NewGuid(), CommandDigest.Sha256Utf8("body"));

        Assert.Equal(CommandDedupClaim.Acquired, (await dedup.ClaimAsync(key, CancellationToken.None)).Outcome);
        await dedup.ReleaseAsync(key, CancellationToken.None);
        Assert.Equal(CommandDedupClaim.Acquired, (await dedup.ClaimAsync(key, CancellationToken.None)).Outcome);
    }
}
