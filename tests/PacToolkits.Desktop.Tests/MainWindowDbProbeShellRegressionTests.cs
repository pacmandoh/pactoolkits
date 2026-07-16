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

    private static string ReadTopicRefreshSource()
        => ReadRepoFile("apps/desktop-avalonia/src/Services/Workspace/WorkspaceTopicRefresh.cs");

    private static string ExtractPlanArm(string source, string topicKey)
    {
        var needle = source.Contains($"\"{topicKey}\" =>", StringComparison.Ordinal)
            ? $"\"{topicKey}\" =>"
            : topicKey == "_"
                ? "_ =>"
                : throw new InvalidOperationException($"Unknown plan arm key: {topicKey}");

        var start = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected plan arm \"{topicKey}\".");

        var nextArm = source.IndexOf("\n            _ =>", start + needle.Length, StringComparison.Ordinal);
        if (nextArm < 0)
        {
            nextArm = source.IndexOf("\n        };", start, StringComparison.Ordinal);
        }

        Assert.True(nextArm > start, $"Expected end of plan arm \"{topicKey}\".");
        return source[start..nextArm];
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
        var titleBarStyles = ReadRepoFile("apps/desktop-avalonia/src/Styles/Components/TitleBarStyles.axaml");
        var statusBarStyles = ReadRepoFile("apps/desktop-avalonia/src/Styles/Components/StatusBarStyles.axaml");

        Assert.Contains("Button.WindowControlsButton", titleBarStyles, StringComparison.Ordinal);
        Assert.Contains("Button.StatusBarItem", statusBarStyles, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(titleBarStyles, "shad:BooleanConverters.ToLoading"));
        Assert.Equal(1, CountOccurrences(statusBarStyles, "shad:BooleanConverters.ToLoading"));
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

        Assert.Contains("HasSameTraceCodeOrder(StockRows, rebuiltRows)", source, StringComparison.Ordinal);
        Assert.Contains("StockRows.ReplaceAll(rebuiltRows)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_silent_reconcile_cancels_and_checks_epoch_on_reload()
    {
        var overview = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/InventoryOverview.cs");
        var detailOps = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/InventoryOverview.DetailOps.cs");
        var policy = ReadRepoFile("apps/desktop-avalonia/src/Services/Workspace/InventorySilentReconcilePolicy.cs");

        Assert.Contains("BeginStockReload()", overview, StringComparison.Ordinal);
        Assert.Contains("CancelSilentReconcile();", overview, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _detailStockEpoch)", overview, StringComparison.Ordinal);
        Assert.Contains("InventorySilentReconcilePolicy.CanApply", detailOps, StringComparison.Ordinal);
        Assert.Contains("capturedEpoch != currentEpoch", policy, StringComparison.Ordinal);
        Assert.Contains("isStockEditEnabled", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanCode_reload_reschedules_pool_check_when_input_present()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/ScanCode.cs");

        Assert.Contains("ReschedulePoolCheckAfterReload()", source, StringComparison.Ordinal);
        Assert.Contains("_lastCompletedPoolCheckKey = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("PoolCheckCandidates", source, StringComparison.Ordinal);
        Assert.Contains("RecalcCodeStats(TraceCodesText)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DrugIndex_repo_disposes_update_reader_before_conflict_lookup()
    {
        var source = ReadRepoFile("packages/infrastructure/Repositories/DrugIndex.cs");
        const string marker = "await using (var updated = await update.ExecuteReaderAsync(token))";

        Assert.Contains(marker, source, StringComparison.Ordinal);
        Assert.Contains("var latest = await GetByKeyAsync(conn, dto.DrugId, dto.Spec, token);", source, StringComparison.Ordinal);

        var updateBlockStart = source.IndexOf(marker, StringComparison.Ordinal);
        var lookupIndex = source.IndexOf("var latest = await GetByKeyAsync", updateBlockStart, StringComparison.Ordinal);
        var blockClose = source.IndexOf('}', updateBlockStart);
        Assert.True(lookupIndex > blockClose, "Conflict lookup must run after the update reader scope is disposed.");
    }

    [Fact]
    public void Drug_index_topic_triggers_active_watermark_refresh()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");

        Assert.Contains("IsDrugIndexTopic(topic)", source, StringComparison.Ordinal);
        Assert.Contains("RefreshDrugIndexFromWatermarkAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReloadFromWatermarkAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_topic_marks_scan_code_dirty_for_catalog_refresh()
    {
        var source = ReadTopicRefreshSource();

        Assert.Contains("\"drug_index\" =>", source, StringComparison.Ordinal);
        Assert.Contains("MarkScanCode: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_topic_skips_self_mark_when_active_page_defers_refresh()
    {
        var topicRefresh = ReadTopicRefreshSource();
        var autoRefresh = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");
        var drugIndexArm = ExtractPlanArm(topicRefresh, "drug_index");

        Assert.Contains("skipDrugIndexPage", autoRefresh, StringComparison.Ordinal);
        Assert.Contains("MarkDrugIndex: !skipDrugIndexPage", drugIndexArm, StringComparison.Ordinal);
    }

    [Fact]
    public void Msfx_topic_marks_only_msfx_link_dirty()
    {
        var source = ReadTopicRefreshSource();
        var msfxArm = ExtractPlanArm(source, "msfx");

        Assert.Contains("MarkMsfx: true", msfxArm, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDashboard: true", msfxArm, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDrugIndex: true", msfxArm, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkScanCode: true", msfxArm, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkInventory: true", msfxArm, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkAllRefreshable: true", msfxArm, StringComparison.Ordinal);
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
    public void Dashboard_forces_tab_page_reload_when_workspace_dirty()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.cs");

        Assert.Contains("WorkspaceDirtyRefresh dirtyRefresh", source, StringComparison.Ordinal);
        Assert.Contains("QueueTabPageReload(force: _dirtyRefresh.IsDirty(this))", source, StringComparison.Ordinal);
        Assert.Contains("DrugOptions.Count == 0 || _dirtyRefresh.IsDirty(this)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_schema_refresh_survives_page_deactivate()
    {
        var settings = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/Settings.cs");

        Assert.Contains("bindPageLifetime: false", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_watermark_invalidates_lookup_catalog()
    {
        var topicRefresh = ReadTopicRefreshSource();
        var autoRefresh = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");

        Assert.Contains("\"drug_index\" =>", topicRefresh, StringComparison.Ordinal);
        Assert.Contains("InvalidateDrugCatalog: true", topicRefresh, StringComparison.Ordinal);
        Assert.Contains("_lookup.InvalidateDrugCatalog()", autoRefresh, StringComparison.Ordinal);
    }

    [Fact]
    public void DrugIndex_notifies_lookup_catalog_after_writes()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.cs");

        Assert.Contains("private void NotifyDrugCatalogChanged()", source, StringComparison.Ordinal);
        Assert.Contains("_lookup.InvalidateDrugCatalog();", source, StringComparison.Ordinal);
        Assert.Contains("_dashboard.ReloadAfterDrugIndexChange();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_watermark_topic_still_marks_all_refreshable_pages_dirty()
    {
        var topicRefresh = ReadTopicRefreshSource();
        var defaultArm = ExtractPlanArm(topicRefresh, "_");

        Assert.Contains("MarkAllRefreshable: true", defaultArm, StringComparison.Ordinal);
        Assert.Contains("foreach (var page in workspacePages)", topicRefresh, StringComparison.Ordinal);
    }

    [Fact]
    public void DrugIndex_save_conflict_offers_discard_or_force_save_dialog()
    {
        var drugIndex = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.cs");
        var reconcile = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.Reconcile.cs");
        var alert = ReadRepoFile("apps/desktop-avalonia/src/Services/Infrastructure/Dialogs/Alert.cs");
        var session = ReadRepoFile("apps/desktop-avalonia/src/Services/Infrastructure/Dialogs/AlertSession.cs");
        var block = ExtractSaveConflictBlock(drugIndex);

        Assert.Contains("HandleSaveConflictAsync", drugIndex, StringComparison.Ordinal);
        Assert.Contains("AlertBuilder<bool?>.Create", block, StringComparison.Ordinal);
        Assert.Contains(".SaveConflict(", block, StringComparison.Ordinal);
        Assert.Contains(".Close(null)", alert, StringComparison.Ordinal);
        Assert.Contains("BindSimpleDialogDismiss", session, StringComparison.Ordinal);
        Assert.Contains("choice is null", block, StringComparison.Ordinal);
        Assert.Contains("ApplyConflictServerBaselineAndReloadAsync", block, StringComparison.Ordinal);
        Assert.Contains("ApplyConflictServerBaseline", reconcile, StringComparison.Ordinal);
        Assert.Contains("DiscardDraft()", reconcile, StringComparison.Ordinal);
        Assert.Contains("AlertRole.Cancel", alert, StringComparison.Ordinal);
        Assert.Contains("AlertRole.Danger", alert, StringComparison.Ordinal);
        Assert.Contains(".Cancel(discardText, false)", alert, StringComparison.Ordinal);
        Assert.Contains("AlertRole.Dismiss", alert, StringComparison.Ordinal);
        Assert.Contains("DialogButtonStyle.Ghost", alert, StringComparison.Ordinal);
        Assert.Contains("DialogButtonStyle.Outline", alert, StringComparison.Ordinal);
        Assert.DoesNotContain("AlertPresets", drugIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmSaveConflict", drugIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("已被其他终端修改。", drugIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("await ReloadAsync(forceFull: true);", block, StringComparison.Ordinal);
    }

    private static string ExtractSaveConflictBlock(string source)
    {
        const string marker = "private async Task<bool> HandleSaveConflictAsync";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected save conflict handler.");

        var end = source.IndexOf("\n    [RelayCommand", start, StringComparison.Ordinal);
        return source[start..end];
    }

    [Fact]
    public void DrugIndex_defers_only_cascade_topics_and_supports_silent_reconcile()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.Reconcile.cs");
        var drugIndex = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/Pages/DrugIndex.cs");

        Assert.Contains("WorkspaceTopicRefresh.DeferDrugIndex", source, StringComparison.Ordinal);
        Assert.Contains("ApplySilentReconcile", source, StringComparison.Ordinal);
        Assert.Contains("ShouldSilentReconcile", source, StringComparison.Ordinal);
        Assert.Contains("HasPendingChanges", source, StringComparison.Ordinal);
        Assert.Contains("_pendingReselectKey.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("ApplyCleanRefresh", source, StringComparison.Ordinal);
        Assert.Contains("FocusSavedRow", source, StringComparison.Ordinal);
        Assert.Contains("QueueReselect", source, StringComparison.Ordinal);
        Assert.Contains("ReloadFromWatermarkAsync", source, StringComparison.Ordinal);
        Assert.Contains("_clearListFocusAfterReload", source, StringComparison.Ordinal);
        Assert.Contains("clearListFocus: true", drugIndex, StringComparison.Ordinal);
        Assert.Contains("existing.ApplySaved(server.ToDto())", source, StringComparison.Ordinal);
        Assert.Contains("_remoteEditBaseline", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_workspace_refresh_blocks_while_db_probe_or_startup_init_runs()
    {
        var source = ReadMainWindowViewModelSource();

        Assert.Contains("if (!CanWorkspaceRefresh())", source, StringComparison.Ordinal);
        Assert.Contains("RunWorkspaceRefreshAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dirtyRefresh.RunAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryRefreshDirtyActivePage", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Drug_index_watermark_refresh_blocks_while_db_probe_or_startup_init_runs()
    {
        var source = ReadRepoFile("apps/desktop-avalonia/src/ViewModels/MainWindow.AutoRefresh.cs");
        var methodStart = source.IndexOf("RefreshDrugIndexFromWatermarkAsync(DrugIndex page)", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Expected RefreshDrugIndexFromWatermarkAsync.");

        var method = source[methodStart..Math.Min(source.Length, methodStart + 500)];

        Assert.Contains("CanWorkspaceRefresh()", method, StringComparison.Ordinal);
        Assert.Contains("ReloadFromWatermarkAsync", method, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.Entry.cs", "dashboard.entry_page.reload_fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.Txn.cs", "dashboard.txn_page.reload_fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.Txn.cs", "dashboard.txn_trend.reload_fail")]
    [InlineData("apps/desktop-avalonia/src/ViewModels/Pages/Dashboard.Abnormal.cs", "dashboard.abnormal_page.reload_fail")]
    public void Dashboard_tab_reload_failures_rethrow(string relativePath, string logEvent)
    {
        var source = ReadRepoFile(relativePath);
        var eventIndex = source.IndexOf(logEvent, StringComparison.Ordinal);
        Assert.True(eventIndex >= 0, $"Expected log event {logEvent} in {relativePath}.");

        var tail = source[eventIndex..Math.Min(source.Length, eventIndex + 400)];
        Assert.Contains("FinishTabReloadFail", tail, StringComparison.Ordinal);
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
