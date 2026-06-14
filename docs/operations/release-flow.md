# 发布流程

PacToolkits 的 UI、Agent、DB Schema 版本由 **`release-manifest.json`** 统一驱动。Electron preview **不参与**本流程。

## 版本源

文件：[release-manifest.json](../../release-manifest.json)

| 字段 | 用途 |
|------|------|
| `suiteVersion` | 套件总版本；UI Velopack `packVersion` |
| `uiVersion` | 桌面 UI 组件版本 |
| `agentVersion` | AHK Agent / `pacinjector.exe` 版本 |
| `dbSchemaVersion` | PostgreSQL migration 目标版本 |
| `compat.uiMinDbSchema` | UI 最低可连 DB schema |
| `compat.agentMinDbSchema` | Agent 最低可连 DB schema |
| `build.channel` | 发布通道：`stable` / `beta` |

同步到各子项目：

```bash
./scripts/export-version.sh   # 生成 Version.g.props、version.generated.json 等
./scripts/check-version.sh    # 校验 manifest 与生成文件一致
./scripts/bump-version.sh     # 按规则 bump 版本
```

## CI 工作流（GitHub Actions）

触发：推送 `v*` tag 或手动 `workflow_dispatch`。

```text
release.yml
  ├─ release-build-agent.yml    编译 pacinjector.exe → apps/desktop-avalonia/src/Tools/
  ├─ release-build-ui.yml       依赖 agent 产物；Velopack 打包 UI
  ├─ release-generate-notes.yml 生成 Release Notes（仅 tag）
  └─ release-publish-assets.yml 上传产物（仅 tag）
```

**路径约定（monorepo）：**

| 产物 | 路径 |
|------|------|
| UI 项目 | `apps/desktop-avalonia/src/` |
| Agent 源码 | `runtime/agent-ahk/` |
| 内置 Agent 二进制 | `apps/desktop-avalonia/src/Tools/pacinjector.exe` |
| DB 脚本 | `database/postgres/` |

`apps/desktop-electron` 与 `packages/*` 库项目**不**单独发布 NuGet；它们随 UI 程序集一并编译。

## 本地发布命令

### UI

```bash
./scripts/release-ui.sh \
  --runtime win-arm64 \
  --vpk-directive win \
  --upload-target user@host:/var/www/updates/pactoolkits
```

Feed 按通道分子目录：`.../stable/`、`.../beta/`。

### Agent

```bash
./scripts/release-agent.sh \
  --upload-target user@host:/var/www/updates/pactoolkits-agent/
```

或 dry-run：

```bash
./scripts/release-agent.sh --skip-upload --dry-run
```

### 数据库

```bash
cd database/postgres
cp scripts/config.example.json scripts/config.json
# 编辑 config.json
./scripts/deploy.sh doctor
./scripts/deploy.sh plan
./scripts/deploy.sh apply   # 按环境策略执行
```

## 客户端更新策略

- `AppUpdateService` 使用 Velopack 已安装版本作为当前版本
- Feed URL 解析为 `{FeedUrl}/stable` 或 `{FeedUrl}/beta`
- **不**自动跨通道切换；通道不一致时提示用户安装目标通道的最新安装包

## 与 Monorepo 分层的关系

- Release **只**打包 Avalonia UI + 内嵌 Agent 二进制 + manifest 版本元数据
- Application / Infrastructure 作为 UI 的项目引用编译进主程序，无独立发布包
- Schema 变更通过 `database/postgres/sql/migrations/` 独立部署，版本号写入 manifest

## 相关文档

- [Monorepo 布局](../architecture/monorepo-layout.md)
- [根目录 README 发布章节](../../README.zh-CN.md#发布与更新链路)
