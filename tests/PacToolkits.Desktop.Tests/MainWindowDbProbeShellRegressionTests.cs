namespace PacToolkits.Desktop.Tests;

/// <summary>
/// Regression guard: DB probe must not disable the sidebar shell (causes full-nav flicker).
/// Probe debounce, busy icons, and refresh guard stay on dedicated bindings/commands.
/// </summary>
public sealed class MainWindowDbProbeShellRegressionTests
{
    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(ResolveRepoPath(relativePath));

    private static string ResolveRepoPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file: {relativePath}");
    }

    private static string ReadMainWindowViewModelSource()
    {
        var vmDir = Path.GetDirectoryName(
            ResolveRepoPath("apps/desktop-avalonia/src/ViewModels/AppPageBase.cs"))!;

        var partials = Directory.GetFiles(vmDir, "MainWindow*.cs")
            .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();

        if (partials.Length == 0)
        {
            throw new FileNotFoundException($"Could not locate MainWindow*.cs under {vmDir}.");
        }

        return string.Join(Environment.NewLine, partials);
    }

    private static string ExtractSidebarBlock(string mainWindowAxaml)
    {
        const string open = "<shad:Sidebar";
        const string close = "</shad:Sidebar>";

        var start = mainWindowAxaml.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.axaml must contain shad:Sidebar.");

        var end = mainWindowAxaml.IndexOf(close, start, StringComparison.Ordinal);
        Assert.True(end > start, "MainWindow.axaml must close shad:Sidebar.");

        return mainWindowAxaml[start..(end + close.Length)];
    }

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    [Fact]
    public void Sidebar_does_not_bind_IsEnabled_to_IsDbProbeRunning()
    {
        var axaml = ReadRepoFile("apps/desktop-avalonia/src/Views/MainWindow.axaml");
        var sidebar = ExtractSidebarBlock(axaml);

        Assert.DoesNotContain("IsDbProbeRunning", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding !IsDbProbeRunning}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReconnectDbCommand_still_debounces_via_CanProbeDb()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("[RelayCommand(CanExecute = nameof(CanProbeDb))]", source, StringComparison.Ordinal);
        Assert.Contains("public bool CanProbeDb() => !IsDbProbeRunning;", source, StringComparison.Ordinal);
        Assert.Contains("TryReconnectDbCommand.NotifyCanExecuteChanged();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Db_probe_buttons_use_ButtonAssist_ShowProgress()
    {
        var axaml = ReadRepoFile("apps/desktop-avalonia/src/Views/MainWindow.axaml");

        Assert.Equal(2, CountOccurrences(axaml, "TryReconnectDbCommand"));
        Assert.Equal(2, CountOccurrences(axaml, "shad:ButtonAssist.ShowProgress=\"{Binding IsDbProbeRunning}\""));
        Assert.DoesNotContain("BusyCircle", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IsDbProbeRunning_still_drives_shell_db_status_text()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.DoesNotContain("ShowDbBusyIcon", source, StringComparison.Ordinal);
        Assert.Contains("IsDbProbeRunning ? \"数据库：检测中…\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_button_templates_support_ButtonAssist_ShowProgress()
    {
        var styles = ReadRepoFile("apps/desktop-avalonia/src/Styles/Components/ShellStyles.axaml");

        Assert.Contains("Button.WindowControlsButton", styles, StringComparison.Ordinal);
        Assert.Contains("Button.StatusBarItem", styles, StringComparison.Ordinal);
        Assert.Contains("shad:BooleanConverters.ToLoading", styles, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(styles, "shad:BooleanConverters.ToLoading"));
    }

    [Fact]
    public void RefreshActivePage_still_blocks_while_db_probe_or_startup_init_runs()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("CanWorkspaceRefresh()", source, StringComparison.Ordinal);
        Assert.Contains("IsDbProbeRunning", source, StringComparison.Ordinal);
        Assert.Contains("IsDbInitCompleted", source, StringComparison.Ordinal);
        Assert.Contains("数据库初始化进行中，请稍候", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MsfxLink_reload_core_calls_auto_board_body_without_nested_page_reload()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/MsfxLink.cs");

        Assert.Contains("0 => RefreshAutoBoardAsync(ct)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("0 => RefreshAutoBoardAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_silent_reconcile_rebuilds_when_trace_code_order_changes()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/InventoryOverview.DetailOps.cs");

        Assert.Contains("StockRowsMatchServerOrder", source, StringComparison.Ordinal);
        Assert.Contains("StockRows.ReplaceAll(rebuiltRows)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_topic_marks_scan_code_dirty_for_catalog_refresh()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("case \"drug_index\":", source, StringComparison.Ordinal);
        Assert.Contains("MarkDirtyByType<ScanCode>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DrugIndex_defers_only_cascade_topics_and_supports_silent_reconcile()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.Reconcile.cs");

        Assert.Contains("public bool DeferRefreshTopic(string? topic)", source, StringComparison.Ordinal);
        Assert.Contains("trace_pool", source, StringComparison.Ordinal);
        Assert.Contains("ApplySilentReconcile", source, StringComparison.Ordinal);
        Assert.Contains("ShouldSilentReconcile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("drug_index", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_workspace_refresh_blocks_while_db_probe_or_startup_init_runs()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("if (!CanWorkspaceRefresh())", source, StringComparison.Ordinal);
        Assert.Contains("RunWorkspaceRefreshAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryRefreshDirtyActivePage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_reload_pipeline_still_waits_for_startup_db_init_independently_of_sidebar()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/AppPageBase.cs");

        Assert.Contains("WaitForStartupDbInitCompletedAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsDbInitCompleted", source, StringComparison.Ordinal);
    }
}
