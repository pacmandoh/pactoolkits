# Injector 模块

Injector 是 Agents 容器下的一个 **Module**（`runtime: ahk`），由 Host 按 `module.json` 的 `entry.win-x64` 启动。负责对目标窗口解析 / 注入 / 验证，以及与 PostgreSQL 任务队列同步。

进程关系与控制文件见 [Agents 运行时架构](../../../docs/architecture/agents.md)。

## 主要职责

- 解析目标窗口 Grid / 剪贴板内容
- 向目标系统执行追溯码注入
- 注入后验证结果
- 领取并执行仓库注入任务
- 将执行结果与事件回写 PostgreSQL
- 读取 `--config`（Postgres）与 `--module-settings`（业务字段）

## 启动与就绪

1. 必须带 `--config "<绝对路径>"`。
2. Host 追加 `--module-settings <ConfigDir>/agents/modules/Injector/settings.json`（必填）。
3. 用户 settings 由 Desktop 在首次需要时从模块模板 `Modules/Injector/settings.json` Copy-once。
4. 自检通过后写入 `module.ready`。

## 配置

| 文件 | 位置 | 作用 |
| ---- | ---- | ---- |
| `settings.json` | 模块包内（模板） | 默认业务字段 |
| `settings.schema.json` | 模块包内 | Desktop Settings 自动表单 |
| `settings.json` | `{ConfigDir}/agents/modules/Injector/` | 用户真相（Settings 编辑） |

业务字段由 `settings.schema.json` 描述；Desktop 启动校验走通用 `ModuleSettingsValidator`。

## 核心源码

相对 `runtime/agents/modules/injector/`：

- `main.ahk`、`module.json`、`settings.json`、`settings.schema.json`
- `src/utils.ahk`、`src/main_semi_auto.ahk`、`src/msfx_task.ahk` 等

## 热键门控

`#HotIf Util_HotIf_TargetApp()`：前台进程 ∈ `AppWin`，窗口类 = `OptWindowClass` 或 `IptWindowClass`（仓库模式仅住院类）。详见源码与 Settings schema 字段说明。
