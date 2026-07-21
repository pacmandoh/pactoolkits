# 发布流程

PacToolkits 的 Desktop、Agents、DB Schema 版本由 **`release-manifest.json`（schema v2）** 统一驱动。

## 版本源（Manifest V2）

文件：[release-manifest.json](../../release-manifest.json)

| 字段                                           | 用途                                                       |
| ---------------------------------------------- | ---------------------------------------------------------- |
| `product.version`                              | 产品总版本；Velopack `packVersion`                         |
| `components.desktop.<impl>.version`            | Desktop 组件版本（当前 `<impl>` = `avalonia`）             |
| `components.agents.version`                    | Agents 容器版本                                            |
| `components.agents.modules.<Id>.version`       | 各 Agents 模块版本（如 `Injector`）                        |
| `components.database.postgres.version`         | PostgreSQL migration 目标版本                              |
| `components.database.postgres.migrationPolicy` | 数据库迁移策略：`stable-only` / `manual` / `isolated-beta` |
| `release.channel`                              | 发布通道：`stable` / `beta`                                |

同步到各子项目：

```bash
./scripts/export-version.sh   # Version.g.props、各处 ReleaseManifest.json、module.json 版本等
./scripts/check-version.sh    # 校验 manifest v2 与生成文件一致
./scripts/bump-version.sh     # 按规则 bump 版本
```

`export-version.sh` **不再**生成 Agents `ReleaseVersion.json`。Desktop 运行时版本服务读取的是安装目录下的 `ReleaseManifest.json`。`runtime/agents/host/ReleaseManifest.json` 只用于源码树与 `check-version` 对齐，Host 进程不读取。

## CI 工作流（GitHub Actions）

触发：推送 `v*` tag 或手动 `workflow_dispatch`。

**Tag 规则：** Stable 标签必须形如 `vX.Y.Z`，Beta 标签必须形如
`vX.Y.Z-beta.N`，并与 `release-manifest.json` 的 `product.version` 完全一致。
tag、version、channel 或 GitHub prerelease 标志不一致时，验证工作流会失败。

```text
release.yml
  ├─ validate-release.yml              校验 tag / channel / prerelease / Feed / DB policy
  ├─ resolve-release-plan.yml          读取 manifest，输出 implementation / artifact / mainExe / icon 等
  ├─ build-agents.yml      Agents 容器 + modules
  ├─ build-desktop-avalonia.yml        implementation=avalonia 时构建
  ├─ package-desktop.yml               接收 resolve 参数，动态打包
  ├─ generate-release-notes.yml
  └─ publish-release.yml
```

本地解析发布计划：

```bash
./scripts/resolve-release-plan.sh
```

**路径约定（monorepo）：**

| 产物              | 路径                                                                        |
| ----------------- | --------------------------------------------------------------------------- |
| Desktop 项目      | `apps/desktop-avalonia/src/`                                                |
| Agents 源码       | `runtime/agents/modules/injector/` + `runtime/agents/host/`                 |
| Agents CI staging | `artifacts/agents/win-x64/`（`Agents.exe` + `Modules/Injector/`）           |
| Host 发布方式     | framework-dependent + single-file（与 Desktop 一致，不嵌 .NET runtime）     |
| 安装包内 Agents   | `Agents/Agents.exe` + `Agents/Modules/Injector/`（manifest `installDir=.`） |
| DB 脚本           | `database/postgres/`                                                        |

**Artifact 命名：**

- 正式 Desktop：`pactoolkits-desktop-win-x64-<product.version>`
- Avalonia 内部构建：`pactoolkits-desktop-avalonia-win-x64-<desktop.version>`
- Agents CI artifact：`PacToolkits-Agents-<runtime>-<agents.version>`（无 channel）
- Agents 发布 zip：`PacToolkits-Agents-win-x64-<agents.version>-<channel>.zip`

**命名分层（原则）：**

