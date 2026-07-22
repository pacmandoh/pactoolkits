# PacToolkits Desktop 日志事件对照与分级

更新时间: 2026-07-21
适用范围: `PacToolkits.Desktop.Avalonia`（JSON Line 文件日志）
生成方式: 从 `apps/desktop-avalonia/src` + `packages/infrastructure` + `packages/application` 源码扫描；与代码不一致时以代码为准。

## 1. 使用说明

- 日志目录默认:
  - macOS: `~/Library/Application Support/PacToolkits/logs/desktop`
  - Linux: `~/.config/PacToolkits/logs/desktop`
  - Windows: `%AppData%\PacToolkits\logs\desktop`
- 日志文件: `desktop-YYYY-MM-DD.log`（同日滚动为 `desktop-YYYY-MM-DD.N.log`）
- 升级迁移：旧默认目录 `.../logs/ui` 在加载配置时会改写为 `.../logs/desktop`（事件 `logging.directory.migrate`）；历史文件不自动搬迁
- 格式: JSON Line；关键字段 `ts` / `level` / `module` / `event` / `message` / `exception`（可选）/ `version` / `context`
- Agents：Desktop 控制 **Host + Injector**（不是单一 AHK 进程）；相关 `module` 多为 `Agents` / `MainWindowVM` / `Settings.Agents`

## 2. 按严重度分层（排障优先级）

### Fatal

- `unhandled.desktop`、`unhandled.appdomain`、`unobserved.task`

### Error（用户可感知失败）

- 更新: `update.check.fail`、`update.apply.flow_fail`、`update.*.fail`
- DB / 页面加载: `db.startup_check.fail`、`db.schema.incompatible.startup`、`*.reload_fail`、`*.save.fail`、`scan.submit.fail`
- Agents: `agents.start_or_restart.fail`、`agents.injector_*.fail`、`settings.agents.*.fail`
- 后台未等待任务: `*.detached.fail`（`ObserveDetached` / `TaskObserve` 接住未 await 任务的异常）

### Warn（降级 / 可恢复）

- 瞬断重试: `conn.open.transient_disconnect.retry`、`reload.transport_retry`
- 局部失败继续: `client_id.query.partial_fail`、多数 `*.dispose.*_fail`
- MSFX 审计成功类也记 Warn（如 `msfx.task.reopen.success`）便于检索

### Info（轨迹）

- 生命周期: `app.start` / `app.ready` / `app.shutdown`
- 更新: `update.check.*`、`update.apply.*`（非 fail）
- 重载: `reload.started` / `reload.skipped` / `reload.finished`
- Agents: `agents.reload`

### MSFX 页面审计（非本文件 JSON）

`码上放心联调` 页内「运行日志详情」由 `MsfxLink.AddAutoLog` 维护，**不**写入 Desktop 日志文件。部分关键节点另写 JSON（见下方 Msfx 节）。联调排障优先看页面审计；程序异常 / 保存失败看本表。

---

## 3. 事件名总表（按 module / 来源）

表项格式: `` `event` ``（Level）— message 摘要。同一 event 多 Level 时并列。

### App

- `app.ready` (Info) — Main window initialized
- `app.shutdown` (Info) — Application shutdown
- `app.start` (Info) — Application startup
- `shutdown.agents_stop_fail` (Warn) — Failed to stop registered Agents runtimes during shutdown
- `shutdown.services_dispose_fail` (Warn) — Failed to dispose service provider
- `unhandled.appdomain` (Fatal) — Unhandled AppDomain exception
- `unhandled.desktop` (Fatal) — Unhandled desktop exception
- `unobserved.task` (Fatal) — Unobserved task exception

### MainWindowVM

