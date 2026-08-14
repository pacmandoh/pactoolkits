using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
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

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");

        Assert.True(defer.Inventory);
        Assert.True(defer.SkipImmediate);
    }

    [Fact]
    public void SkipActiveRefresh_defers_inventory_while_stock_edit_enabled()
    {
        var active = CreateInventoryDeferPage(
            suppressUntilUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
            stockEditEnabled: true);

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");

        Assert.True(defer.Inventory);
        Assert.True(defer.SkipImmediate);
    }

    [Fact]
    public void Plan_dirty_marks_inventory_topic()
    {
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks("inventory");

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

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");
        Assert.True(defer.Inventory);
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

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "inventory");
        Assert.False(defer.Inventory);
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

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "trace_pool");
        Assert.True(defer.Inventory);
        Assert.Equal(0, activeCalls);
        Assert.True(dirtyRefresh.IsDirty(active));
        Assert.True(dirtyRefresh.IsDirty(dashboard));
    }

    [Fact]
    public void Drug_index_during_inventory_suppress_still_marks_cascade_pages_dirty()
    {
        var active = CreateInventoryDeferPage(DateTimeOffset.UtcNow.AddSeconds(7));
        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "drug_index");

        Assert.False(defer.Inventory);
        Assert.False(defer.SkipImmediate);

        // MainWindow 始终按 topic 打 dirty，与 defer 解耦
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks("drug_index");

        Assert.False(plan.MarkInventory);
        Assert.True(plan.MarkDashboard);
        Assert.True(plan.MarkScanCode);
    }

    [Fact]
    public void SkipActiveRefresh_defers_msfx_while_manual_write_active()
    {
        var active = new MsfxDeferPageStub(manualWriteActive: true);
        active.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "msfx");

        Assert.False(defer.Inventory);
        Assert.True(defer.SkipImmediate);
    }

    [Fact]
    public void SkipActiveRefresh_does_not_defer_msfx_when_idle()
    {
        var active = new MsfxDeferPageStub(manualWriteActive: false);
        active.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, "msfx");

        Assert.False(defer.Inventory);
        Assert.False(defer.SkipImmediate);
    }

    private static void RunTopicChangeHarness(
        string topic,
        AppPageBase active,
        WorkspaceDirtyRefresh dirtyRefresh,
        IReadOnlyDictionary<Type, AppPageBase> pagesByType)
    {
        var defer = WorkspaceTopicRefresh.SkipActiveRefresh(active, topic);

        // 与 MainWindow.OnTopicChanged 一致：dirty 与 defer 解耦
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks(topic);

        WorkspaceTopicRefresh.ApplyDirtyPlan(
            plan,
            invalidateDrugCatalog: null,
            type => pagesByType.TryGetValue(type, out var page) ? page : null,
            pagesByType.Values,
            WorkspacePageRefresh.CanRefreshPage,
            dirtyRefresh.Mark);

        if (!defer.SkipImmediate)
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
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());
        return page;
    }

    private static RefreshablePageStub CreateRefreshablePage(Func<CancellationToken, Task> reload)
    {
        var page = new RefreshablePageStub(reload);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());
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

    private sealed class MsfxDeferPageStub : AppPageBase, IMsfxRefreshPage
    {
        private readonly AsyncRelayCommand _refresh;
        private readonly bool _manualWriteActive;

        public MsfxDeferPageStub(bool manualWriteActive)
        {
            _manualWriteActive = manualWriteActive;
            _refresh = new AsyncRelayCommand(() => TestRunReloadCoreAsync());
        }

        public bool DeferRefreshTopic(string? topic)
            => WorkspaceTopicRefresh.DeferMsfx(topic) && _manualWriteActive;

        public override string DisplayName => "Msfx";
        public override string Icon => "Cloud";
        public override int Index => 5;
        public override System.Windows.Input.ICommand? RefreshCommand => _refresh;

        protected override Task ReloadCoreAsync(CancellationToken ct) => Task.CompletedTask;
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
