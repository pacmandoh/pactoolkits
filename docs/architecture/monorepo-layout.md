# Monorepo 布局

PacToolkits 使用单仓库组织 Desktop、Agents 运行时、数据库和共享 .NET 包。本文定义各目录的职责与依赖边界。

## 顶层结构

```text
pactoolkits/
  apps/
    desktop-avalonia/src/     Desktop（Avalonia 12 与 ShadUI）
    api-asp/src/              HTTP 宿主 PacToolkits.Api（API Key 换 JWT）
  packages/
    core/                     纯领域模型与跨层工具（无 I/O）
    application/              用例层：DTO、服务接口、应用服务
    infrastructure/           外部实现：PostgreSQL、仓储实现
    agents-contracts/         Desktop 与 Agents 共享协议
    logger/                   共享 JSON Lines 文件日志（级别、滚动、写盘）
  runtime/
    agents/
      host/                   Host（Agents.exe，.NET 入口）
      modules/                参与发布构建的模块源码
      templates/              模块开发模板，不参与发布构建
      README.md
      docs/
  database/
    postgres/                 Schema bootstrap、migration、verify、deploy
  scripts/                    版本、打包、发布脚本
  docs/                       架构与运维文档
  .github/workflows/          持续集成与发布工作流
  PacToolkits.sln             .NET 解决方案入口
  release-manifest.json       版本与兼容性以该清单为准
```

## 各区域职责

| 路径                        | 角色            | 说明                                                                     |
| --------------------------- | --------------- | ------------------------------------------------------------------------ |
| `apps/desktop-avalonia`     | **Desktop**     | 业务 UI、配置、更新；Agents：**OS Host**、desired、Snapshot 投影         |
| `apps/api-asp`              | **API**         | HTTP 宿主；具名客户端 Key 换 JWT；域用例走 Application 与 Infrastructure |
| `runtime/agents/host`       | **Agents Host** | `Agents.exe`：desired reconcile、模块监管、Snapshot / moduleFailed       |
| `runtime/agents/modules`    | **Agents 模块** | 独立自动化进程、描述文件、默认配置与 settings schema                     |
| `runtime/agents/templates`  | **模块模板**    | 新模块起点；不参与运行时发现与打包                                       |
| `database/postgres`         | **数据库**      | SQL 与部署脚本；schema 演进与校验                                        |
| `packages/core`             | **纯业务核心**  | 无数据库、文件、日志、配置、桌面端依赖                                   |
| `packages/application`      | **用例层**      | 页面/功能对应的应用服务与抽象                                            |
| `packages/infrastructure`   | **基础设施**    | Npgsql、仓储、DB 连接与 schema 版本读取                                  |
| `packages/agents-contracts` | **Agents 协议** | IPC/Snapshot/路径；`IAgentsRuntime` ≠ `IAgentsClient`；零依赖            |
| `packages/logger`           | **共享日志**    | JSON Lines；Desktop / Host 共用                                          |

## .NET 解决方案

`PacToolkits.sln` 包含：

- `PacToolkits.Desktop.Avalonia`
- `PacToolkits.Api`
- `PacToolkits.Core`
- `PacToolkits.Application`
- `PacToolkits.Infrastructure`
- `PacToolkits.Agents.Contracts`
- `PacToolkits.Logger`
- `PacToolkits.Agents.Host`
- `PacToolkits.Desktop.Tests`
- `PacToolkits.Desktop.UiTests`
- `PacToolkits.Agents.Contracts.Tests`
- `PacToolkits.Api.Tests`

## 配置与版本

- Desktop 全局配置由 `AppConfigStore` 读写（PostgreSQL、Host 路径、模块启用键）
- 模块 catalog 与启用扩容来自 Host Snapshot，非配置加载扫盘
- 模块业务配置：`{ConfigDir}/agents/modules/<Id>/settings.json`
- Desktop 起 Host：`--config <绝对路径>`；模块挂载走管道 desired
- 版本以 `release-manifest.json` 为准；`scripts/export-version.sh` 同步各组件

## 相关文档

- [分层与依赖规则](./layering.md)
- [API 宿主与迁移](./api.md)
- [Agents 运行时架构](./agents.md)
- [Desktop 状态模型](./desktop-state.md)
- [发布流程](../operations/release-flow.md)
- [Desktop](../../apps/desktop-avalonia/README.md)
- [API](../../apps/api-asp/README.md)
- [Agents](../../runtime/agents/README.md)
- [PostgreSQL](../../database/postgres/README.md)
- [脚本工具](../../scripts/docs/tooling.md)
