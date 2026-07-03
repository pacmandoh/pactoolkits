using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class MainWindowDirtyRefreshTests
{
    [Fact]
    public async Task RefreshSucceeded_returns_true_only_for_ready_or_stale_pages()
    {
        var ready = CreatePage(_ => Task.CompletedTask);
        await ready.TestRunReloadCoreAsync();
        Assert.True(WorkspacePageRefresh.RefreshSucceeded(ready));

        var failed = CreatePage(_ => throw new InvalidOperationException("boom"));
        await failed.TestRunReloadCoreAsync();
        Assert.False(WorkspacePageRefresh.RefreshSucceeded(failed));
    }

    [Fact]
    public async Task Batch_refresh_marks_inactive_pages_dirty_and_clears_active_on_success()
    {
        var active = CreatePage(_ => Task.CompletedTask);
        var other = CreatePage(_ => Task.CompletedTask);
        var dirty = new DirtyPageTracker();

        await WorkspaceBatchRefresh.RunAsync(
            [active, other],
            active,
            WorkspacePageRefresh.CanRefreshPage,
            TryRefreshTestPageAsync,
            dirty);

        Assert.False(dirty.IsDirty(active));
        Assert.True(dirty.IsDirty(other));
        Assert.Equal(1, dirty.Count);
    }

    [Fact]
    public async Task Batch_refresh_keeps_active_dirty_when_refresh_fails()
    {
        var active = CreatePage(_ => throw new InvalidOperationException("boom"));
        var dirty = new DirtyPageTracker();

        await WorkspaceBatchRefresh.RunAsync(
            [active],
            active,
            WorkspacePageRefresh.CanRefreshPage,
            TryRefreshTestPageAsync,
            dirty);

        Assert.True(dirty.IsDirty(active));
        Assert.Equal(PageDataAvailability.LoadFailed, active.PageDataAvailability);
    }

    [Fact]
    public void CanRefreshPage_skips_settings_pages()
    {
        var page = new SettingsPageStub();
        Assert.False(WorkspacePageRefresh.CanRefreshPage(page));
    }

    private static async Task<bool> TryRefreshTestPageAsync(AppPageBase page)
    {
        if (page is TestReloadPage testPage)
        {
            await testPage.TestRunReloadCoreAsync();
            return WorkspacePageRefresh.RefreshSucceeded(testPage);
        }

        return await WorkspacePageRefresh.TryRefreshAsync(page);
    }

    private static TestReloadPage CreatePage(Func<CancellationToken, Task> reload)
    {
        var page = new TestReloadPage { ReloadAction = reload };
        page.TestInjectDbServices(new AppPageBaseReloadPipelineTests.FakeDbMonitor { IsConnected = true });
        return page;
    }

    private sealed class TestReloadPage : AppPageBase
    {
        public Func<CancellationToken, Task>? ReloadAction { get; set; }

        public override string DisplayName => "Test";
        public override string Icon => "Activity";
        public override int Index => 99;

        protected override Task ReloadCoreAsync(CancellationToken ct)
            => ReloadAction?.Invoke(ct) ?? Task.CompletedTask;
    }

    private sealed class SettingsPageStub : AppPageBase, ISettingsPage
    {
        public bool HasUnsavedChanges => false;

        public override string DisplayName => "Settings";
        public override string Icon => "Settings";
        public override int Index => 100;

        public Task<bool> TrySaveOrDiscardAllAsync() => Task.FromResult(true);
    }
}
