# 发布流程

PacToolkits 的 Desktop、Agent、DB Schema 版本由 **`release-manifest.json`（schema v2）** 统一驱动。Electron preview **不参与**正式 Feed。

## 版本源（Manifest V2）

文件：[release-manifest.json](../../release-manifest.json)

| 字段 | 用途 |
|------|------|
| `product.version` | 产品总版本；Velopack `packVersion` |
| `components.desktop.version` | Desktop 组件版本 |
| `components.desktop.implementation` | `avalonia` / `electron` |
| `components.desktop.bundles` | 随 Desktop 发布的 Agent 组件 ID 列表 |
| `components.agent-injector-ahk.version` | AHK Agent 版本 |
| `components.database-postgres.version` | PostgreSQL migration 目标版本 |
| `release.channel` | 发布通道：`stable` / `beta` |

同步到各子项目：

```bash
./scripts/export-version.sh   # 生成 Version.g.props、version.generated.json 等
./scripts/check-version.sh    # 校验 manifest v2 与生成文件一致
./scripts/bump-version.sh     # 按规则 bump 版本（支持 --product/--desktop/--agent 及旧别名）
```

## CI 工作流（GitHub Actions）

触发：推送 `v*` tag 或手动 `workflow_dispatch`。

**Tag 规则：** 推送 tag 发布时，Git 标签必须形如 `vX.Y.Z`，且 `X.Y.Z` 与 `release-manifest.json` 的 `product.version` 完全一致（例如 tag `v0.17.1` ↔ manifest `0.17.1`）。`resolve-release-plan` 与 `publish-release` 会在 tag 发布时校验，不匹配则失败。

```text
release.yml
  ├─ resolve-release-plan.yml          读取 manifest，输出 implementation / artifact / mainExe / icon 等
  ├─ build-agent-injector-ahk.yml      所有 agent 组件（按 bundles）
  ├─ build-desktop-avalonia.yml        仅当 implementation=avalonia
  ├─ build-desktop-electron.yml        仅当 implementation=electron（需 npm run build:desktop）
  ├─ build-desktop-electron-preview.yml 非 electron 正式实现时的占位 CI
  ├─ package-desktop.yml               接收 resolve 参数，动态打包
  ├─ generate-release-notes.yml
  └─ publish-release.yml
```

本地解析发布计划：

```bash
./scripts/resolve-release-plan.sh
```

**路径约定（monorepo）：**

| 产物 | 路径 |
|------|------|
| Desktop 项目 | `apps/desktop-avalonia/src/` |
| Agent 源码 | `runtime/agents/injector-ahk/` |
| Agent CI staging | `artifacts/agents/agent-injector-ahk/win-x64/pactoolkits-injector.exe` |
| 安装包内 Agent | `Agents/injector/pactoolkits-injector.exe`（manifest `artifact.installDir` + `windows-x64`） |
| DB 脚本 | `database/postgres/` |

**Artifact 命名：**

- 正式 Desktop：`pactoolkits-desktop-win-x64-<product.version>`
- Avalonia 内部构建：`pactoolkits-desktop-avalonia-win-x64-<desktop.version>`
- Agent：`pactoolkits-injector-win-x64-<agent.version>`
- Electron preview（仅 CI 标记，不发布）：`pactoolkits-desktop-electron-preview-win-x64-<product.version>`

**命名分层（原则）：**

| 层级 | 规则 | 当前示例 |
|------|------|----------|
| 用户主程序 | 短名；**不带** Avalonia / Electron / AHK 等技术栈 | `pactoolkits-desktop.exe` |
| 安装包 | 通道 + Setup，长度适中 | `pactoolkits-stable-Setup.exe` |
| CI Artifact | 结构化、可较长；implementation 仅用于 CI/内部区分 | `pactoolkits-desktop-electron-preview-win-x64-0.17.1.zip` |
| 代码项目 / 程序集 | 保持完整语义 | `PacToolkits.Desktop.Avalonia` |
| Manifest 组件 ID | 内部标识，可含实现细节 | `agent-injector-ahk` |
| Agent 用户可见 exe | 短名、无技术栈 | `pactoolkits-injector.exe` |
| 安装目录 | 目录与 exe 不重复堆叠 | `Agents/injector/pactoolkits-injector.exe` |

同一发布中，除非需要用户主动区分两种实现（例如并存 Avalonia 与 Electron），否则不要把实现技术名写进最终用户程序名。CI Artifact 与 manifest 组件 ID 可以继续保留 implementation 信息。

## 本地发布命令

### Desktop（含 Agent 聚合）

```bash
./scripts/release-desktop.sh \
  --runtime win-x64 \
  --vpk-directive win \
  --upload-target user@host:/var/www/updates/pactoolkits
```

需先将 Agent 二进制放入 `artifacts/agents/agent-injector-ahk/win-x64/`。

Feed 按通道分子目录：`.../stable/`、`.../beta/`（`packId=pactoolkits` 不变）。

### Agent（独立 artifact）

```bash
./scripts/release-agent-injector-ahk.sh --artifact-dir artifacts/agents/agent-injector-ahk/win-x64 --skip-upload
```

### 数据库

```bash
cd database/postgres
cp scripts/config.example.json scripts/config.json
./scripts/deploy.sh doctor
./scripts/deploy.sh plan
./scripts/deploy.sh apply
```

## 客户端更新策略

- `AppUpdateService` 使用 Velopack 已安装版本作为当前版本
- Feed URL 解析为 `{FeedUrl}/stable` 或 `{FeedUrl}/beta`
- **不**自动跨通道切换

## Agent 路径解析

启动时 `AgentPathResolver` 按以下顺序解析（相对路径基于 Desktop 安装目录）：

1. **Legacy / 旧标准路径升级**：配置为 `Tools\pacinjector.exe` 或旧版 `Agents\agent-injector-ahk\pactoolkits-agent-injector-ahk.exe`，且 bundled 新标准 exe 存在 → 使用 `.\Agents\injector\pactoolkits-injector.exe` 并写回配置
2. **Configured**：其它配置路径且文件存在 → 使用配置路径（含用户自定义路径）
3. **Standard**：配置无效/文件不存在，但 bundled 标准 exe 存在 → 使用标准路径并按需写回配置
4. **Missing**：均不可用 → 启动失败

不再扫描磁盘上的 `Tools\pacinjector.exe` 作为兜底。停止/重启时仍会识别进程名 `pacinjector` 以结束旧进程。

## 相关文档

- [Monorepo 布局](../architecture/monorepo-layout.md)
- [根目录 README 发布章节](../../README.zh-CN.md)
