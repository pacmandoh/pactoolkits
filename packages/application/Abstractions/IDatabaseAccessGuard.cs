namespace PacToolkits.Application.Abstractions;

public interface IDatabaseAccessGuard
{
    bool IsBlocked { get; }

    string? BlockReason { get; }

    void Block(string reason);

    void Clear();

    void ThrowIfBlocked();
}
