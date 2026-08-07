namespace PacToolkits.Application.DTOs;

/// <summary>Agents 包与当前 Desktop 的 SemVer 配套拒绝原因</summary>
public enum AgentsBundleDenyKind
{
    None = 0,
    IncompleteRange,
    Unparsable,
    BelowMin,
    AboveMax,
}

/// <summary>minDesktop–maxDesktop 闭区间配套判定结果</summary>
public sealed record AgentsBundleResult(
    bool Ok,
    AgentsBundleDenyKind DenyKind,
    string Message);