| 层级              | 规则                                                               | 当前示例                                                         |
| ----------------- | ------------------------------------------------------------------ | ---------------------------------------------------------------- |
| 用户主程序        | 与 `AssemblyName` 一致；**不带** 技术栈后缀                        | `PacToolkits.Desktop.exe`                                        |
| 安装包            | 通道 + Setup，长度适中                                             | `PacToolkits-beta-Setup.exe` / `PacToolkits-stable-Setup.exe`    |
| CI Artifact       | 结构化、可较长；implementation 仅用于 CI/内部区分                  | `pactoolkits-desktop-avalonia-win-x64-1.0.2-beta.5.zip`          |
| 代码项目 / 程序集 | 项目文件可含实现后缀；输出程序集用 Desktop 短名                    | 项目 `PacToolkits.Desktop.Avalonia` / 输出 `PacToolkits.Desktop` |
| Manifest 组件 ID  | 内部标识，可含实现细节                                             | `agents`                                                         |
| Host 用户可见 exe | 与 Agents 容器同名的入口短名（Host=`Agents.exe`）；容器只指 Agents | `Agents.exe`                                                     |
| 安装目录          | Host 在 Agents 容器根；模块在 `Modules/<id>/`                      | `Agents/Agents.exe`                                              |

不要把实现技术名写进最终用户程序名。CI Artifact 可继续保留 `avalonia` 标识。

## 本地发布命令

### Desktop（含 Agents 聚合）

```bash
./scripts/release-desktop.sh \
  --runtime win-x64 \
  --vpk-directive win \
  --upload-target user@host:/var/www/updates/pactoolkits
```

需先将 Agents 二进制放入 `artifacts/agents/win-x64/`。

Feed 按通道分子目录：`.../stable/`、`.../beta/`（小写；与 Velopack `--channel` / Setup 文件名一致，如 `PacToolkits-beta-Setup.exe`）。
Stable 和 Beta Feed 必须完全隔离；Beta GitHub Release 必须标记为 prerelease。

### Agents（独立 artifact）

先由 Windows CI/`build-agents.yml` 产出 staging 布局，再打包：

```bash
./scripts/release-agents.sh --artifact-dir artifacts/agents/win-x64 --skip-upload
```

要求目录含 `Agents.exe` 与 `Modules/Injector/`（含 `Injector.exe`、`module.json`）。

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

## Agents 路径解析与运行时

进程模型（Desktop → Host → Injector、`module.control` / `module.ready`）见 [Agents 运行时架构](../architecture/agents.md)。

启动时 `AgentsPath` 按以下顺序解析 Host 可执行文件（相对路径基于 Desktop 安装目录）：

1. **配置 Schema v2**：读入时将 Main/`SchemaVersion=1` 的 `AutomationTools`（`Ahk` 路径 + `Agent` 注入参数）收敛为单一 `Agents`（容器路径 + `Injector`），写回 `SchemaVersion=2`
2. **Main 路径升级**：配置为 Main 已发布的 `Tools\pacinjector.exe` → 写回 `.\Agents\Agents.exe`（不依赖本机是否已有新 Host 二进制）
3. **Configured**：其它配置路径且文件存在 → 使用配置路径（含用户自定义路径）
4. **Standard**：配置无效/文件不存在，但 bundled 标准 exe 存在 → 使用标准路径并按需写回配置
5. **Missing**：均不可用 → 启动失败

不再扫描磁盘上的 `Tools\pacinjector.exe` 作为兜底。停止/重启时仍会识别进程名 `pacinjector` 以结束旧进程。

Desktop 启动 Host 时附带 `--config <AppConfig 绝对路径>`；Host 转发给模块，自身不解析该文件。

## 相关文档

- [Monorepo 布局](../architecture/monorepo-layout.md)
- [Agents 运行时架构](../architecture/agents.md)
- [Beta 发布政策](beta-release-policy.md)
- [数据库兼容与回退政策](database-compatibility-policy.md)
- [脚本工具](../../scripts/docs/tooling.md)
- [Desktop](../../apps/desktop-avalonia/README.md)
- [Agents](../../runtime/agents/README.md)
- [PostgreSQL 运维](../../database/postgres/README.md)
