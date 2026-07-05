using System;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

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

    public void TryRefreshIfDirty(AppPageBase page, Func<bool>? stillActive = null)
    {
        if (_canWorkspaceRefresh?.Invoke() != true)
        {
            return;
        }

        if (!_dirty.IsDirty(page) || !WorkspacePageRefresh.CanRefreshPage(page))
        {
            return;
        }

        _schedule?.Invoke(() => RunRefreshAsync(page, stillActive));
    }

    private async Task RunRefreshAsync(AppPageBase page, Func<bool>? stillActive)
    {
        try
        {
            if (stillActive?.Invoke() == false)
            {
                return;
            }

            if (await WorkspacePageRefresh.TryRefreshAsync(page).ConfigureAwait(true))
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
