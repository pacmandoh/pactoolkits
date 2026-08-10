# 发布流程

`release-manifest.json`（Manifest V2）是 Desktop、Agents、API 与数据库 schema 的统一版本清单。

## 版本清单

文件：[release-manifest.json](../../release-manifest.json)

| 字段                                     | 用途                                |
| ---------------------------------------- | ----------------------------------- |
| `product.version`                        | 产品总版本；Velopack `packVersion`  |
| `components.desktop.avalonia.version`    | Avalonia Desktop 组件版本           |
| `components.agents.version`              | Agents 容器版本                     |
| `components.agents.modules.<Id>.version` | 各 Agents 模块版本（如 `Injector`） |
| `components.agents.modules.<Id>.minDbSchema` / `maxDbSchema` | 可选；依赖库模块的 schema 闭区间；export 写入对应 `module.json` |
| `components.api.version` | API 制品版本；export 写入 `apps/api-asp/src/Version.g.props` |
| `components.api.contractVersion` | HTTP 协议 SemVer；export 写入 `ApiContract.g.cs`（协议字段；与制品版本解耦） |
| `components.api.minDbSchema` / `maxDbSchema` | API 宿主 SchemaBounds；export 写入 `SchemaBounds.g.cs` 与 `appsettings.json` |
| `components.database.postgres.version`   | PostgreSQL migration 目标版本       |
| `release.channel`                        | 发布通道：`stable` 或 `beta`        |

使用下列脚本维护和验证派生文件：

```bash
./scripts/export-version.sh   # 按清单生成各产物版本文件与 module/API 字段
./scripts/check-version.sh    # 校验清单与派生文件一致
./scripts/bump-version.sh     # 按参数改清单字段
```

`bump-version.sh` 常用参数（可单条或多条组合；细则见 `--help`）：

| 改什么 | 参数 |
|--------|------|
| 组件制品版本 | `--component api=…` / `agents=…` 等 |
| Desktop / DB | `--desktop` / `--db` |
| API 协议 | `--api-contract X.Y.Z` |
| Desktop 与 API 的 schema 闭区间 | `--component-min-db` / `--component-max-db desktop\|api=…`（Desktop 亦可用 `--desktop-min-db` / `--desktop-max-db`） |
| 单模块 version | `--module Injector=…` |
| 单模块 schema 区间 | `--module-min-db` / `--module-max-db MODULE=…`（成对有效；皆缺=不依赖库） |
| Agents 与 Desktop 区间 | `--agents-min-desktop` / `--agents-max-desktop`（同通道显式 `--desktop` 不改区间；stable 切 beta 且未写区间时跟 Desktop；beta 上 product 代填 Desktop 且原为单点钉住时整段平移；宽区间则只补越界侧） |

`export-version.sh` 按产物写只读字段：

| 生成文件 | 内容 |
|----------|------|
| Desktop `ReleaseManifest.json` | product、desktop、agents.version、database、release |
| Agents Host `ReleaseManifest.json` | agents.version、minDesktop、maxDesktop、modules 各版本 |
| 各模块 `module.json` | version，以及可选的 minDbSchema、maxDbSchema |
| API | Version.g.props、ApiContract.g.cs、SchemaBounds.g.cs、appsettings 的 SchemaBounds |

Feed 发布全量 `release-manifest.json`（更新探测）。`check-version` 按各产物文件字段与清单逐项核对。源码树 `runtime/agents/host/ReleaseManifest.json` 供校验与打包；Host 进程不读该文件。

模块库区间在清单 `components.agents.modules.<Id>` 维护（与 version 同条；皆缺=不依赖库；成对则 X.Y.Z 且 min≤max），export 写入 `module.json`；运行时读安装树该文件。

API：`components.api` 的 `version`、`contractVersion`、min/maxDbSchema；协议与制品版本解耦。

## CI 工作流（GitHub Actions）

发布流程由 `v*` tag 或 `workflow_dispatch` 触发。

Stable tag 格式为 `vX.Y.Z`，Beta tag 格式为 `vX.Y.Z-beta.N`。tag 必须与 `product.version` 完全一致；tag、版本、通道或 GitHub prerelease 状态不一致时，发布验证失败。

