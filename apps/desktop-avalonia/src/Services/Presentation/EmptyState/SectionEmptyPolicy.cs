using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

/// <summary>何时展示 section 空态面板的策略</summary>
public static class SectionEmptyPolicy
{
    public static bool IsPending(PageDataAvailability availability, bool hasLoadedOnce)
        => !hasLoadedOnce && availability is PageDataAvailability.Loading;

    /// <summary>
    /// 页面已稳定且内容为空时展示空态面板，不留空表壳
    /// 首次拉取进行中不展示；加载由 Busy 表达
    /// 门控文案由 <see cref="SectionEmptyCopy"/> 负责
    /// </summary>
    public static bool Show(bool isContentEmpty, PageDataAvailability availability, bool hasLoadedOnce)
    {
        if (!isContentEmpty)
        {
            return false;
        }

        if (IsPending(availability, hasLoadedOnce))
        {
            return false;
        }

        return true;
    }
}