- `agents.host_top_action.error` (Error) — Agents Host top action failed
- `agents.injector_top_action.error` (Error) — Agents Injector top action failed
- `agents.startup_autostart.exception` (Warn) — Startup auto-start threw exception
- `agents.startup_autostart.fail` (Warn) — Failed to auto-start Agents host on startup
- `config.agent_sync_fail` (Warn) — Failed to synchronize agent configuration
- `config.external_apply_fail` (Warn) — Failed to handle external DB config change
- `config.external_db_change_ignored` (Warn) — Detected external DB config change, ignored until manual apply in Settings
- `config.safe_hot_reload.fail` (Warn) — Failed to hot-reload safe config sections
- `config.watch.parse_null` (Warn) — Config deserialize returned null; hot-reload skipped
- `config.watch.read_fail` (Warn) — Failed to read or parse config for hot-reload
- `config.watcher.init_fail` (Warn) — Failed to initialize config watcher
- `db.probe.error` (Error) — Database probe failed
- `db.probe.timeout` (Warn) — Database probe timed out
- `db.probe.unsuccessful` (Warn) — Database probe finished with unsuccessful result
- `db.schema.incompatible` (Warn) — Database schema incompatible
- `db.schema.incompatible.startup` (Error) — Database schema incompatible during startup
- `db.schema.ok` (Info) — Database schema version compatible
- `db.schema.external_update_required.startup` (Warn) — Database schema below app minimum; external update required before business access
- `db.schema.recovery_poll.fail` (Warn) — Schema recovery polling failed
- `db.schema.status.refresh.fail` (Warn) — Failed to refresh DB schema status for Settings page
- `db.startup_check.fail` (Error/Warn) — Database connection test failed on startup; shell banner will show status
- `db.startup_check.start` (Info) — Checking database connectivity on startup
- `dispose.safe_execute_fail` (Warn) — Dispose cleanup action failed
- `drug_index.watermark.refresh_fail` (Warn) — Drug index watermark refresh failed
- `main.init` (Info) — Main window initialized
- `page.lifecycle.activate_fail` (Warn) — Page activation failed
- `page.lifecycle.deactivate_fail` (Warn) — Page deactivation failed
- `page.refresh.active_fail` (Warn) — Active page refresh failed
- `page.refresh.batch_fail` (Warn) — Batch refresh failed
- `update.check.startup` (Info) — Auto checking updates on startup

### AppPageBase

- `reload.access_blocked.fail` (Warn) — Reload stopped because database access is blocked
- `reload.db_monitor_unhook.fail` (Warn) — Failed to unhook DB monitor events
- `reload.db_signal_refresh.fail` (Warn) — Auto refresh from DB signal failed
- `reload.db_transport_error` (Warn) — Reload hit transport error, signaling monitor
- `reload.execute.fail` (Error) — Page refresh execution failed
- `reload.finished` (Info) — Page reload finished
- `reload.get_access_guard.fail` (Warn) — Failed to resolve database access guard from DI
- `reload.get_db_monitor.fail` (Warn) — Failed to resolve DB monitor from DI
- `reload.get_startup_state.fail` (Warn) — Failed to resolve startup state from DI
- `reload.pipeline.fail` (Error) — Page reload failed with non-transport error
- `reload.skipped` (Info) — Page reload skipped
- `reload.started` (Info) — Page reload started
- `reload.transport_retry` (Warn) — Retrying reload after transport error

### PageReload

- `reload.run.fail` (Warn) — Page reload behavior captured exception

### Agents

- `agents.close_window.fail` (Warn) — Failed to close Agents process window gracefully
- `agents.dispose.gate_fail` (Warn) — Failed to dispose command gate
- `agents.dispose.timer_fail` (Warn) — Failed to dispose poll timer
- `agents.injector_start.fail` (Error) — Injector start failed
- `agents.injector_stop.fail` (Error) — Injector stop failed
- `agents.injector_terminate.fail` (Warn) — Failed to terminate Injector process
- `agents.kill.fail` (Warn) — Failed to kill Agents process
- `agents.reload` (Info) — Agents runtime config reloaded
- `agents.start_or_restart.fail` (Error) — Agents start/restart failed
- `agents.status_changed.fail` (Warn) — Agents status change handler failed
- `agents.stop.fail` (Error) — Agents stop failed
- `agents.terminate.fail` (Warn) — Failed to terminate Agents process

### SettingsVM

