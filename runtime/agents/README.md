# Agents 运行时

本目录包含 Agents Host、自动化模块和 AHK 模块模板。

```text
runtime/agents/
  host/                  .NET Host，发布为 Agents.exe
  lib/ahk/               AHK 共用源码（编译期 #Include，不随包发布；库内互引用 A_LineFile）
                         args / ready / log / ui / path / JSON / startup
  modules/               参与发布构建的模块源码
  templates/ahk-module/  新建 AHK 模块起始模板
  docs/                  模块说明
```

架构全文见 [docs/architecture/agents.md](../../docs/architecture/agents.md)（三边界：Contracts / Host / Desktop 侧）。

## 运行模型

| 角色 | 职责 |
| --- | --- |
| **Desktop** | CreateProcess / 超时强杀 Host 树；会话 desired；投影 Snapshot；**Host 启停闸门**可按入口清模块孤儿 |
| **Host** | 扫 `module.json`、desired reconcile、ready/崩溃/模块热更、推 Snapshot |
| **Module** | 业务自动化；写 `module.ready`；不跑 Desktop 监管 |

- 模块挂载意图 = **desired 集合**（持续意图，非 control 文件 start/stop）
- Desktop **运行时不** Kill 模块 PID；日常不扫盘 catalog
- 无 `host.control` / `module.control`；status **仅 schema v2**
- 主路径走命名管道；`host.desired.json` / `host.status.json` 为冷启动种子与诊断镜像

## 日志

Host 与 AHK 与 Desktop 共用日志根，JSON Lines：

```text
%LocalAppData%/PacToolkits/logs/   # macOS：Application Support；Linux：~/.local/share
  desktop/YYYY-MM-DD.log
  agents/host/YYYY-MM-DD.log
  agents/modules/<Id>/YYYY-MM-DD.log
```

| 字段 | 含义 |
| --- | --- |
| `level` | `Debug` / `Info` / `Warn` / `Error` / `Fatal` |
| `event` | 英文短名（如 `reserve.fail`） |
| `message` | 可读正文 |
| `context` | 结构化附加 |
| `exception` | 可选异常 |

- **Desktop 与 Host**：`Logging.*`（`JsonLogWriter`）
- **模块**：用户 `settings.json` 交给 `Log_ApplySettings`
- 文件名 `YYYY-MM-DD[.N].log`（靠目录区分来源）
- Host 事件例：`host.ready` / `host.quit` / `host.module.start` / `.stop` / `.exited`

## 发布布局

```text
Agents/
  Agents.exe
  ReleaseManifest.json
  host.desired.json
  host.status.json
  Modules/<Id>/...
```

管道名：`PacToolkits.Agents.{sha256(AgentsDir)[0..8]}`（`AgentsIpc.PipeName`）。

用户模块配置：`{ConfigDir}/agents/modules/<Id>/settings.json`（首次从包内默认复制）。

## 发现、热更与 catalog

- Host 扫盘写入 Snapshot catalog，再由 Desktop UI / 设置页消费
- Desktop **不**在 poll/`Reload` 扫 `Modules/` 发现  
- 发现 ≠ 启动；enabled 且 desired 才挂载
- Host.exe 热更：Desktop；模块入口热更：Host
- 未运行模块文件变了不自动起

macOS/Linux：可 stage Host 与描述文件做设置页布局；**无 Windows Host 则无 Snapshot catalog**，模块配置区为空（正常）。

## 本地验证

```bash
./scripts/run-desktop-with-agents.sh --configuration Release --stage-only
```

Windows 若有 `Ahk2Exe` / `AutoHotkey64` 会真编译 AHK（`AHK2EXE_PATH` / `AHK_BASE_PATH` 可覆盖）。

```bash
./scripts/release-agents.sh \
  --artifact-dir artifacts/agents/win-x64 \
  --dry-run
```

## 新建模块

复制 [`templates/ahk-module/`](templates/ahk-module/)，按模板改 id/入口/图标/默认设置/清单。模板不参与 CI 模块发现。

## 相关文档

- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Injector 模块](./docs/injector.md)
- [发布流程](../../docs/operations/release-flow.md)
- [脚本工具](../../scripts/docs/tooling.md)
