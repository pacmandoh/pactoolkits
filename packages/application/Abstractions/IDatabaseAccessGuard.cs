namespace PacToolkits.Application.Abstractions;

// Compatibility gate shared with PgDb. Repos and services that bypass IDb must call ThrowIfBlocked().
public interface IDatabaseAccessGuard
{
    bool IsBlocked { get; }

    string? BlockReason { get; }

    void Block(string reason);

    void Clear();

    void ThrowIfBlocked();
}
