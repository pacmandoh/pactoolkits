# Injector 模块

Injector 是 Agents 容器下的一个 **Module**（`runtime: ahk`），由 Host 按 `module.json` 的 `entry.windows-x64` 启动。负责对目标窗口解析 / 注入 / 验证，以及与 PostgreSQL 任务队列同步。

进程关系与控制文件见 [Agents 运行时架构](../../../docs/architecture/agents.md)。

## 主要职责

- 解析目标窗口 Grid / 剪贴板内容
- 向目标系统执行追溯码注入
- 注入后验证结果
- 领取并执行仓库注入任务
- 将执行结果与事件回写 PostgreSQL
- 读取 Desktop 生成的共享配置（`--config`，经 Host 转发）

## 启动与就绪

1. 必须带 `--config "<绝对路径>"`；缺失则退出。
2. 配置自检通过后写入同目录 `module.ready`（Desktop 以此判定 Injector「运行中」）。
3. 退出 / 重启前清除 ready 标记（见 `Util_ClearModuleReady`）。

## 核心源码

相对 `runtime/agents/modules/injector/`：

- `main.ahk` — 入口、热键、`module.ready`
- `module.json` — id / version / entry
- `src/main_semi_auto.ahk`、`src/msfx_task.ahk`
- `src/parse_clipboard.ahk`、`src/ui_txn.ahk`
- `src/db_txn.ahk`、`src/pg_exec.ahk`、`src/utils.ahk`

## 执行模型

- 门诊 / 住院（`opt` / `ipt`）走 AHK 半自动执行链
- 仓库模式走数据库任务队列
- parse / inject / verify / finalize 分模块
- 仓库重复注入防护与任务状态以数据库为准
- 窗口类、解析区、验证区、输入控件均用完整 `ClassNN` 配置，不做代码内拼接推导
- 仅 `WarehouseEnabled` 时做仓库列特征软校验
- 运行时元信息启动时初始化一次，复用于版本标识与客户端身份日志

## 热键门控

`#HotIf Util_HotIf_TargetApp()`（不可仅靠配置关掉）：

1. 前台窗口进程名 ∈ `AppWin`
2. 窗口类 = `OptWindowClass` **或** `IptWindowClass`（仓库模式：仅 `IptWindowClass`）

左 / 右键仍须落在配置的解析网格 `ClassNN` 上才会进入解析或注入。

**没有**「任意窗口都触发」的配置项；要对准测试窗，只能改窗口类与 `AppWin`。

## 关键配置字段

由 Desktop 写入 AppConfig 的 `Agents.Injector`（契约：`InjectorOptions`）：

- `OptWindowClass` / `IptWindowClass` — 门诊 / 住院顶层窗口类
- `OptParseGridClassNN` / `OptVerifyGridClassNN` — 门诊解析 / 验证网格
- `IptParseGridClassNN` / `IptVerifyGridClassNN` — 住院解析 / 验证网格
- `OptInputClassNN` / `IptInputClassNN` — 门诊 / 住院（含仓库）输入控件
- `AppWin` — 允许的目标 exe 名映射
- `WarehouseEnabled`、`WarehouseAnchorTexts`、`CodePickPolicy`、`WarehouseTaskIdentifier`
- `ColSpecs`、`IntCols`、`ConfirmTimeoutMs`、`PgDriver`、`PgSsl`

像 `TcxGridSite1` 这类 Grid `ClassNN`，运行时按「基类名 + 序号」解析，避免把尾部数字当成字面类名。

## 相关文档

- [Agents README](../README.md)
- [Agents 运行时架构](../../../docs/architecture/agents.md)
- [发布流程（Agents 路径解析）](../../../docs/operations/release-flow.md)
