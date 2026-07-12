# PacToolkits Desktop 日志事件对照与分级

更新时间: 2026-06-19
适用范围: `PacToolkits.Desktop.Avalonia`（manifest 组件 `desktop`，当前实现 `avalonia`）

## 1. 使用说明

- 日志目录默认:
  - macOS: `~/Library/Application Support/PacToolkits/logs/desktop`
  - Linux: `~/.config/PacToolkits/logs/desktop`
- 日志文件: `desktop-YYYY-MM-DD.log`（同日滚动为 `desktop-YYYY-MM-DD.N.log`）
- 升级迁移：若本地配置仍保存旧默认目录 `.../PacToolkits/logs/ui`，启动加载配置时会自动改写为 `.../logs/desktop` 并写回配置文件（事件 `logging.directory.migrate`）；旧目录中的历史日志文件不会自动搬迁
- 日志格式: JSON Line（每行一个 JSON 对象）
- 关键字段:
  - `ts`: 时间
  - `level`: 严重度（`Debug|Info|Warn|Error|Fatal`）
  - `module`: 模块
  - `event`: 事件名
  - `message`: 描述
  - `exception`: 异常详情（可选）

---

## 2. 按严重度分层（排障优先级）

### 2.1 `Fatal`（最高优先级）

先看是否出现以下事件:

- `unhandled.desktop`
- `unobserved.task`
- `unhandled.appdomain`

含义:

- 程序存在未兜底异常，可能导致崩溃或状态不可恢复。

动作建议:

1. 先抓同时间窗口的 `Error` 事件上下文。
2. 对照 `exception.stackTrace` 定位代码路径。
3. 必要时降低功能开关（更新/自动化）复现验证。

### 2.2 `Error`（业务失败级）

重点看这几类事件组:

- 更新链路: `update.*.error`, `update.*.fail`
- 数据库链路: `db.*.fail`, `dashboard.*.reload_fail`, `drug_index.*.fail`, `scan.submit.fail`, `inventory.reassign.*.fail`
- 自动化集成: `settings.automation.*.fail`, `ahk.*.fail`
- 配置链路: `*.save.fail`, `config.external_apply_fail`

含义:

- 用户可感知失败（保存失败、加载失败、提交失败）。

动作建议:

1. 优先按 `event` 聚合统计频次。
2. 高频 `Error` 先修复；低频但高影响（数据写入/更新）次之。

### 2.3 `Warn`（降级/可恢复异常）

常见:

- 重试/瞬断: `conn.transient_disconnect.retry`, `tx.transient_disconnect.retry`
- 局部失败但流程继续: `client_id.query.partial_fail`, `inventory.stock.batch_update.one_fail`
- Dispose/取消订阅清理失败: `*.dispose.*_fail`

含义:

- 系统进入降级路径，但不一定立即影响用户主流程。

动作建议:

1. 观察是否短时间放大并升级为 `Error`。
2. 若集中在同模块，补稳定性和重试策略。

### 2.4 `Info`（状态/轨迹）

常见:

- 生命周期: `app.start`, `app.ready`, `app.shutdown`
- 更新轨迹: `update.check.result`, `update.prompt.*`
- 配置操作: `logging.settings.saved`, `ahk.options.saved`

含义:

- 主要用于复盘路径与行为链路。

---

### 2.5 `MSFX` 页面审计日志（非 JSON logger）

`码上放心联调` 页面下方的“运行日志详情”不是 `_logger` 写入的 JSON Line 日志，
而是页面内审计面板数据，由 `MsfxLink.AddAutoLog(...)` 维护。

这类事件:

- 不写入 Desktop 日志文件
- 不走 `module/event/message` 统一结构
- 主要用于联调链路的页面内追踪与人工排查

说明:

- 当前已将部分关键节点同步写入 Desktop JSON logger，见下方 `MsfxLink` 模块事件表
- 但页面内绝大多数即时轨迹仍以审计面板为主，不做全量持久化

常见阶段名:

- `初始化`
- `调度`
- `任务`
- `批次`
- `重试`
- `子码解析`
- `落库`
- `上游出库单`
- `映射`
- `建任务`
- `批次结算`
- `批量映射`
- `任务重开`
- `诊断`
- `审计`
- `异常`
- `日志`

如果要排查 `MSFX` 页面联调执行链，优先看页面内审计面板；
如果要排查 Desktop 程序异常、保存失败、更新失败等，再看本文件下面的 JSON logger 事件总表。

---

## 3. 事件名总表（按模块）

## App

- `app.start`
- `app.ready`
- `app.shutdown`
- `unhandled.desktop`
- `unobserved.task`
- `unhandled.appdomain`
- `shutdown.ahk_stop_fail`
- `shutdown.services_dispose_fail`

## MainWindowVM

