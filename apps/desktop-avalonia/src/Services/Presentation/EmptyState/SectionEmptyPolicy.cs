using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

/// <summary>决定区块何时等待首个数据结果</summary>
public static class SectionEmptyPolicy
{
    /// <summary>服务可用且首个数据结果尚未返回时等待</summary>
    public static bool IsPending(
        bool hasLoadedOnce,
        PageDataAvailability availability,
        ConnectionKind connection)
        => !hasLoadedOnce
           && connection == ConnectionKind.Up
           && availability is PageDataAvailability.NotLoaded or PageDataAvailability.Loading;
}
