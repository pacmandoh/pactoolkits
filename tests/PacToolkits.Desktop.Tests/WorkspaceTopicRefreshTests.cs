using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Workspace;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class WorkspaceTopicRefreshTests
{
    [Theory]
    [InlineData("inventory", true)]
    [InlineData("trace_pool", true)]
    [InlineData("trace_txn", true)]
    [InlineData("trace_txn_item", true)]
    [InlineData("", true)]
    [InlineData("drug_index", false)]
    [InlineData("msfx", false)]
    public void Inventory_suppress_window_defers_inventory_topics(string topic, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var suppressUntil = now.AddSeconds(7);

        Assert.Equal(
            expected,
            now < suppressUntil && WorkspaceTopicRefresh.DeferInventory(topic));
    }

    [Fact]
    public void Inventory_suppress_expired_does_not_defer()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(now < now.AddSeconds(-1) && WorkspaceTopicRefresh.DeferInventory("inventory"));
    }

    [Fact]
    public void SkipActiveRefresh_uses_defer_refresh_topic_on_inventory_page()
    {
        var now = DateTimeOffset.UtcNow;
        var active = CreateInventoryDeferPage(suppressUntilUtc: now.AddSeconds(7));

        var (skipInventory, skipDrugIndex) = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");

        Assert.True(skipInventory);
        Assert.False(skipDrugIndex);
    }

    [Fact]
    public void SkipActiveRefresh_defers_inventory_while_stock_edit_enabled()
    {
        var active = CreateInventoryDeferPage(
            suppressUntilUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
            stockEditEnabled: true);

        var (skipInventory, skipDrugIndex) = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");

        Assert.True(skipInventory);
        Assert.False(skipDrugIndex);
    }

    [Fact]
    public void Plan_dirty_marks_can_skip_inventory_when_requested()
    {
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks(
            "inventory",
            skipInventoryPage: true,
            skipDrugIndexPage: false);

        Assert.False(plan.MarkInventory);
        Assert.True(plan.MarkDashboard);
    }

    [Fact]
    public void Plan_dirty_marks_inventory_when_not_deferred()
    {
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks(
            "inventory",
            skipInventoryPage: false,
            skipDrugIndexPage: false);

        Assert.True(plan.MarkInventory);
        Assert.True(plan.MarkDashboard);
    }

    [Fact]
    public async Task Topic_change_harness_during_inventory_suppress_marks_dirty_skips_refresh()
    {
        var active = CreateInventoryDeferPage(DateTimeOffset.UtcNow.AddSeconds(7));
        var activeCalls = 0;
        active.ReloadAction = _ =>
        {
            activeCalls++;
            return Task.CompletedTask;
        };
        var dashboard = CreateRefreshablePage(_ => Task.CompletedTask);
        var (dirtyRefresh, runPending) = CreateDirtyRefresh();

        RunTopicChangeHarness(
            topic: "inventory",
            active,
            dirtyRefresh,
            new Dictionary<Type, AppPageBase>
            {
                [typeof(InventoryOverview)] = active,
                [typeof(Dashboard)] = dashboard,
            });

        await runPending();

        var (deferInventory, _) = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");
        Assert.True(deferInventory);
        Assert.Equal(0, activeCalls);
        Assert.True(dirtyRefresh.IsDirty(active));
        Assert.True(dirtyRefresh.IsDirty(dashboard));
    }

    [Fact]
    public async Task Topic_change_harness_stock_edit_marks_dirty_skips_refresh()
    {
        var active = CreateInventoryDeferPage(
            suppressUntilUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
            stockEditEnabled: true);
        var activeCalls = 0;
        active.ReloadAction = _ =>
        {
            activeCalls++;
            return Task.CompletedTask;
        };
        var dashboard = CreateRefreshablePage(_ => Task.CompletedTask);
        var (dirtyRefresh, runPending) = CreateDirtyRefresh();

        RunTopicChangeHarness(
            topic: "trace_pool",
            active,
            dirtyRefresh,
            new Dictionary<Type, AppPageBase>
            {
                [typeof(InventoryOverview)] = active,
                [typeof(Dashboard)] = dashboard,
            });

        await runPending();

        Assert.Equal(0, activeCalls);
        Assert.True(dirtyRefresh.IsDirty(active));
        Assert.True(active.FlushRefreshAfterStockEdit);
        Assert.True(dirtyRefresh.IsDirty(dashboard));
    }

    [Fact]
    public async Task Topic_change_harness_after_suppress_refreshes_active_inventory()
    {
        var active = CreateInventoryDeferPage(DateTimeOffset.UtcNow.AddSeconds(-1));
        var activeCalls = 0;
        active.ReloadAction = _ =>
        {
            activeCalls++;
            return Task.CompletedTask;
        };
        var dashboard = CreateRefreshablePage(_ => Task.CompletedTask);
        var (dirtyRefresh, runPending) = CreateDirtyRefresh();

        RunTopicChangeHarness(
            topic: "inventory",
            active,
            dirtyRefresh,
            new Dictionary<Type, AppPageBase>
            {
                [typeof(InventoryOverview)] = active,
                [typeof(Dashboard)] = dashboard,
            });

        await runPending();

        var (deferInventory, _) = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");
        Assert.False(deferInventory);
        Assert.Equal(1, activeCalls);
        Assert.False(dirtyRefresh.IsDirty(active));
        Assert.True(dirtyRefresh.IsDirty(dashboard));
    }

    [Fact]
    public async Task Topic_change_harness_trace_pool_during_suppress_marks_dirty_skips_refresh()
    {
        var active = CreateInventoryDeferPage(DateTimeOffset.UtcNow.AddSeconds(7));
        var activeCalls = 0;
        active.ReloadAction = _ =>
        {
            activeCalls++;
            return Task.CompletedTask;
        };
        var dashboard = CreateRefreshablePage(_ => Task.CompletedTask);
        var (dirtyRefresh, runPending) = CreateDirtyRefresh();

        RunTopicChangeHarness(
            topic: "trace_pool",
            active,
            dirtyRefresh,
            new Dictionary<Type, AppPageBase>
            {
                [typeof(InventoryOverview)] = active,
                [typeof(Dashboard)] = dashboard,
            });

        await runPending();

        var (deferInventory, _) = WorkspaceTopicRefresh.SkipActiveRefresh(active, "trace_pool");
        Assert.True(deferInventory);
        Assert.Equal(0, activeCalls);
        Assert.True(dirtyRefresh.IsDirty(active));
        Assert.True(dirtyRefresh.IsDirty(dashboard));
    }

    [Fact]
    public void Drug_index_during_inventory_suppress_still_marks_cascade_pages_dirty()
    {
        var active = CreateInventoryDeferPage(DateTimeOffset.UtcNow.AddSeconds(7));
        var (deferInventory, deferDrugIndex) = WorkspaceTopicRefresh.SkipActiveRefresh(active, "drug_index");

        Assert.False(deferInventory);
        Assert.False(deferDrugIndex);

        // MainWindow 始终按 topic 打 dirty，不再把 defer 传进 PlanDirtyMarks
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks(
            "drug_index",
            skipInventoryPage: false,
            skipDrugIndexPage: false);

        Assert.False(plan.MarkInventory);
        Assert.True(plan.MarkDashboard);
        Assert.True(plan.MarkScanCode);
    }

    private static void RunTopicChangeHarness(
        string topic,
        AppPageBase active,
        WorkspaceDirtyRefresh dirtyRefresh,
        IReadOnlyDictionary<Type, AppPageBase> pagesByType)
    {
        var (deferInventoryRefresh, deferDrugIndexRefresh) =
            WorkspaceTopicRefresh.SkipActiveRefresh(active, topic);

        // 与 MainWindow.OnTopicChanged 一致：dirty 与 defer 解耦
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks(
            topic,
            skipInventoryPage: false,
            skipDrugIndexPage: false);

        WorkspaceTopicRefresh.ApplyDirtyPlan(
            plan,
            invalidateDrugCatalog: null,
            type => pagesByType.TryGetValue(type, out var page) ? page : null,
            pagesByType.Values,
            WorkspacePageRefresh.CanRefreshPage,
            dirtyRefresh.Mark);

        if (!deferInventoryRefresh && !deferDrugIndexRefresh)
        {
            dirtyRefresh.TryRefreshIfDirty(active, () => true);
        }
    }

    private static (WorkspaceDirtyRefresh Refresh, Func<Task> RunPending) CreateDirtyRefresh()
    {
        Func<Task>? pending = null;
        var refresh = new WorkspaceDirtyRefresh();
        refresh.Configure(() => true, work => pending = work);
        return (refresh, () => pending?.Invoke() ?? Task.CompletedTask);
    }

    private static InventoryDeferPageStub CreateInventoryDeferPage(
        DateTimeOffset suppressUntilUtc,
        bool stockEditEnabled = false)
    {
        var page = new InventoryDeferPageStub(suppressUntilUtc, stockEditEnabled);
        page.TestInjectDbServices(new AppPageBaseReloadPipelineTests.FakeDbMonitor { IsConnected = true });
        return page;
    }

    private static RefreshablePageStub CreateRefreshablePage(Func<CancellationToken, Task> reload)
    {
        var page = new RefreshablePageStub(reload);
        page.TestInjectDbServices(new AppPageBaseReloadPipelineTests.FakeDbMonitor { IsConnected = true });
        return page;
    }

    private sealed class InventoryDeferPageStub : AppPageBase, IInventoryRefreshPage
    {
        private readonly AsyncRelayCommand _refresh;
        private readonly DateTimeOffset _suppressUntilUtc;
        private readonly bool _stockEditEnabled;

        public InventoryDeferPageStub(DateTimeOffset suppressUntilUtc, bool stockEditEnabled)
        {
            _suppressUntilUtc = suppressUntilUtc;
            _stockEditEnabled = stockEditEnabled;
            _refresh = new AsyncRelayCommand(() => TestRunReloadCoreAsync());
        }

        public Func<CancellationToken, Task>? ReloadAction { get; set; }

        public bool FlushRefreshAfterStockEdit { get; private set; }

        public bool DeferRefreshTopic(string? topic)
        {
            if (!WorkspaceTopicRefresh.DeferInventory(topic))
            {
                return false;
            }

            if (_stockEditEnabled)
            {
                FlushRefreshAfterStockEdit = true;
                return true;
            }

            return DateTimeOffset.UtcNow < _suppressUntilUtc;
        }

        public override string DisplayName => "Inventory";
        public override string Icon => "Package";
        public override int Index => 1;
        public override System.Windows.Input.ICommand? RefreshCommand => _refresh;

        protected override Task ReloadCoreAsync(CancellationToken ct)
            => ReloadAction?.Invoke(ct) ?? Task.CompletedTask;
    }

    private sealed class RefreshablePageStub : AppPageBase
    {
        private readonly AsyncRelayCommand _refresh;
        private readonly Func<CancellationToken, Task> _reload;

        public RefreshablePageStub(Func<CancellationToken, Task> reload)
        {
            _reload = reload;
            _refresh = new AsyncRelayCommand(() => TestRunReloadCoreAsync());
        }

        public override string DisplayName => "Refreshable";
        public override string Icon => "Activity";
        public override int Index => 98;
        public override System.Windows.Input.ICommand? RefreshCommand => _refresh;

        protected override Task ReloadCoreAsync(CancellationToken ct) => _reload(ct);
    }
}
