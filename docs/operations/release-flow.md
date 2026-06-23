# 发布流程

PacToolkits 的 Desktop、Agent、DB Schema 版本由 **`release-manifest.json`（schema v2）** 统一驱动。

## 版本源（Manifest V2）

文件：[release-manifest.json](../../release-manifest.json)

| 字段                                           | 用途                                                       |
| ---------------------------------------------- | ---------------------------------------------------------- |
| `product.version`                              | 产品总版本；Velopack `packVersion`                         |
| `components.desktop.version`                   | Desktop 组件版本                                           |
| `components.desktop.implementation`            | `avalonia` / `electron`                                    |
| `components.desktop.bundles`                   | 随 Desktop 发布的 Agent 组件 ID 列表                       |
| `components.agent-injector-ahk.version`        | AHK Agent 版本                                             |
| `components.database-postgres.version`         | PostgreSQL migration 目标版本                              |
| `components.database-postgres.migrationPolicy` | 数据库迁移策略：`stable-only` / `manual` / `isolated-beta` |
| `release.channel`                              | 发布通道：`stable` / `beta`                                |

同步到各子项目：

```bash
./scripts/export-version.sh   # 生成 Version.g.props、version.generated.json 等
./scripts/check-version.sh    # 校验 manifest v2 与生成文件一致
./scripts/bump-version.sh     # 按规则 bump 版本
```

## CI 工作流（GitHub Actions）

触发：推送 `v*` tag 或手动 `workflow_dispatch`。

**Tag 规则：** Stable 标签必须形如 `vX.Y.Z`，Beta 标签必须形如
`vX.Y.Z-beta.N`，并与 `release-manifest.json` 的 `product.version` 完全一致。
tag、version、channel 或 GitHub prerelease 标志不一致时，验证工作流会失败。

```text
release.yml
  ├─ validate-release.yml              校验 tag / channel / prerelease / Feed / DB policy
  ├─ resolve-release-plan.yml          读取 manifest，输出 implementation / artifact / mainExe / icon 等
  ├─ build-agent-injector-ahk.yml      所有 agent 组件（按 bundles）
  ├─ build-desktop-avalonia.yml        仅当 implementation=avalonia
  ├─ build-desktop-electron.yml        仅当 implementation=electron（需 npm run build:desktop）
  ├─ package-desktop.yml               接收 resolve 参数，动态打包
  ├─ generate-release-notes.yml
  └─ publish-release.yml
```

本地解析发布计划：

```bash
./scripts/resolve-release-plan.sh
```

**路径约定（monorepo）：**

| 产物             | 路径                                                                                         |
| ---------------- | -------------------------------------------------------------------------------------------- |
| Desktop 项目     | `apps/desktop-avalonia/src/`                                                                 |
| Agent 源码       | `runtime/agents/injector-ahk/`                                                               |
| Agent CI staging | `artifacts/agents/agent-injector-ahk/win-x64/pactoolkits-injector.exe`                       |
| 安装包内 Agent   | `Agents/injector/pactoolkits-injector.exe`（manifest `artifact.installDir` + `windows-x64`） |
| DB 脚本          | `database/postgres/`                                                                         |

**Artifact 命名：**

- 正式 Desktop：`pactoolkits-desktop-win-x64-<product.version>`
- Avalonia 内部构建：`pactoolkits-desktop-avalonia-win-x64-<desktop.version>`
- Agent：`pactoolkits-injector-win-x64-<agent.version>`

**命名分层（原则）：**