- `client_alias.save.fail` (Error) — Failed to save client aliases
- `db.save.fail` (Error) — Failed to save DB settings
- `db.schema.startup_refresh.fail` (Error) — Startup schema status refresh failed
- `db.test.timeout` (Warn) — DB connection test timed out
- `desktop_behavior.save.fail` (Error) — Failed to save desktop behavior
- `dispose.client_alias_unsub_fail` (Warn) — Failed to unsubscribe ClientAlias
- `dispose.desktop_behavior_unsub_fail` (Warn) — Failed to unsubscribe UiBehavior
- `dispose.logging_settings_unsub_fail` (Warn) — Failed to unsubscribe LoggingSettings
- `dispose.trace_rule_unsub_fail` (Warn) — Failed to unsubscribe TraceCodeRule
- `dispose.update_flow_unsub_fail` (Warn) — Failed to unsubscribe UpdateFlow
- `dispose.update_settings_unsub_fail` (Warn) — Failed to unsubscribe UpdateSettings
- `dispose.updates_unsub_fail` (Warn) — Failed to unsubscribe Updates
- `logging.copy_path_fail` (Error) — Failed to copy log path
- `logging.export_recent` (Info) — Exported recent logs
- `logging.export_recent_fail` (Error) — Failed to export recent logs
- `logging.open_dir` (Info) — Opened log directory
- `logging.open_dir_fail` (Error) — Failed to open log directory
- `logging.settings.save_fail` (Error) — Failed to save logging settings
- `logging.settings.saved` (Info) — Logging settings updated
- `msfx.cursor.advance.fail` (Error) — Failed to advance msfx pull cursor
- `msfx.settings.save.fail` (Error) — Failed to save msfx api settings
- `trace_rule.regex_invalid` (Warn) — Invalid trace regex pattern
- `trace_rule.save.fail` (Error) — Failed to save trace code rule
- `update.autosave.fail` (Error) — Failed to auto-save update preferences
- `update.clear_ignored.fail` (Error) — Failed to clear ignored version
- `update.settings.save.fail` (Error) — Failed to save update settings

### Settings.Agents

- `settings.agents.dispose.appwin_collection_unsub_fail` (Warn) — Failed to unsubscribe InjectorAppWinItems
- `settings.agents.dispose.appwin_item_unsub_fail` (Warn) — Failed to unsubscribe InjectorAppWin item
- `settings.agents.dispose.colspecs_collection_unsub_fail` (Warn) — Failed to unsubscribe InjectorColSpecsItems
- `settings.agents.dispose.colspecs_item_unsub_fail` (Warn) — Failed to unsubscribe InjectorColSpecs item
- `settings.agents.dispose.intcols_collection_unsub_fail` (Warn) — Failed to unsubscribe InjectorIntColsItems
- `settings.agents.dispose.intcols_item_unsub_fail` (Warn) — Failed to unsubscribe InjectorIntCols item
- `settings.agents.dispose.runtime_unsub_fail` (Warn) — Failed to unsubscribe runtime status
- `settings.agents.dispose.warehouse_anchor_item_unsub_fail` (Warn) — Failed to unsubscribe InjectorWarehouseAnchor item
- `settings.agents.dispose.warehouse_anchors_collection_unsub_fail` (Warn) — Failed to unsubscribe InjectorWarehouseAnchorItems
- `settings.agents.host_run.fail` (Error) — Failed to toggle Host running
- `settings.agents.injector_enable.fail` (Error) — Failed to save Injector enabled
- `settings.agents.injector_options.parse_fail` (Error) — Failed to parse Injector options
- `settings.agents.injector_run.fail` (Error) — Failed to toggle Injector running
- `settings.agents.restart.fail` (Error) — Failed to restart Agents runtime
- `settings.agents.save.fail` (Error) — Failed to save Agents settings
- `settings.agents.save_options.fail` (Error) — Failed to save Agents options to config
- `settings.agents.save_options.silent_fail` (Error) — Silent save options failed

### Settings

- `settings.nav_click.fail` (Warn) — Navigation button handler failed

### AppUpdateService

- `update.apply.downloaded` (Info) — Update package downloaded and pending restart
- `update.apply.none` (Info) — Apply requested but no updates were available
- `update.apply.pending` (Info) — Update already pending restart
- `update.pending.load_fail` (Warn) — Failed to load pending update state
- `update.channel.align_installed` (Info) — Aligned configured update channel with Velopack installed channel
- `update.channel.align_installed.fail` (Warn) — Failed aligning configured update channel with installed channel
- `update.check.available` (Info) — Update check found release
- `update.check.compatibility_blocked` (Warn) — Update check blocked by release or database compatibility
- `update.check.fail` (Error) — Update check failed
- `update.check.start` (Info) — Starting update check
- `update.check.uptodate` (Info) — No updates available
- `update.restart.apply` (Info) — Applying pending update and restarting
- `update.restart.compatibility_blocked` (Warn) — Pending update restart blocked by current compatibility check