```text
release.yml
  ├─ validate-release.yml              校验 tag、channel、prerelease、Feed 和数据库策略
  ├─ resolve-release-plan.yml          读取 manifest，输出 artifact、mainExe 和 icon 等参数
  ├─ build-agents.yml                  构建 Agents Host 与模块
  ├─ build-desktop-avalonia.yml        构建唯一的 Avalonia Desktop
  ├─ package-desktop.yml               接收 resolve 参数，动态打包
  ├─ generate-release-notes.yml
  └─ publish-release.yml
```

本地解析发布计划：

```bash
./scripts/resolve-release-plan.sh
```

**路径约定（monorepo）：**

| 产物              | 路径                                                                             |
| ----------------- | -------------------------------------------------------------------------------- |
| Desktop 项目      | `apps/desktop-avalonia/src/`                                                     |
| Agents 源码       | `runtime/agents/host/` 和 `runtime/agents/modules/*/`（按 `module.json` ID）     |
| Agents CI staging | `artifacts/agents/win-x64/`（`Agents.exe` 与 `Modules/<Id>/`，与 manifest 对齐） |
| Host 发布方式     | framework-dependent single-file，与 Desktop 共用目标机 .NET Runtime              |
| 安装包内 Agents   | `Agents/Agents.exe` 和 `Agents/Modules/<Id>/`（发布清单中的全部模块）            |
| DB 脚本           | `database/postgres/`                                                             |

**Artifact 命名：**

- Desktop 发布产物：`pactoolkits-desktop-win-x64-<product.version>`
- Avalonia 内部构建：`pactoolkits-desktop-avalonia-win-x64-<desktop.version>`
- Agents CI artifact：`PacToolkits-Agents-<runtime>-<agents.version>`（不包含 channel）
- Agents 发布 zip：`PacToolkits-Agents-win-x64-<agents.version>-<channel>.zip`

**命名规则：**

| 层级             | 规则                                            | 示例                                                            |
| ---------------- | ----------------------------------------------- | --------------------------------------------------------------- |
| 用户主程序       | 与 `AssemblyName` 一致；**不带** 技术栈后缀     | `PacToolkits.Desktop.exe`                                       |
| 安装包           | 使用通道名称和 Setup 后缀                       | `PacToolkits-beta-Setup.exe`、`PacToolkits-stable-Setup.exe`    |
| CI Artifact      | 使用结构化名称并保留 Avalonia 技术标识          | `pactoolkits-desktop-avalonia-win-x64-1.0.2-beta.5.zip`         |
| 代码项目与程序集 | 项目名可含实现后缀；输出程序集使用 Desktop 名称 | 项目 `PacToolkits.Desktop.Avalonia`，输出 `PacToolkits.Desktop` |
| Manifest 组件 ID | 内部标识，可含实现细节                          | `agents`                                                        |
| Host 可执行文件  | 使用 Agents 部署单元的入口短名                  | `Agents.exe`                                                    |
| 安装目录         | Host 在 Agents 容器根；模块在 `Modules/<id>/`   | `Agents/Agents.exe`                                             |

用户可见程序名不包含实现技术后缀；CI Artifact 可以保留 `avalonia` 等构建标识。

## 本地发布命令

### Desktop（含 Agents 聚合）

```bash
./scripts/release-desktop.sh \
  --runtime win-x64 \
  --vpk-directive win \
  --upload-target user@host:/var/www/updates/pactoolkits
```

执行前必须将 Agents 二进制放入 `artifacts/agents/win-x64/`。

Feed 按通道使用 `.../stable/` 和 `.../beta/` 子目录。目录名必须为小写，并与 Velopack `--channel` 及 Setup 文件名一致，例如 `PacToolkits-beta-Setup.exe`。
Stable 和 Beta Feed 必须完全隔离；Beta GitHub Release 必须标记为 prerelease。

### Agents（独立 Artifact）

首先由 Windows CI 的 `build-agents.yml` 生成 staging 布局，然后执行打包：

```bash
./scripts/release-agents.sh --artifact-dir artifacts/agents/win-x64 --skip-upload
```

Staging 目录必须包含 `Agents.exe` 和 `Modules/<Id>/`。每个模块目录必须包含 `module.json`、`settings.json`、`settings.schema.json` 以及 `entry.win-x64` 指定的可执行文件；目录集合必须与 `components.agents.modules` 完全一致。

本地验证 Desktop 与 Agents 聚合布局：

