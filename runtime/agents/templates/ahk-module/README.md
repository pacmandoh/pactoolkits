# AHK 模块模板

该目录提供 AHK 模块的最小可运行结构，不参与模块发现、发布构建或 Desktop 打包。

## 创建模块

1. 将目录复制到 `runtime/agents/modules/<source-name>/`，源码目录使用小写名称
2. 确定稳定的模块 ID，并更新 `module.json`、`main.ahk` 和根目录 `release-manifest.json`
3. 将 `entry.win-x64` 设置为模块发布文件名，并同步 Ahk2Exe 元数据
4. 替换 `assets/module.ico`
5. 根据业务需求更新 `settings.json` 和 `settings.schema.json`
6. 在 `components.agents.modules` 中登记模块版本

模块 ID 首字符必须为 ASCII 字母或数字，其余字符仅允许 ASCII 字母、数字、`.`、`_`、`-`。模块 ID、发布目录名和 `module.json` 中的 `id` 必须完全一致。

## 模板行为

模板入口演示以下运行时契约：

- `Log_Startup`、`Ready_Install`、`Settings_RequireJson`（`lib/ahk/startup.ahk` / `ready.ahk`）
- 配置读取成功后创建 `module.ready`，退出时删除（`Ready_Mark` / `Ready_Clear`）
- JSON 日志写入 `%LocalAppData%\PacToolkits\logs\agents\modules\<Id>\`（`lib/ahk/log.ahk`）
- 提示 / 失败弹窗用 `UI_Tip` / `UI_Fail`（`lib/ahk/ui.ahk`；`UI_Fail` 的 `title` 每次必传）
- 使用 `Ctrl+Alt+F8` 显示测试信息

入口通过 `#Include "%A_ScriptDir%\..\..\lib\ahk\..."` 引用公共库；复制到 `modules/<name>/` 后相对路径仍有效。模板中的热键和消息仅用于开发验证，创建正式模块时应替换为业务逻辑。
## 构建要求

Windows CI 根据 `module.json` 选择 Ahk2Exe，编译 `main.ahk`，并将下列文件写入 `Agents/Modules/<Id>/`：

- `entry.win-x64` 指定的可执行文件
- `module.json`
- `settings.json`
- `settings.schema.json`

构建前应运行 Agents 契约测试，确认描述文件、默认配置和 schema 一致。
