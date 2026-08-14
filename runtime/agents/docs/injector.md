# Injector 模块

Injector 是 Agents 的生产业务模块，由 Host 按 `module.json` 的 `entry.win-x64` 启动。AutoHotkey v2：解析目标界面、注入追溯码、校验结果；任务同步走 PacAPI

## 职责

- 识别目标应用和业务窗口
- 解析 Grid 或剪贴板中的业务数据
- 执行门诊、住院和仓库场景的追溯码录入（左键拆零、右键全量；仓库仍走左键）
- 验证录入结果并记录异常
- 领取仓库任务并回写任务状态与事件

## 启动参数

Host 启动 Injector 时传入两类配置：

| 参数                | 内容                                                    |
| ------------------- | ------------------------------------------------------- |
| `--config`          | Desktop 配置文件绝对路径；环境变量缺省时读取 `PacApi.BaseUrl` / `PacApi.AgentsApiKey` |
| `--module-settings` | Injector 用户配置绝对路径，提供窗口、控件和业务策略配置 |

缺少任一参数、配置文件无法读取或关键配置无效时，Injector 自检失败且不会创建 `module.ready`；失败会写入 JSON 日志（`UI_Fail` / `Log_Error`）。

## 日志

- 目录：`%LocalAppData%\PacToolkits\logs\agents\modules\Injector\`
- 格式：JSON Lines（`lib/ahk/log.ahk`）
- 约定：与 Desktop 同字段；`level` ∈ `Debug|Info|Warn|Error|Fatal`；`event` = 分类短名；`message` = 正文；附加进 `context`
- API：`Log_Debug` / `Log_Info` / `Log_Warn` / `Log_Error` / `Log_Fatal`（写入 `Debug`…`Fatal` 原文）
- 门控与滚动：模块 `settings.json` 的 `LogEnabled` / `LogMinimumLevel` / `LogRetentionDays` / `LogMaxFileSizeMb`（默认开启、`Error`、14 天、20MB）；加载配置后 `Log_ApplySettings`
- 级别：`Debug` = 热键/解析/半自动/事务/贴码/校验/仓库轨迹；`Info` = 启动就绪、清理 PENDING、门诊热键、半自动/仓库完成；`Warn` = 半自动可恢复告警；`Error` = `UI_Fail`
- 调试：将 `LogMinimumLevel` 设为 `Debug` 并重启模块。热键无反应看 `hotif.deny` / `hot.*.miss_grid`；解析看 `parse.*`；预留看 `txn.reserve.*`；贴码看 `ui.paste*`；校验看 `ui.confirm.*_tick`；仓库看 `msfx.*` / `wh.soft.*`

## 配置文件

| 文件        | 位置                                                | 用途                               |
| ----------- | --------------------------------------------------- | ---------------------------------- |
| 默认配置    | `Agents/Modules/Injector/settings.json`             | 新用户配置的初始值                 |
| 设置 schema | `Agents/Modules/Injector/settings.schema.json`      | Desktop 设置页的字段定义和校验规则 |
| 用户配置    | `{ConfigDir}/agents/modules/Injector/settings.json` | 运行时实际读取的业务配置           |

Desktop 仅在用户配置不存在时复制默认配置；升级时若默认配置新增键（如 `LogEnabled`），会合并进已有用户配置而不会覆盖已有值。`ModuleSettingsValidator` 在保存和启动前按 schema 校验用户配置。

## 运行门控

全局热键由 `Util_HotIf_TargetApp()` 限制。执行自动化前必须同时满足：

- 当前前台进程存在于 `AppWin`
- 当前窗口类匹配 `OptWindowClass` 或 `IptWindowClass`
- 仓库模式仅在住院窗口类下启用

窗口类、ClassNN、仓库识别文本和字段策略均由用户配置提供。运行时不会提供绕过目标应用检查的全局模式。

门诊取码计划读数量列的「单位」，对照 `OptPackUnits`（包装单位）与 `OptPieceUnits`（数量单位）；空或未命中任一侧则中止。

## 源码结构

相对 `runtime/agents/modules/injector/`：

- `main.ahk`：启动、自检和模块入口
- `module.json`：模块发现、桌面展示和构建元数据
- `settings.json`、`settings.schema.json`：默认业务配置与设置页定义
- `src/main_semi_auto.ahk`：半自动录入流程（左键拆零 / 右键全量）
- `src/txn_plan.ahk`：取码计划（整盒数与拆零粒）
- `src/msfx_task.ahk`：仓库任务 Run 流程、行指纹与节拍
- `src/pg_exec.ahk`：追溯码事务 PacAPI（预留/提交/回滚/清理 PENDING）
- `src/msfx_sql.ahk`：仓库任务 PacAPI（领取/防重/回写/事件/结算）
- `src/msfx_code.ahk`：取码策略与码串分组
- `src/ui_focus.ahk`：网格/编辑框聚焦、HWND 缓存与网格拷贝
- `src/ui_paste.ahk`：追溯码贴入策略（门诊/住院/仓库）
- `src/ui_confirm.ahk`：录入结果等待与弹窗处置
- `src/util_misc.ahk`：ID/剪贴板/dotenv 等杂项辅助
- `src/util_config.ahk`：模块配置加载与字段校验
- `src/util_scene.ahk`：场景识别、HotIf、表头抓取

进程控制、配置生效和二进制更新行为见 [Agents 运行时架构](../../../docs/architecture/agents.md)。

## 测试

`test_that/` 读模块目录 `.env.local`：`PAC_API_BASE_URL`、`PAC_API_KEY`（需 `read` 与 `write`）。可选 `TEST_CLIENT_ID`、`TEST_DRUG_ID`、`TEST_SPEC`，缺省 `盐酸氨基葡萄糖胶囊` / `0.75g*60粒`

| 脚本                         | 覆盖                                                                 |
| ---------------------------- | -------------------------------------------------------------------- |
| `test_load_dotenv.ahk`       | `.env.local` 路径与 `PAC_API_*` 键                                   |
| `test_pac_api.ahk`           | 换票与目录单盒 `quantity`                                            |
| `test_txn_plan.ahk`          | `Txn_PlanPick` 整包 skip/full、拆零余数、已扫、`WHOLE_BOX_PENDING`   |
| `test_txn_service.ahk`       | 拆零提交与回滚、`bySpec` 拆零计划、整盒预留；对照预留返回体。A 提交 1 粒 |
| `test_cleanup_pending.ahk`   | `txn/cleanup`；未超时 PENDING 不动                                   |
| `test_msfx_code.ahk`         | 取码 MAX/MIN、码归一、leaf/staging 分组                              |
| `test_msfx_fingerprint.ahk`  | 仓库行指纹                                                           |