- `main.init`
- `db.startup_check.start`
- `db.schema.read_fail`
- `db.schema.mismatch`
- `db.schema.ok`
- `db.schema.incompatible`
- `db.schema.incompatible.single_path.still_bad`
- `db.schema.postcheck.refresh.fail`
- `db.schema.status.refresh.fail`
- `db.probe.timeout`
- `db.probe.error`
- `db.probe.unsuccessful`
- `db.startup_check.migrate.skipped`
- `db.startup_check.migrate.retry_on_incompatible`
- `db.startup_check.migrate.stamp.saved`
- `db.startup_check.fail`
- `db.reconnected.migrate.fail`
- `ahk.startup_autostart.fail`
- `ahk.startup_autostart.exception`
- `ahk.top_action.error`
- `update.check.startup`
- `update.check.start`
- `update.check.channel_mismatch`
- `update.check.result`
- `update.check.uptodate`
- `update.check.available`
- `update.check.cancel`
- `update.check.error`
- `update.prompt.apply_now`
- `update.prompt.ignore`
- `update.apply.pending`
- `update.apply.channel_mismatch`
- `update.apply.none`
- `update.apply.downloaded`
- `update.apply.error`
- `update.restart.apply`
- `config.watcher.init_fail`
- `config.external_db_change_ignored`
- `config.external_apply_fail`
- `page.refresh.batch_fail`
- `dispose.safe_execute_fail`

## AppPageBase

- `reload.busy_delay.fail`
- `reload.db_monitor_unhook.fail`
- `reload.db_signal_refresh.fail`
- `reload.db_transport_error`
- `reload.execute.fail`
- `reload.get_db_monitor.fail`
- `reload.get_startup_state.fail`
- `reload.local.db_transport_error`

## SettingsVM

- `db.test.timeout`
- `db.save.fail`
- `db.schema.incompatible`
- `db.schema.startup_refresh.fail`
- `client_alias.save.fail`
- `trace_rule.regex_invalid`
- `trace_rule.save.fail`
- `desktop_behavior.save.fail`
- `update.settings.save.fail`
- `update.check.fail`
- `update.apply.fail`
- `update.clear_ignored.fail`
- `update.ignore.fail`
- `logging.settings.saved`
- `logging.settings.save_fail`
- `logging.open_dir`
- `logging.open_dir_fail`
- `logging.copy_path_fail`
- `logging.export_recent`
- `logging.export_recent_fail`
- `dispose.desktop_behavior_unsub_fail`
- `dispose.update_settings_unsub_fail`
- `dispose.updates_unsub_fail`
- `dispose.logging_settings_unsub_fail`
- `settings.nav_click.fail`

## InventoryOverview

- `inventory.reassign_context.load_fail`
- `inventory.reassign_drug_options.load_fail`
- `inventory.reassign_specs.refresh_fail`
- `inventory.reassign.preview_fail`
- `inventory.reassign.apply_fail`
- `inventory.stock.batch_update.one_fail`
- `inventory.stock.delete_fail`
- `inventory.external_refresh.reconcile_fail`

## ScanCode

- `scan.notify_drug_index.fail`
- `scan.prefill.qty_read.fail`
- `scan.specs.load_fail`
- `scan.entry_log.write_fail`
- `scan.submit.fail`

## Settings（自动化集成）

- `settings.automation.save.fail`
- `settings.automation.restart.fail`
- `settings.automation.toggle.fail`
- `settings.automation.save_options.silent_fail`
- `settings.automation.save_options.fail`
- `settings.automation.agent_options.parse_fail`
- `settings.automation.dispose.runtime_unsub_fail`
- `settings.automation.dispose.appwin_collection_unsub_fail`
- `settings.automation.dispose.colspecs_collection_unsub_fail`
- `settings.automation.dispose.intcols_collection_unsub_fail`
- `settings.automation.dispose.warehouse_anchors_collection_unsub_fail`
- `settings.automation.dispose.appwin_item_unsub_fail`
- `settings.automation.dispose.colspecs_item_unsub_fail`
- `settings.automation.dispose.intcols_item_unsub_fail`
- `settings.automation.dispose.warehouse_anchor_item_unsub_fail`

说明:

- 模块名为 `Settings`；事件前缀 `settings.automation.*`（设置页「自动化集成」Tab）

## MsfxLink

- `msfx.auto.run.start`
- `msfx.auto.batch.created`
- `msfx.auto.map.summary`
- `msfx.auto.task_build.summary`
- `msfx.auto.run.finish`
- `msfx.auto.run.fail`
- `msfx.auto.batch_finalize.fail`
- `msfx.task.reopen.success`
- `msfx.task.reopen.fail`
- `msfx.map.batch.apply`
- `msfx.task.merge.success`
- `msfx.task.split.success`
- `msfx.audit.diagnose.pending_without_match`
- `msfx.audit.snapshot.refresh_fail`
- `msfx.auto.timer_tick.fail`

说明:

