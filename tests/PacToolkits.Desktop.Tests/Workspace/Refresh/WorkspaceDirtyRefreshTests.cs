using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class WorkspaceDirtyRefreshTests
{
    [Fact]
    public void TryRefreshIfDirty_skips_when_page_is_not_refreshable()
    {
        var (refresh, _) = CreateRefresh();
        var page = new NonRefreshablePageStub();
        refresh.Mark(page);

        refresh.TryRefreshIfDirty(page);

        Assert.True(refresh.IsDirty(page));
    }

    [Fact]
    public async Task TryRefreshIfDirty_clears_dirty_after_successful_refresh()
    {
        var (refresh, runPending) = CreateRefresh();
        var calls = 0;
        var page = new RefreshablePageStub(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        refresh.Mark(page);

        refresh.TryRefreshIfDirty(page);
        await runPending();

        Assert.False(refresh.IsDirty(page));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TryRefreshIfDirty_keeps_dirty_when_still_active_guard_fails()
    {
        var (refresh, runPending) = CreateRefresh();
        var calls = 0;
        var page = new RefreshablePageStub(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        refresh.Mark(page);

        refresh.TryRefreshIfDirty(page, () => false);
        await runPending();

        Assert.True(refresh.IsDirty(page));
        Assert.Equal(0, calls);
    }

    private static (WorkspaceDirtyRefresh Refresh, Func<Task> RunPending) CreateRefresh()
    {
        Func<Task>? pending = null;
        var refresh = new WorkspaceDirtyRefresh();
        refresh.Configure(() => true, work => pending = work);
        return (refresh, () => pending?.Invoke() ?? Task.CompletedTask);
    }

    private sealed class RefreshablePageStub : AppPageBase
    {
        private readonly AsyncRelayCommand _refresh;
        private readonly Func<CancellationToken, Task> _reload;

        public RefreshablePageStub(Func<CancellationToken, Task> reload)
        {
            _reload = reload;
            TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());
            _refresh = new AsyncRelayCommand(() => TestRunReloadCoreAsync());
        }

        public override string DisplayName => "Refreshable";
        public override string Icon => "Activity";
        public override int Index => 98;
        public override ICommand? RefreshCommand => _refresh;

        protected override Task ReloadCoreAsync(CancellationToken ct) => _reload(ct);
    }

    private sealed class NonRefreshablePageStub : AppPageBase
    {
        public override string DisplayName => "Hidden";
        public override string Icon => "Activity";
        public override int Index => 97;
        public override ICommand? RefreshCommand => null;

        protected override Task ReloadCoreAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
