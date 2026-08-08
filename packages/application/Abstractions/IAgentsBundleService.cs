using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// Agents 对 Desktop 的 SemVer 配套校验（Agents 包 min/maxDesktop × 当前 Desktop 版本）
///
/// 区间由调用方从 Agents 安装树 manifest 传入；纯比较，不操作 Host、不读磁盘
/// 缺 Desktop 版本或缺成对区间时拒绝
/// </summary>
public interface IAgentsBundleService
{
    AgentsBundleResult Validate(
        string? desktopVersion,
        string? minDesktop,
        string? maxDesktop);
}