### UpdateDesktopFlow

- `update.apply.flow_fail` (Error) — Update apply flow failed

### ReleaseManifestProbe

- `update.channel.feed_probe_fail` (Warn) — Failed probing target release channel feed

### AppConfigStore

- `config.atomic_cleanup.fail` (Warn) — Failed to cleanup temporary config file
- `config.legacy_migrate.fail` (Warn) — Failed to migrate legacy config file
- `logging.directory.migrate` (Warn) — Migrating legacy desktop log directory to new standard location

### AppViews

- `view.resolve.fail` (Warn) — No view type mapped for viewmodel

### Dashboard

- `dashboard.catalog.reload_fail` (Warn) — Failed to reload drug catalog after drug-index change
- `dashboard.dispose.safe_execute_fail` (Warn) — Dispose cleanup action failed
- `dashboard.init.filters_fail` (Warn) — Failed to initialize dashboard filters
- `dashboard.pointer_handler.fail` (Warn) — Dashboard pointer handler failed
- `dashboard.reload.fail` (Error) — Failed to reload dashboard
- `dashboard.reload.lazy_filters_fail` (Warn) — Failed lazy loading filters during reload
- `dashboard.row_activation.fail` (Warn) — Dashboard row activation failed
- `dashboard.selection_handler.fail` (Warn) — Dashboard selection handler failed

### Dashboard.Abnormal

- `dashboard.abnormal_page.reload_fail` (Error) — Failed to reload abnormal page

### Dashboard.Entry

- `dashboard.entry_page.reload_fail` (Error) — Failed to reload entry page

### Dashboard.Txn

- `dashboard.txn_page.reload_fail` (Error) — Failed to reload transaction page
- `dashboard.txn_trend.reload_fail` (Error) — Failed to reload transaction trend page

### DrugIndex

- `drug_index.delete.fail` (Error) — Failed to delete drug row
- `drug_index.fix_key.concurrency_conflict` (Warn) — Detected key-fix concurrency conflict
- `drug_index.fix_key.fail` (Error) — Failed to fix drug key
- `drug_index.import_clipboard.fail` (Error) — Failed importing drug rows from clipboard
- `drug_index.reload.fail` (Error) — Failed to reload drug index
- `drug_index.save.concurrency_conflict` (Warn) — Detected optimistic concurrency conflict
- `drug_index.save.fail` (Error) — Failed to save drug row

### ScanCode

- `scan.entry_log.write_fail` (Warn) — trace_entry_log write failed after submit
- `scan.notify_drug_index.fail` (Warn) — Failed to refresh drug options after drug-index change
- `scan.pool_check.fail` (Warn) — Failed to pre-check trace codes in pool
- `scan.prefill.qty_read.fail` (Warn) — Failed to read qty during prefill
- `scan.specs.load_fail` (Error) — Failed to load specs for selected drug
- `scan.submit.fail` (Error) — Submit trace codes failed

### InventoryOverview

- `inventory.pointer_release.fail` (Warn) — Failed handling row pointer action
- `inventory.stock_begin_edit.fail` (Warn) — Failed handling stock begin-edit
- `inventory.stock_cell_press.fail` (Warn) — Failed handling stock cell pointer pressed
- `inventory.stock_edit_end.fail` (Warn) — Failed committing stock cell edit

### InventoryOverview.DetailOps

- `inventory.external_refresh.reconcile_fail` (Warn) — Failed to reconcile current detail page after external change
- `inventory.reassign.apply_fail` (Error) — Failed to apply reassign operation
- `inventory.reassign.preview_fail` (Error) — Failed to preview reassign operation
- `inventory.reassign_context.load_fail` (Error) — Failed to load reassign context
- `inventory.reassign_drug_options.load_fail` (Warn) — Failed to load reassign drug options
- `inventory.stock.delete_fail` (Error) — Failed to delete stock rows

### MsfxLink.AutoBoard