- `msfx.auto.*` 里的开始/汇总/完成类事件主要为 `Info`
- 人工介入且需要保留审计可见性的事件，如 `msfx.task.reopen.success`、`msfx.map.batch.apply`、`msfx.task.merge.success`、`msfx.task.split.success`，按 `Warn` 记录
- 失败类事件按 `Error`

## MsfxMappingBatchDialogVM

- `msfx.map.batch.reload_fail`
- `msfx.map.batch.preview_fail`

说明:

- 批量映射对话框内分组加载/预览失败按 `Warn` 记录（模态对话框，不重复 shell 连接 toast）

## MsfxMappingBatchView

- `msfx.map.batch.attach_init.fail`

## SearchInputDebouncer

- `search.debounce.fail`

## Dashboard

- `dashboard.init.filters_fail`
- `dashboard.reload.lazy_filters_fail`
- `dashboard.reload.fail`
- `dashboard.txn_page.reload_fail`
- `dashboard.txn_trend.reload_fail`
- `dashboard.entry_page.reload_fail`
- `dashboard.abnormal_page.reload_fail`
- `dashboard.dispose.safe_execute_fail`

## DrugIndex

- `drug_index.fix_key.concurrency_conflict`
- `drug_index.fix_key.fail`
- `drug_index.import_clipboard.fail`
- `drug_index.save.concurrency_conflict`
- `drug_index.save.fail`
- `drug_index.reload.fail`
- `drug_index.delete.fail`

## AhkRuntime

- `ahk.reload`
- `ahk.options.saved`
- `ahk.start_or_restart.fail`
- `ahk.stop.fail`
- `ahk.terminate.fail`
- `ahk.close_window.fail`
- `ahk.kill.fail`
- `ahk.dispose.timer_fail`
- `ahk.dispose.gate_fail`

## DbConnectionMonitor

- `monitor.dispose.cancel_fail`
- `monitor.dispose.channel_close_fail`
- `monitor.wait_cancel.fail`
- `monitor.next_signal.await_fail`
- `monitor.pending_signal.read_fail`
- `monitor.probe.fail`
- `monitor.conn.close_fail`
- `monitor.probe.scalar_fail`

## DbSchemaVersion

- `schema_version.read_fail`

## DbConnectionTester

- `db.test.fail`

## DbSchemaMigration

- `db.migrate.start`
- `db.migrate.bootstrap.begin`
- `db.migrate.bootstrap.ok`
- `db.migrate.apply.begin`
- `db.migrate.apply.ok`
- `db.migrate.apply.fail`
- `db.migrate.finish`

## UpdateDesktopFlow

- `update.check.result`
- `update.check.cancel`
- `update.check.timeout`
- `update.check.error`
- `update.toast.ignore`
- `update.toast.apply`
- `update.apply.flow_fail`

## PgDb

- `conn.transient_disconnect.retry`
- `tx.transient_disconnect.retry`
- `tx.rollback.fail`
- `tx.retry.rollback.fail`
- `pool.clear.fail`

## ChangeWatermark

- `watermark.dispose.cancel_fail`
- `watermark.dispose.channel_close_fail`
- `watermark.poll.fail`
- `watermark.dispatch.fail`

## ClientAliasStore

- `alias.save.persist_fail`

## AppConfigStore

- `config.atomic_cleanup.fail`
- `logging.directory.migrate`

## PageReload

- `reload.run.fail`

## DataGridInteraction

- `grid.clear_selection_item.fail`
- `grid.clear_selection_index.fail`
- `grid.clear_selection_items.fail`
- `grid.read_selected_items.fail`

## ViewLocator

- `view.cache.hit`
- `view.cache.add`
- `view.create.new`

## AppViews

- `view.resolve.fail`

## DashboardView

- `dashboard.selection_handler.fail`
- `dashboard.pointer_handler.fail`

## InventoryOverviewView

- `inventory.pointer_release.fail`
- `inventory.stock_edit_end.fail`
- `inventory.context_delete.fail`

## ClientIdReadRepo

- `client_id.query.partial_fail`

---

## 4. 常用检索命令

在日志目录执行:

```bash
# 看最近 Fatal/Error
rg '"level":"(Fatal|Error)"' desktop-*.log

# 看更新链路
rg '"event":"update\.' desktop-*.log

# 看 DB 相关
rg '"module":"(DbConnectionMonitor|PgDb|DbSchemaVersion|DbConnectionTester)"' desktop-*.log

# 看页面失败热点
rg '"event":"(dashboard\.|scan\.|inventory\.|drug_index\.|tools\.)' desktop-*.log
```

---

## 5. 维护约定

1. 新增 `catch` 时必须记录日志（至少 `Warn`，用户可感知失败用 `Error`）。
2. 事件名格式统一: `domain.action.result`（如 `update.check.result`）。
3. 同一失败场景事件名不要频繁改名，避免历史统计断裂。
4. 修改事件名时同步更新本文件。
