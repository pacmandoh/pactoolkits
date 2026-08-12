using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;

/// <summary>
/// 跟踪工作区页面的待刷新状态，并在页面重新激活时执行刷新
/// </summary>
public sealed class WorkspaceDirtyRefresh
{
    private readonly DirtyPageTracker _dirty = new();
    private Func<bool>? _canWorkspaceRefresh;
    private Action<Func<Task>>? _schedule;
    private Action<AppPageBase, Exception>? _logRefreshFail;

    public DirtyPageTracker Dirty => _dirty;

    public void Configure(
        Func<bool> canWorkspaceRefresh,
        Action<Func<Task>> schedule,
        Action<AppPageBase, Exception>? logRefreshFail = null)
    {
        _canWorkspaceRefresh = canWorkspaceRefresh;
        _schedule = schedule;
        _logRefreshFail = logRefreshFail;
    }

    public void Mark(AppPageBase page) => _dirty.Mark(page);

    public bool IsDirty(AppPageBase page) => _dirty.IsDirty(page);

    public void Clear(AppPageBase page) => _dirty.Clear(page);

    public Task RunAsync(IEnumerable<AppPageBase> pages, AppPageBase? active)
        => RunAsync(
            pages,
            active,
            WorkspacePageRefresh.CanRefreshPage,
            page => WorkspacePageRefresh.TryRefreshAsync(page, silent: true));

    internal async Task RunAsync(
        IEnumerable<AppPageBase> pages,
        AppPageBase? active,
        Func<AppPageBase, bool> canRefresh,
        Func<AppPageBase, Task<bool>> tryRefresh)
    {
        foreach (var page in pages)
        {
            if (canRefresh(page) && !ReferenceEquals(page, active))
            {
                _dirty.Mark(page);
            }
        }

        if (active is null || !canRefresh(active))
        {
            return;
        }

        if (await tryRefresh(active).ConfigureAwait(true))
        {
            _dirty.Clear(active);
        }
        else
        {
            _dirty.Mark(active);
        }
    }

    public void TryRefreshIfDirty(AppPageBase page, Func<bool>? stillActive = null, bool silent = true)
    {
        if (_canWorkspaceRefresh?.Invoke() != true)
        {
            return;
        }

        if (!_dirty.IsDirty(page) || !WorkspacePageRefresh.CanRefreshPage(page))
        {
            return;
        }

        _schedule?.Invoke(() => RunRefreshAsync(page, stillActive, silent));
    }

    private async Task RunRefreshAsync(AppPageBase page, Func<bool>? stillActive, bool silent)
    {
        try
        {
            if (stillActive?.Invoke() == false)
            {
                return;
            }

            if (await WorkspacePageRefresh.TryRefreshAsync(page, silent).ConfigureAwait(true))
            {
                _dirty.Clear(page);
            }
        }
        catch (Exception ex)
        {
            _logRefreshFail?.Invoke(page, ex);
        }
    }
}