- `msfx.audit.diagnose.pending_without_match` (Warn) — MSFX diagnostics found pending rows without mapping hit
- `msfx.audit.snapshot.refresh_fail` (Warn) — MSFX snapshot refresh failed
- `msfx.auto.run.fail` (Error) — MSFX auto run failed
- `msfx.auto.run.finish` (Info) — MSFX auto run finished
- `msfx.auto.run.start` (Info) — MSFX auto run started
- `msfx.sensitive.unlock.cancelled` (Warn) — MSFX sensitive operation cancelled before database write
- `msfx.sensitive.unlock.granted` (Warn) — MSFX sensitive operation unlocked
- `msfx.task.discard.fail` (Error) — MSFX inject task discard failed
- `msfx.task.discard.success` (Warn) — MSFX inject task discarded
- `msfx.task.merge.fail` (Error) — MSFX inject tasks merge failed
- `msfx.task.merge.success` (Warn) — MSFX inject tasks merged
- `msfx.task.remap.fail` (Error) — MSFX inject task return to mapping queue failed
- `msfx.task.remap.success` (Warn) — MSFX inject task returned to mapping queue
- `msfx.task.reopen.fail` (Error) — MSFX inject task reopen failed
- `msfx.task.reopen.success` (Warn) — MSFX inject task reopened
- `msfx.task.split.custom.success` (Warn) — MSFX inject task custom split
- `msfx.task.split.fail` (Error) — MSFX inject task split failed
- `msfx.task.split.success` (Warn) — MSFX inject task split

### MsfxLink.Mapping

- `msfx.map.batch.apply` (Info) — MSFX batch mapping applied
- `msfx.map.batch.discard` (Info) — MSFX batch mapping discarded into task queue
- `msfx.mapping.catalog.reload_fail` (Warn) — Failed to refresh mapping drug catalog

### MsfxLink.Subcode

- `msfx.subcode.query_fail` (Error) — Failed to query subcodes
- `msfx.subcode.query_timeout` (Warn) — Subcode query timed out

### MsfxLink.Upout

- `msfx.upout.query_fail` (Error) — Failed to query upstream outbound list
- `msfx.upout.query_timeout` (Warn) — Upstream outbound query timed out

### PgDb

- `conn.open.transient_disconnect.retry` (Warn) — Transient disconnect detected while opening connection, retrying once
- `pool.clear.fail` (Warn) — Failed to clear Npgsql pools
- `session_lock.release.fail` (Warn) — PostgreSQL session lock release failed
- `tx.rollback.fail` (Warn) — Transaction rollback failed

### DbConnectionMonitor

- `monitor.conn.close_fail` (Warn) — Failed to close probe connection
- `monitor.dispose.cancel_fail` (Warn) — Failed to cancel DB monitor
- `monitor.dispose.channel_close_fail` (Warn) — Failed to close monitor channel
- `monitor.next_signal.await_fail` (Warn) — Failed awaiting pending signal
- `monitor.pending_signal.read_fail` (Warn) — Failed to read pending signal
- `monitor.probe.fail` (Warn) — Database probe loop failed
- `monitor.probe.scalar_fail` (Warn) — DB probe scalar check failed
- `monitor.wait_cancel.fail` (Warn) — Failed to cancel wait CTS

### DbConnectionTester

- `db.test.fail` (Warn) — Database connection test failed

### DbSchemaVersion

- `schema_version.read_fail` (Warn) — Failed reading schema_version

### ChangeWatermark

- `watermark.dispatch.fail` (Warn) — Watermark dispatch failed
- `watermark.dispose.cancel_fail` (Warn) — Failed to cancel watermark service
- `watermark.dispose.channel_close_fail` (Warn) — Failed to close watermark channel
- `watermark.poll.fail` (Warn) — Watermark polling failed

### ClientIdReadRepo

- `client_id.query.partial_fail` (Warn) — Failed one client-id query, continue fallback

### ClientAliasStore

- `alias.save.persist_fail` (Warn) — Failed to persist alias map to config

### DataGridInteraction

- `grid.clear_selection_index.fail` (Warn) — Failed to clear selected index
- `grid.clear_selection_item.fail` (Warn) — Failed to clear selected item
- `grid.clear_selection_items.fail` (Warn) — Failed to clear selected items
- `grid.scroll_into_view.fail` (Warn) — Failed to scroll row into view
- `grid.set_current_column.fail` (Warn) — Failed to set current column