```bash
./scripts/run-desktop-with-agents.sh --configuration Release --stage-only
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

## 客户端更新策略

- `AppUpdateService` 使用 Velopack 已安装版本作为当前版本
- 启动时对齐配置通道与 Velopack 安装通道：检测到安装通道标记变化时自动同步；尚未记录标记且通道不一致时询问用户，确认后同步通道，取消后仅记录当前安装通道并保留手动选择
- Feed 可为 HTTP(S) 或内网共享/本地目录；解析为 `{FeedUrl}/stable` 或 `{FeedUrl}/beta`（本地路径用目录分隔符拼接），每通道目录含 `release-manifest.json`
- 每个通道目录发布 `release-manifest.json`，客户端每次发现具体更新版本时读取目标通道的 DB 兼容范围
- Stable 切换至 Beta 时，保存设置前需要风险确认；下载前重新读取 Feed、目标版本和当前数据库状态
- Beta 切换至 Stable 时，检查、下载和重启前均验证当前数据库；数据库版本高于 Stable 的 `maxDbSchema` 时阻止更新
- 通道切换只保存更新源选择，不触发数据库迁移或降级；兼容授权不作为长期配置保存
- `release-manifest.json` 的 `product.version` 必须与 Velopack 候选版本一致，否则客户端拒绝下载
- 已下载待安装包不保存长期授权；重启前重新读取当前通道 Manifest 并检查数据库 Schema
- 通道选择和自动检查开关即时保存；通道变化立即静默检查，自动检查开关只启停调度
- 用户触发立即更新时重新执行兼容性验证，并下载当时最新的兼容版本；候选版本变化不会重复要求通道风险确认
- 应用不会预下载更新；只有用户点击顶部更新入口或设置页立即更新后才会下载，并在下载完成后直接重启安装
- 设置页只读显示待更新版本、通道、Feed、数据库与 Schema 范围，安装包生命周期由更新服务管理

## 发布与数据库安全边界

- Desktop 只检查数据库兼容性，不执行初始化、迁移或降级
- 数据库变更必须通过服务器或受控运维节点上的 PostgreSQL 部署脚本执行
- Beta 数据库测试应使用与生产隔离的环境；实际 deploy 由运维流程控制
- 应用可以回退，数据库默认只前向演进
- 数据库高于 Stable `maxDbSchema` 时，Stable 必须停止写入，且不能切回该 Stable
- 禁止把数据库备份恢复当作普通版本回退；恢复备份仅用于经过审批的灾难恢复
- 已执行的 SQL migration 不得修改、删除或覆盖，只能追加新的前向 migration

完整决策与操作要求见：

- [Beta 发布规则](beta-release-policy.md)
- [数据库兼容与回退规则](database-compatibility-policy.md)

## Agents 路径解析与运行时

进程模型与 IPC/desired 协议见 [Agents 运行时架构](../architecture/agents.md)。

`AgentsPath` 按以下优先级解析 Host 可执行文件；相对路径以 Desktop 安装目录为基准：

1. **Configured**：配置路径有效且文件存在时使用配置路径
2. **Standard**：配置路径不可用但标准布局中的 `Agents.exe` 存在时使用标准路径，并按需规范化配置
3. **Missing**：两种路径均不可用时返回缺失状态，启动流程终止

`Agents.Modules` 不再由配置加载期扫盘扩删；catalog 与启用键 merge 来自 Host **StatusSnapshot**（见 [agents.md](../architecture/agents.md)）。配置层只规范化已有启用键形状。

模块默认配置首次复制到 `{ConfigDir}/agents/modules/<Id>/settings.json`。后续模块升级不会覆盖该用户文件。

Desktop 启动 Host：`--config <AppConfig 绝对路径>`；Host 转发给模块，自身不解析该文件。desired/quit 走管道；启停 Host 超时仅强杀 **Host 进程树**。

## 相关文档

- [Monorepo 布局](../architecture/monorepo-layout.md)
- [Agents 运行时架构](../architecture/agents.md)
- [Beta 发布规则](beta-release-policy.md)
- [数据库兼容与回退规则](database-compatibility-policy.md)
- [脚本工具](../../scripts/docs/tooling.md)
- [Desktop](../../apps/desktop-avalonia/README.md)
- [Agents](../../runtime/agents/README.md)
- [PostgreSQL 运维](../../database/postgres/README.md)