| 层级               | 规则                                              | 当前示例                                          |
| ------------------ | ------------------------------------------------- | ------------------------------------------------- |
| 用户主程序         | 短名；**不带** Avalonia / Electron / AHK 等技术栈 | `pactoolkits-desktop.exe`                         |
| 安装包             | 通道 + Setup，长度适中                            | `pactoolkits-stable-Setup.exe`                    |
| CI Artifact        | 结构化、可较长；implementation 仅用于 CI/内部区分 | `pactoolkits-desktop-avalonia-win-x64-0.17.1.zip` |
| 代码项目 / 程序集  | 保持完整语义                                      | `PacToolkits.Desktop.Avalonia`                    |
| Manifest 组件 ID   | 内部标识，可含实现细节                            | `agent-injector-ahk`                              |
| Agent 用户可见 exe | 短名、无技术栈                                    | `pactoolkits-injector.exe`                        |
| 安装目录           | 目录与 exe 不重复堆叠                             | `Agents/injector/pactoolkits-injector.exe`        |

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
Stable 和 Beta Feed 必须完全隔离；Beta GitHub Release 必须标记为 prerelease。

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
./scripts/deploy.sh upgrade
./scripts/deploy.sh verify
```

### Beta 隔离数据库

Beta 迁移不得直接作用于共享生产库。发布 Beta 前，由运维人员显式克隆 Stable
数据库或恢复 Stable 备份；Desktop 不会自动执行这些脚本。

macOS / Linux：

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --template pactoolkits_production \
  --config database/postgres/scripts/config.json
```

Windows PowerShell：

```powershell
./scripts/create-beta-database.ps1 `
  -Version 0.18.0-beta.1 `
  -TemplateDatabase pactoolkits_production `
  -ConfigPath database/postgres/scripts/config.json
```

目标库按 `pactoolkits_beta_<版本>` 命名，`.`、`-`、`+` 转换为 `_`。同名库存在时
脚本立即失败且不会覆盖或删除。使用模板库时，创建前必须断开模板库的全部活跃连接。
创建成功后脚本写入 `Database.Environment=isolated`、
`Database.AllowBetaMigrations=true`、克隆来源和 Beta 版本标记，并输出不含密码的连接串。

详细参数见 [PostgreSQL 运维说明](../../database/postgres/README.md)。

## 客户端更新策略

- `AppUpdateService` 使用 Velopack 已安装版本作为当前版本
- Feed URL 解析为 `{FeedUrl}/stable` 或 `{FeedUrl}/beta`
- 每个通道目录发布 `release-manifest.json`，客户端切换前读取目标通道的 DB 兼容范围
- Stable → Beta 需要风险确认、目标 Feed 可用且当前 DB 位于 Beta min/max 范围
- Beta → Stable 需要当前 DB 位于 Stable min/max 范围；高于 Stable max 时阻止切换
- 通道切换只保存更新源选择，不触发数据库迁移或降级

## 发布与数据库安全边界

- Beta 应用默认不能迁移共享生产数据库
- Beta 数据库测试必须使用隔离数据库
- `isolated-beta` 需要 `Database.Environment=isolated`、
  `Database.AllowBetaMigrations=true` 与对应的用户或 CI 显式授权
- `isolated-beta` 是开发/测试通道，不是生产升级通道
- 应用可以回退，数据库默认只前向演进
- 数据库高于 Stable `maxDbSchema` 时，Stable 必须停止写入，且不能切回该 Stable
- 禁止把数据库备份恢复当作普通版本回退；恢复备份仅用于经过审批的灾难恢复
- 已执行的 SQL migration 不得修改、删除或覆盖，只能追加新的前向 migration

完整决策与操作要求见：

- [Beta 发布政策](beta-release-policy.md)
- [数据库兼容与回退政策](database-compatibility-policy.md)

## Agent 路径解析

启动时 `AgentPathResolver` 按以下顺序解析（相对路径基于 Desktop 安装目录）：

1. **Legacy / 旧标准路径升级**：配置为 `Tools\pacinjector.exe` 或旧版 `Agents\agent-injector-ahk\pactoolkits-agent-injector-ahk.exe`，且 bundled 新标准 exe 存在 → 使用 `.\Agents\injector\pactoolkits-injector.exe` 并写回配置
2. **Configured**：其它配置路径且文件存在 → 使用配置路径（含用户自定义路径）
3. **Standard**：配置无效/文件不存在，但 bundled 标准 exe 存在 → 使用标准路径并按需写回配置
4. **Missing**：均不可用 → 启动失败

不再扫描磁盘上的 `Tools\pacinjector.exe` 作为兜底。停止/重启时仍会识别进程名 `pacinjector` 以结束旧进程。

## 相关文档

- [Monorepo 布局](../architecture/monorepo-layout.md)
- [Beta 发布政策](beta-release-policy.md)
- [数据库兼容与回退政策](database-compatibility-policy.md)
- [根目录 README 发布章节](../../README.zh-CN.md)
