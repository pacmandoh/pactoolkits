namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 数据库结构不兼容时的共享访问限制；独立连接路径也必须调用 <c>ThrowIfBlocked</c>
/// </summary>
public interface IDbAccessGuard
{
    bool IsBlocked { get; }

    string? BlockReason { get; }

    void Block(string reason);

    void Clear();

    void ThrowIfBlocked();
}