### ObserveDetached / TaskObserve

启动后未 `await` 的后台任务若失败，由此写入日志；`module` 为调用方类型名（或 `TaskObserve` 显式传入的 module）。下列按 event 汇总。

- `abnormal.reload.detached.fail` (Error) — Detached task failed
- `auto_refresh.detached.fail` (Error) — Detached task failed
- `catalog.refresh.detached.fail` (Error) — Detached task failed
- `catalog.sync.detached.fail` (Error) — Detached task failed
- `commit.apply.detached.fail` (Error) — Detached task failed (`AutoCompleteCommit` / TaskObserve)
- `config.watch.detached.fail` (Error) — Detached task failed
- `db.bootstrap.detached.fail` (Error) — Detached task failed
- `drug_index.watermark.refresh.fail` (Error) — Detached task failed
- `entry.reload.detached.fail` (Error) — Detached task failed
- `grid.mount.detached.fail` (Error) — Detached task failed (`PageGridMountScheduler`)
- `init.detached.fail` (Error) — Detached task failed
- `map_queue.refresh.detached.fail` (Error) — Detached task failed
- `msfx.mapping.catalog.reload.detached.fail` (Error) — Detached task failed
- `msfx.mapping.preview.detached.fail` (Error) — Detached task failed
- `msfx.mapping.search.detached.fail` (Error) — Detached task failed
- `msfx.mapping.target.detached.fail` (Error) — Detached task failed
- `page.active.detached.fail` (Error) — Detached task failed
- `page.lifecycle.detached.fail` (Error) — Detached task failed
- `pool.check.detached.fail` (Error) — Detached task failed
- `qty.refresh.detached.fail` (Error) — Detached task failed
- `qty.sync.detached.fail` (Error) — Detached task failed
- `queue.search.detached.fail` (Error) — Detached task failed
- `reassign.preview.detached.fail` (Error) — Detached task failed
- `reconcile.detached.fail` (Error) — Detached task failed
- `reload.detached.fail` (Error) — Detached task failed
- `reload.quiet.detached.fail` (Error) — Detached task failed
- `schema.recovery.detached.fail` (Error) — Detached task failed
- `schema.refresh.detached.fail` (Error) — Detached task failed
- `selection.change.detached.fail` (Error) — Detached task failed
- `settings.agents.host_run.detached.fail` (Error) — Detached task failed
- `settings.agents.injector_enable.detached.fail` (Error) — Detached task failed
- `settings.agents.injector_run.detached.fail` (Error) — Detached task failed
- `settings.tab_switch.detached.fail` (Error) — Detached task failed
- `startup.config.detached.fail` (Error) — Detached task failed
- `startup.init.detached.fail` (Error) — Detached task failed
- `tray.persist.detached.fail` (Error) — Detached task failed
- `txn.reload.detached.fail` (Error) — Detached task failed
- `txn_trend.reload.detached.fail` (Error) — Detached task failed
- `update.poll.detached.fail` (Error) — Detached task failed
- `update.recheck.detached.fail` (Error) — Detached task failed (`AppUpdateService`)
- `workspace.dirty_refresh.fail` (Error) — Detached task failed
- `workspace.refresh.detached.fail` (Error) — Detached task failed

## 4. 检索建议

```bash
# 今日 Error/Fatal
jq -c 'select(.level=="Error" or .level=="Fatal")' ~/Library/Application\ Support/PacToolkits/logs/desktop/desktop-$(date +%F).log

# Agents / Injector
jq -c 'select(.event|startswith("agents.") or startswith("settings.agents."))' .../desktop-YYYY-MM-DD.log

# 更新通道
jq -c 'select(.event|startswith("update."))' .../desktop-YYYY-MM-DD.log

# 页面重载流水线
jq -c 'select(.event|startswith("reload."))' .../desktop-YYYY-MM-DD.log
```

## 5. 相关文档

- [Desktop 概览](./overview.md)
- [Agents 运行时架构](../../../docs/architecture/agents.md)
- [Desktop 状态模型](../../../docs/architecture/desktop-state.md)
