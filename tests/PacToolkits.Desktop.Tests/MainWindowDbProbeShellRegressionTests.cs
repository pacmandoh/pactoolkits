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

    private static string ExtractSwitchCaseBlock(string source, string caseLabel)
    {
        var needle = $"case \"{caseLabel}\":";
        var start = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected switch case \"{caseLabel}\".");

        var breakIndex = source.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.True(breakIndex > start, $"Expected break after case \"{caseLabel}\".");

        return source[start..(breakIndex + "break;".Length)];
    }

    private static string ExtractMethodBlock(string source, string methodName)
    {
        var needle = $"void {methodName}";
        var start = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected method {methodName}.");

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace > start, $"Expected opening brace for {methodName}.");

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method block for {methodName}.");
    }

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

        Assert.Contains("InventoryStockOrderPolicy.MatchTraceCodeOrder", source, StringComparison.Ordinal);
        Assert.Contains("StockRows.ReplaceAll(rebuiltRows)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_silent_reconcile_cancels_and_checks_epoch_on_reload()
    {
        var overview = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/InventoryOverview.cs");
        var detailOps = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/InventoryOverview.DetailOps.cs");

        Assert.Contains("BeginStockReload()", overview, StringComparison.Ordinal);
        Assert.Contains("CancelSilentReconcile();", overview, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _detailStockEpoch)", overview, StringComparison.Ordinal);
        Assert.Contains("epoch != Volatile.Read(ref _detailStockEpoch)", detailOps, StringComparison.Ordinal);
        Assert.Contains("IsPageReloadActive", detailOps, StringComparison.Ordinal);
        Assert.Contains("if (IsStockEditEnabled)", detailOps, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanCode_reload_reschedules_pool_check_when_input_present()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/ScanCode.cs");

        Assert.Contains("ReschedulePoolCheckAfterReload()", source, StringComparison.Ordinal);
        Assert.Contains("_lastPoolCheckKey = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("RecalcCodeStats(TraceCodesText)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_topic_marks_scan_code_dirty_for_catalog_refresh()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("case \"drug_index\":", source, StringComparison.Ordinal);
        Assert.Contains("MarkDirtyByType<ScanCode>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_topic_skips_self_mark_when_active_page_defers_refresh()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");
        var drugIndexCase = ExtractSwitchCaseBlock(source, "drug_index");

        Assert.Contains("skipDrugIndexPage", source, StringComparison.Ordinal);
        Assert.Contains("if (!skipDrugIndexPage)", drugIndexCase, StringComparison.Ordinal);
    }

    [Fact]
    public void Msfx_topic_marks_only_msfx_link_dirty()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");
        var msfxCase = ExtractSwitchCaseBlock(source, "msfx");

        Assert.Contains("MarkDirtyByType<MsfxLink>()", msfxCase, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDirtyByType<Dashboard>()", msfxCase, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDirtyByType<DrugIndex>()", msfxCase, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDirtyByType<ScanCode>()", msfxCase, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDirtyByType<InventoryOverview>()", msfxCase, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var page in WorkspacePages)", msfxCase, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkDirtyByType_marks_page_without_refresh_command_gate()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");

        Assert.Contains("private void MarkDirtyByType<TPage>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CanRefreshPage(page)", ExtractMethodBlock(source, "MarkDirtyByType"));
    }

    [Fact]
    public void MsfxLink_refreshes_dirty_auto_board_when_returning_to_tab_zero()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/MsfxLink.cs");

        Assert.Contains("partial void OnSelectedTabIndexChanged(int value)", source, StringComparison.Ordinal);
        Assert.Contains("_dirtyRefresh.TryRefreshIfDirty(this)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_watermark_topic_still_marks_all_refreshable_pages_dirty()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");

        Assert.Contains("default:", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var page in WorkspacePages)", source, StringComparison.Ordinal);
        Assert.Contains("MarkPageDirty(page)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DrugIndex_defers_only_cascade_topics_and_supports_silent_reconcile()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.Reconcile.cs");

        Assert.Contains("WatermarkActiveRefreshDeferPolicy.ShouldDeferDrugIndexActiveRefresh", source, StringComparison.Ordinal);
        Assert.Contains("ApplySilentReconcile", source, StringComparison.Ordinal);
        Assert.Contains("ShouldSilentReconcile", source, StringComparison.Ordinal);
        Assert.Contains("HasPendingChanges", source, StringComparison.Ordinal);
        Assert.Contains("_pendingReselectKey.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("ApplyCleanRefresh", source, StringComparison.Ordinal);
        Assert.Contains("ClearListFocus", source, StringComparison.Ordinal);
        Assert.Contains("DetachListSelection()", source, StringComparison.Ordinal);
        Assert.Contains("FocusSavedRow", source, StringComparison.Ordinal);
        Assert.Contains("QueueReselect", source, StringComparison.Ordinal);
        Assert.DoesNotContain("drug_index", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_workspace_refresh_blocks_while_db_probe_or_startup_init_runs()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("if (!CanWorkspaceRefresh())", source, StringComparison.Ordinal);
        Assert.Contains("RunWorkspaceRefreshAsync", source, StringComparison.Ordinal);
        Assert.Contains("WorkspaceBatchRefresh.RunAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryRefreshDirtyActivePage", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.cs", "drug_index.reload.fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.cs", "dashboard.reload.fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/MsfxLink.AutoBoard.cs", "msfx.audit.snapshot.refresh_fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/MsfxLink.Subcode.cs", "msfx.subcode.query_fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/MsfxLink.Upout.cs", "msfx.upout.query_fail")]
    public void Reload_failure_paths_rethrow_for_pipeline_load_failed(string relativePath, string logEvent)
    {
        var source = ReadRepoFile(relativePath);
        var eventIndex = source.IndexOf(logEvent, StringComparison.Ordinal);
        Assert.True(eventIndex >= 0, $"Expected log event {logEvent} in {relativePath}.");

        var tail = source[eventIndex..Math.Min(source.Length, eventIndex + 700)];
        Assert.Contains("throw;", tail, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_reload_pipeline_still_waits_for_startup_db_init_independently_of_sidebar()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/AppPageBase.cs");

        Assert.Contains("WaitForStartupDbInitCompletedAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsDbInitCompleted", source, StringComparison.Ordinal);
    }
}
