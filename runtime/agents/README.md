# Agents 运行时

本目录包含 Agents Host、自动化模块和 AHK 模块模板。

```text
runtime/agents/
  host/                  .NET Host，发布为 Agents.exe
  lib/ahk/               AHK 模块共用源码（编译期 #Include，不随包发布；库内互引用 A_LineFile，勿裸文件名）
                         args / ready / log / ui / path / JSON / startup
  modules/               参与发布构建的模块源码
  templates/ahk-module/  新建 AHK 模块的起始模板
  docs/                  模块说明
```

## 日志

Host 与 AHK 模块写入 JSON Lines，目录与 Desktop 共用根路径：

```text
%AppData%/PacToolkits/logs/   # macOS/Linux：Application Support / .config
  desktop/YYYY-MM-DD.log
  agents/host/YYYY-MM-DD.log
  agents/modules/<Id>/YYYY-MM-DD.log
```

字段对齐 Desktop：`ts`、`level`、`module`、`event`、`message`、`version`、`context?`、`exception?`。

| 字段 | 含义 |
| --- | --- |
| `level` | 严重度，仅 `Debug` / `Info` / `Warn` / `Error` / `Fatal`（PascalCase，与 Desktop 一致） |
| `event` | 事件分类短名（原 Injector 结果里的 `type` / `[预留错误]` 一类归这里，用英文短名如 `reserve.fail`） |
| `message` | 人类可读正文（原 `why`） |
| `context` | 结构化附加字段（窗口、txn、数量等） |
| `exception` | 可选；真实异常时的 type / message / stackTrace |

控制面：

- **Desktop + Host**：`PacToolkits.Desktop.config.json` → `Logging.Enabled` / `MinimumLevel` / `RetentionDays` / `MaxFileSizeMb`（设置页「日志与诊断」）；落盘经 `packages/logger`（`JsonLogWriter`）
- **模块**：各模块用户 `settings.json` → `LogEnabled` / `LogMinimumLevel` / `LogRetentionDays` / `LogMaxFileSizeMb`（设置页「模块配置」；AHK `Log_ApplySettings` → `lib/ahk/log.ahk`）
- 文件名一致：`YYYY-MM-DD[.N].log`（靠目录区分 Desktop / Host / 模块）
- Host 关键生命周期：`host.ready` / `host.quit` / `host.shutdown`；模块 `host.module.start` / `host.module.stop` / `host.module.exited`（另有 `discovered` / `removed`）

## 运行模型

Agents 的发布单元由一个常驻 Host 和多个独立模块进程组成：

- Desktop 启动 Host，并通过控制文件下发 Host 与模块命令
- Host 发现 `Modules/<Id>/module.json`，按 `entry.win-x64` 启动模块
- 模块自检完成后创建 `module.ready`
- Desktop 结合进程状态和 `module.ready` 展示模块状态

Host 启动后不会自行挂载模块。Desktop 会根据 `Agents.Modules[id].Enabled` 挂载已启用模块，也可以在运行期间单独启动或停止模块。

## 发布布局

```text
Agents/
  Agents.exe
  ReleaseManifest.json
  host.control
  Modules/
    <Id>/
      module.json
      settings.json
      settings.schema.json
      module.control
      module.ready
      <Entry>.exe
```

`settings.json` 是随模块发布的默认配置。Desktop 首次使用模块时，将其复制到 `{ConfigDir}/agents/modules/<Id>/settings.json`；后续设置修改仅写入用户配置。

## 模块发现与更新

Desktop 和 Host 都会定期扫描模块描述文件，因此运行期间可以发现新增或移除的模块目录。发现模块不等于启动模块。

Desktop 还会监视正在运行的入口二进制：

- Host 二进制更新后重启 Host
- 模块二进制更新后只重启对应模块
- 未运行模块更新后不自动启动

文件变化须连续两次观测稳定后才会触发重启，以避开复制过程中的临时状态。

## 本地验证

在 Desktop 输出目录生成与安装包一致的 Agents 目录布局：

```bash
./scripts/run-desktop-with-agents.sh --configuration Release --stage-only
```

Windows（Git Bash / MSYS）上若探测到本机 `Ahk2Exe.exe` 与 `AutoHotkey64.exe`，会对 AHK 模块做真实编译并写入 staging（同时刷新 `artifacts/agents/win-x64/Modules/`）。可用 `AHK2EXE_PATH` / `AHK_BASE_PATH` 覆盖路径。未找到编译器时回退到 artifacts 或模块目录下已有入口文件。

macOS 和 Linux 无法编译或运行 Windows AHK 模块；脚本仍会复制 Host、模块描述文件、默认设置和 schema，用于验证发现与设置页行为。

验证已由 Windows CI 生成的 Agents staging：

```bash
./scripts/release-agents.sh \
  --artifact-dir artifacts/agents/win-x64 \
  --skip-upload \
  --dry-run
```

## 新建模块

复制 [`templates/ahk-module/`](templates/ahk-module/) 后，按模板说明更新模块 ID、入口、图标、默认设置和发布清单。模板目录不参与运行时模块发现或 CI 编译。

## 相关文档

- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Injector 模块](./docs/injector.md)
- [发布流程](../../docs/operations/release-flow.md)
- [脚本工具](../../scripts/docs/tooling.md)
