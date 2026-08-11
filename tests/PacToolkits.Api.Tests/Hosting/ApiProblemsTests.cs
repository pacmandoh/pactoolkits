using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class ApiProblemsTests
{
    [Fact]
    public async Task Conflict_includes_code_trace_and_current_version()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        await using var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            TraceIdentifier = "trace-1",
            RequestServices = provider,
            Response = { Body = new MemoryStream() },
        };

        var result = ApiProblems.Conflict(
            http,
            title: "row changed",
            detail: "refresh",
            currentVersion: 9);

        await result.ExecuteAsync(http);

        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        var json = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"code\":\"conflict\"", json, StringComparison.Ordinal);
        Assert.Contains("\"currentVersion\":9", json, StringComparison.Ordinal);
        Assert.Contains("\"traceId\":\"trace-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"detail\":\"refresh\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCommandId_requires_valid_uuid()
    {
        var http = new DefaultHttpContext();
        Assert.False(ApiProblems.TryGetCommandId(http.Request, out _, out var missing));
        Assert.NotNull(missing);

        http.Request.Headers[PacApiHeaders.CommandId] = "not-a-guid";
        Assert.False(ApiProblems.TryGetCommandId(http.Request, out _, out var invalid));
        Assert.NotNull(invalid);

        http.Request.Headers[PacApiHeaders.CommandId] = Guid.Empty.ToString("D");
        Assert.False(ApiProblems.TryGetCommandId(http.Request, out _, out var empty));
        Assert.NotNull(empty);

        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        http.Request.Headers[PacApiHeaders.CommandId] = id.ToString("D");
        Assert.True(ApiProblems.TryGetCommandId(http.Request, out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal(id, parsed);
    }

    [Fact]
    public async Task MemoryCommandDedup_claim_complete_replays()
    {
        var dedup = new MemoryCommandDedup();
        var key = new CommandDedupKey(
            ClientId: "c1",
            Operation: "demo.write",
            CommandId: Guid.NewGuid(),
            RequestDigest: CommandDigest.Sha256Utf8("""{"x":1}"""));
        var entry = new CommandDedupEntry(200, "application/json", "{}"u8.ToArray());
        var ct = TestContext.Current.CancellationToken;

        var first = await dedup.ClaimAsync(key, ct);
        Assert.Equal(CommandDedupClaim.Acquired, first.Outcome);

        var concurrent = await dedup.ClaimAsync(key, ct);
        Assert.Equal(CommandDedupClaim.InProgress, concurrent.Outcome);

        await dedup.CompleteAsync(key, entry, ct);
        var replay = await dedup.ClaimAsync(key, ct);
        Assert.Equal(CommandDedupClaim.Completed, replay.Outcome);
        Assert.NotNull(replay.Cached);
        Assert.Equal(200, replay.Cached.StatusCode);
    }

    [Fact]
    public async Task MemoryCommandDedup_release_allows_reclaim()
    {
        var dedup = new MemoryCommandDedup();
        var key = new CommandDedupKey(
            ClientId: "c1",
            Operation: "demo.write",
            CommandId: Guid.NewGuid(),
            RequestDigest: CommandDigest.Sha256Utf8("body"));
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(CommandDedupClaim.Acquired, (await dedup.ClaimAsync(key, ct)).Outcome);
        await dedup.ReleaseAsync(key, ct);
        Assert.Equal(CommandDedupClaim.Acquired, (await dedup.ClaimAsync(key, ct)).Outcome);
    }

    [Fact]
    public async Task MemoryCommandDedup_concurrent_claims_single_acquired()
    {
        var dedup = new MemoryCommandDedup();
        var key = new CommandDedupKey(
            ClientId: "c1",
            Operation: "demo.write",
            CommandId: Guid.NewGuid(),
            RequestDigest: CommandDigest.Sha256Utf8("same"));
        var ct = TestContext.Current.CancellationToken;

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => dedup.ClaimAsync(key, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Outcome == CommandDedupClaim.Acquired));
        Assert.Equal(31, results.Count(r => r.Outcome == CommandDedupClaim.InProgress));
    }

    [Fact]
    public async Task MemoryCommandDedup_same_command_id_different_digest_is_mismatch()
    {
        var dedup = new MemoryCommandDedup();
        var commandId = Guid.NewGuid();
        var first = new CommandDedupKey(
            ClientId: "c1",
            Operation: "demo.write",
            CommandId: commandId,
            RequestDigest: CommandDigest.Sha256Utf8("""{"x":1}"""));
        var altered = first with { RequestDigest = CommandDigest.Sha256Utf8("""{"x":2}""") };
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(CommandDedupClaim.Acquired, (await dedup.ClaimAsync(first, ct)).Outcome);
        Assert.Equal(
            CommandDedupClaim.PayloadMismatch,
            (await dedup.ClaimAsync(altered, ct)).Outcome);

        await dedup.CompleteAsync(
            first,
            new CommandDedupEntry(200, "application/json", "{}"u8.ToArray()),
            ct);

        Assert.Equal(
            CommandDedupClaim.PayloadMismatch,
            (await dedup.ClaimAsync(altered, ct)).Outcome);
        Assert.Equal(
            CommandDedupClaim.Completed,
            (await dedup.ClaimAsync(first, ct)).Outcome);
    }
}
