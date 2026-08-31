using System.Collections.Generic;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;

/// <summary>
/// 跟踪存在待处理数据变更的工作区页面
///
/// 刷新成功后仅当途中未再 Mark 才清除
/// stamp 单调递增，Clear 只去掉脏标记
/// </summary>
public sealed class DirtyPageTracker
{
    private readonly object _gate = new();
    private readonly HashSet<AppPageBase> _pages = new();
    private readonly Dictionary<AppPageBase, int> _stamps = new();

    public void Mark(AppPageBase page)
    {
        lock (_gate)
        {
            _stamps.TryGetValue(page, out var n);
            _stamps[page] = n + 1;
            _pages.Add(page);
        }
    }

    public int Stamp(AppPageBase page)
    {
        lock (_gate)
        {
            return _pages.Contains(page) && _stamps.TryGetValue(page, out var n) ? n : 0;
        }
    }

    public bool IsDirty(AppPageBase page)
    {
        lock (_gate)
        {
            return _pages.Contains(page);
        }
    }

    public void Clear(AppPageBase page)
    {
        lock (_gate)
        {
            _pages.Remove(page);
        }
    }

    public void ClearIf(AppPageBase page, int stamp)
    {
        lock (_gate)
        {
            if (stamp == 0
                || !_pages.Contains(page)
                || !_stamps.TryGetValue(page, out var current)
                || current != stamp)
            {
                return;
            }

            _pages.Remove(page);
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _pages.Count;
            }
        }
    }
}
