using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// 写命令幂等领取参数
///
/// ClientId、Operation、CommandId 定身份；RequestDigest 只用来发现请求体被改
/// </summary>
public sealed record CommandDedupKey(
    string ClientId,
    string Operation,
    Guid CommandId,
    string RequestDigest);

/// <summary>已完成命令的可回放响应</summary>
public sealed record CommandDedupEntry(
    int StatusCode,
    string? ContentType,
    byte[]? Body);

public enum CommandDedupClaim
{
    Acquired,
    Completed,
    InProgress,

    /// <summary>同身份命令的请求摘要不一致</summary>
    PayloadMismatch,
}

public sealed record CommandDedupClaimResult(
    CommandDedupClaim Outcome,
    CommandDedupEntry? Cached);

/// <summary>写命令幂等存储</summary>
public interface ICommandDedup
{
    /// <summary>失败时是否要调 Release；false 表示外层事务回滚即释放 claim</summary>
    bool ReleaseOnFailure { get; }

    Task<CommandDedupClaimResult> ClaimAsync(CommandDedupKey key, CancellationToken ct);

    Task CompleteAsync(CommandDedupKey key, CommandDedupEntry entry, CancellationToken ct);

    /// <summary>未完成时清掉 claim，便于同身份再 Claim</summary>
    Task ReleaseAsync(CommandDedupKey key, CancellationToken ct);
}

internal static class CommandDedupKeys
{
    public static void Validate(CommandDedupKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.ClientId))
        {
            throw new ArgumentException("ClientId is required", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(key.Operation))
        {
            throw new ArgumentException("Operation is required", nameof(key));
        }

        if (key.CommandId == Guid.Empty)
        {
            throw new ArgumentException("CommandId must be non-empty", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(key.RequestDigest))
        {
            throw new ArgumentException("RequestDigest is required", nameof(key));
        }
    }
}

/// <summary>请求体 SHA-256 hex，用作 <see cref="CommandDedupKey.RequestDigest"/></summary>
public static class CommandDigest
{
    public static string Sha256Hex(ReadOnlySpan<byte> payload)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256Utf8(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Sha256Hex(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>进程内去重；重启即清空，勿作站点默认实现</summary>
public sealed class MemoryCommandDedup : ICommandDedup
{
    private readonly ConcurrentDictionary<Id, Slot> _slots = new();

    public bool ReleaseOnFailure => true;

    public Task<CommandDedupClaimResult> ClaimAsync(CommandDedupKey key, CancellationToken ct)
    {
        CommandDedupKeys.Validate(key);
        ct.ThrowIfCancellationRequested();

        var id = ToId(key);
        var created = new Slot(key.RequestDigest);
        if (_slots.TryAdd(id, created))
        {
            return Task.FromResult(new CommandDedupClaimResult(CommandDedupClaim.Acquired, Cached: null));
        }

        if (!_slots.TryGetValue(id, out var existing))
        {
            if (_slots.TryAdd(id, created))
            {
                return Task.FromResult(new CommandDedupClaimResult(CommandDedupClaim.Acquired, Cached: null));
            }

            existing = _slots[id];
        }

        lock (existing.Gate)
        {
            if (!DigestEquals(existing.RequestDigest, key.RequestDigest))
            {
                return Task.FromResult(
                    new CommandDedupClaimResult(CommandDedupClaim.PayloadMismatch, Cached: null));
            }

            if (existing.Completed)
            {
                return Task.FromResult(
                    new CommandDedupClaimResult(CommandDedupClaim.Completed, existing.Entry));
            }

            return Task.FromResult(
                new CommandDedupClaimResult(CommandDedupClaim.InProgress, Cached: null));
        }
    }

    public Task CompleteAsync(CommandDedupKey key, CommandDedupEntry entry, CancellationToken ct)
    {
        _ = ct;
        CommandDedupKeys.Validate(key);
        ArgumentNullException.ThrowIfNull(entry);

        var id = ToId(key);
        if (!_slots.TryGetValue(id, out var slot))
        {
            throw new InvalidOperationException("command dedup key was not claimed");
        }

        lock (slot.Gate)
        {
            EnsureDigestMatch(slot, key);
            slot.Entry = entry;
            slot.Completed = true;
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(CommandDedupKey key, CancellationToken ct)
    {
        _ = ct;
        CommandDedupKeys.Validate(key);

        var id = ToId(key);
        if (!_slots.TryGetValue(id, out var slot))
        {
            return Task.CompletedTask;
        }

        lock (slot.Gate)
        {
            EnsureDigestMatch(slot, key);
            if (!slot.Completed)
            {
                _slots.TryRemove(id, out _);
            }
        }

        return Task.CompletedTask;
    }

    private static Id ToId(CommandDedupKey key)
        => new(key.ClientId, key.Operation, key.CommandId);

    private static void EnsureDigestMatch(Slot slot, CommandDedupKey key)
    {
        if (!DigestEquals(slot.RequestDigest, key.RequestDigest))
        {
            throw new InvalidOperationException("command dedup request digest mismatch");
        }
    }

    private static bool DigestEquals(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private sealed record Id(string ClientId, string Operation, Guid CommandId);

    private sealed class Slot(string requestDigest)
    {
        public object Gate { get; } = new();

        public string RequestDigest { get; } = requestDigest;

        public bool Completed { get; set; }

        public CommandDedupEntry? Entry { get; set; }
    }
}
