using System.Collections.Generic;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace;

/// <summary>跟踪存在待处理数据变更的工作区页面</summary>
public sealed class DirtyPageTracker
{
    private readonly object _gate = new();
    private readonly HashSet<AppPageBase> _pages = new();

    public void Mark(AppPageBase page)
    {
        lock (_gate)
        {
            _pages.Add(page);
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
