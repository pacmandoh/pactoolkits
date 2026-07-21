namespace PacToolkits.Application.Abstractions;

/// <summary>
/// Schema 不兼容时的共享阻断门；绕过 IDb 的路径也须 ThrowIfBlocked()
/// </summary>
public interface IDbAccessGuard
{
    bool IsBlocked { get; }

    string? BlockReason { get; }

    void Block(string reason);

    void Clear();

    void ThrowIfBlocked();
}
