# Monorepo 布局

PacToolkits 采用单仓库（monorepo）组织 Desktop、Agents 运行时、数据库与共享 .NET 包。本文描述**当前**目录职责，而非历史路径。

## 顶层结构

```text
pactoolkits/
  apps/
    desktop-avalonia/src/     当前正式 Desktop（Avalonia 12 + ShadUI）
  packages/
    core/                     纯领域 / 跨层工具（无 IO）
    application/              用例层：DTO、服务接口、应用服务
    infrastructure/           外部实现：PostgreSQL、仓储实现
    agents-contracts/         Desktop ↔ Agents（Host + Modules）共享协议
  runtime/
    agents/
      host/                   Host（Agents.exe，.NET 入口）
      modules/injector/       Injector 模块（AHK → Modules/Injector）
      README.md
      docs/
  database/
    postgres/                 Schema bootstrap、migration、verify、deploy
  scripts/                    版本、打包、发布脚本
  docs/                       架构与运维文档
  .github/workflows/          CI / Release
  PacToolkits.sln             .NET 解决方案入口
  release-manifest.json       版本与兼容性唯一事实来源
```

## 各区域职责

| 路径                              | 角色                 | 说明                                                                  |
| --------------------------------- | -------------------- | --------------------------------------------------------------------- |
| `apps/desktop-avalonia`           | **当前正式 Desktop** | 业务 UI、配置、更新、诊断；控制 Agents Host / Injector                |
| `runtime/agents/host`             | **Host（入口进程）** | `Agents.exe`；常驻；按 `module.control` 启停 Modules；Agents 才是容器 |
| `runtime/agents/modules/injector` | **Injector 模块**    | AutoHotkey v2；解析、录入、验证、仓库任务；发布为 `Modules/Injector`  |
| `database/postgres`               | **数据库**           | SQL 与部署脚本；schema 演进与校验                                     |
| `packages/core`                   | **纯业务核心**       | 无数据库、文件、日志、配置、桌面端依赖                                |
| `packages/application`            | **用例层**           | 页面/功能对应的应用服务与抽象                                         |
| `packages/infrastructure`         | **基础设施**         | Npgsql、仓储、DB 连接与迁移实现                                       |
| `packages/agents-contracts`       | **Agents 协议**      | 配置模型、路径、运行时抽象；Host 与 Desktop 共用                      |

## .NET 解决方案

`PacToolkits.sln` 包含：

- `PacToolkits.Desktop.Avalonia`
- `PacToolkits.Core`
- `PacToolkits.Application`
- `PacToolkits.Infrastructure`
- `PacToolkits.Agents.Contracts`
- `PacToolkits.Agents.Host`
- `PacToolkits.Desktop.Tests` / `PacToolkits.Desktop.UiTests` / `PacToolkits.Agents.Contracts.Tests`

## 配置与版本

- 统一配置由桌面端 `AppConfigStore` 读写（Postgres + `Agents` / `Injector` 等）
- Desktop 启动 Host 时传入 `--config <绝对路径>`；Host **转发**给模块，**自身不解析**该 JSON
- 所有发布版本以 `release-manifest.json` 为准，经 `scripts/export-version.sh` 同步到 Desktop / Agents 源码树中的 `ReleaseManifest.json`、`Version.g.props`、`module.json` 等

## 相关文档

- [分层与依赖规则](./layering.md)
- [Agents 运行时架构](./agents.md)
- [Desktop 状态模型](./desktop-state.md)
- [发布流程](../operations/release-flow.md)
- [Desktop](../../apps/desktop-avalonia/README.md)
- [Agents](../../runtime/agents/README.md)
- [PostgreSQL](../../database/postgres/README.md)
- [脚本工具](../../scripts/docs/tooling.md)
