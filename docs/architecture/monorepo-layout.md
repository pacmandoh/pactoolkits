# Monorepo 布局

PacToolkits 使用单仓库组织 Desktop、Agents 运行时、数据库和共享 .NET 包。本文定义各目录的职责与依赖边界。

## 顶层结构

```text
pactoolkits/
  apps/
    desktop-avalonia/src/     Desktop（Avalonia 12 + ShadUI）
  packages/
    core/                     纯领域模型与跨层工具（无 I/O）
    application/              用例层：DTO、服务接口、应用服务
    infrastructure/           外部实现：PostgreSQL、仓储实现
    agents-contracts/         Desktop ↔ Agents（Host + Modules）共享协议
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
  release-manifest.json       版本与兼容性的权威来源
```

## 各区域职责

| 路径                        | 角色            | 说明                                              |
| --------------------------- | --------------- | ------------------------------------------------- |
| `apps/desktop-avalonia`     | **Desktop**     | 业务 UI、配置、更新、诊断以及 Agents 运行控制     |
| `runtime/agents/host`       | **Agents Host** | `Agents.exe`；处理控制文件并监管模块进程          |
| `runtime/agents/modules`    | **Agents 模块** | 独立自动化进程及其描述文件、默认配置和设置 schema |
| `runtime/agents/templates`  | **模块模板**    | 新模块的开发起点，不参与运行时模块发现与打包      |
| `database/postgres`         | **数据库**      | SQL 与部署脚本；schema 演进与校验                 |
| `packages/core`             | **纯业务核心**  | 无数据库、文件、日志、配置、桌面端依赖            |
| `packages/application`      | **用例层**      | 页面/功能对应的应用服务与抽象                     |
| `packages/infrastructure`   | **基础设施**    | Npgsql、仓储、DB 连接与 schema 版本读取           |
| `packages/agents-contracts` | **Agents 协议** | 配置模型、路径、运行时抽象；Host 与 Desktop 共用  |

## .NET 解决方案

`PacToolkits.sln` 包含：

- `PacToolkits.Desktop.Avalonia`
- `PacToolkits.Core`
- `PacToolkits.Application`
- `PacToolkits.Infrastructure`
- `PacToolkits.Agents.Contracts`
- `PacToolkits.Agents.Host`
- `PacToolkits.Desktop.Tests`
- `PacToolkits.Desktop.UiTests`
- `PacToolkits.Agents.Contracts.Tests`

## 配置与版本

- Desktop 全局配置由 `AppConfigStore` 读写，包含 PostgreSQL、Host 路径和模块启用状态
- 模块业务配置独立存放在 `{ConfigDir}/agents/modules/<Id>/settings.json`
- Desktop 启动 Host 时传入 `--config <绝对路径>`；Host 将参数转发给模块，但不解析配置内容
- `release-manifest.json` 是发布版本来源，`scripts/export-version.sh` 将版本同步到各组件生成文件和模块描述文件

## 相关文档

- [分层与依赖规则](./layering.md)
- [Agents 运行时架构](./agents.md)
- [Desktop 状态模型](./desktop-state.md)
- [发布流程](../operations/release-flow.md)
- [Desktop](../../apps/desktop-avalonia/README.md)
- [Agents](../../runtime/agents/README.md)
- [PostgreSQL](../../database/postgres/README.md)
- [脚本工具](../../scripts/docs/tooling.md)
