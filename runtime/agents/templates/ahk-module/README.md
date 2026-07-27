# AHK Module 模板

此目录是非发布模板，不会被 Agents CI 扫描或打进 Desktop。

## 新建模块

1. 将整个目录复制到 `runtime/agents/modules/<source-name>/`，源码目录沿用小写命名。
2. 选定正式 Module Id，并同步替换 `module.json`、`main.ahk` 与 `release-manifest.json` 中的 `ModuleTemplate`；EXE 文件名使用相同大小写的 Id 加 `.exe`。
3. 修改 `main.ahk` 中的 Ahk2Exe 元数据和测试逻辑。
4. 替换 `assets/module.ico`。
5. 根据业务字段修改 `settings.json` 与 `settings.schema.json`。
6. 在仓库根目录 `release-manifest.json` 的 `components.agents.modules` 中登记模块版本。

模块 Id 首字符须为 ASCII 字母或数字，其余仅允许字母、数字、`.`、`_`、`-`。

## 模板测试行为

- 从 Host 参数 `--module-settings` 读取用户业务配置。
- 自检成功后写入 `module.ready`，Desktop 随即显示 Running。
- 按 `Ctrl+Alt+F8` 显示当前配置文件路径和内容摘要。
- Host 停止模块时进程退出，并清理 `module.ready`。

CI 会用 Ahk2Exe 编译 `main.ahk`，并将 EXE、清单、默认设置和 Schema 一并放入 Desktop 的 `Agents/Modules/<ModuleId>/`。
