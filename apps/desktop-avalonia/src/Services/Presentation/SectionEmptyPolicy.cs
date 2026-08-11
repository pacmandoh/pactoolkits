using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>何时展示 section 空态面板的策略</summary>
public static class SectionEmptyPolicy
{
    public static bool IsPending(PageDataAvailability availability, bool hasLoadedOnce)
        => !hasLoadedOnce
           && availability is PageDataAvailability.NotLoaded
               or PageDataAvailability.AwaitingDatabase
               or PageDataAvailability.AwaitingService
               or PageDataAvailability.Loading;

    /// <summary>
    /// 页面已稳定且内容为空时，优先展示 section 空态面板，而不是空 grid 壳
    /// 首次 fetch 进行中仍保留 section chrome，由面板 busy 态表达加载
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
