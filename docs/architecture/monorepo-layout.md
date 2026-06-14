# Monorepo 布局

PacToolkits 采用单仓库（monorepo）组织 UI、自动化运行时、数据库与共享 .NET 包。本文描述**当前**目录职责，而非历史路径。

## 顶层结构

```text
pactoolkits/
  apps/
    desktop-avalonia/src/     当前正式 UI（Avalonia）
    desktop-electron/         未来 Nuxt + Electron Preview（空壳，不参与 release）
  packages/
    core/                     纯领域 / 跨层工具（无 IO）
    application/              用例层：DTO、服务接口、应用服务
    infrastructure/           外部实现：PostgreSQL、仓储实现
    agent-contracts/          UI ↔ Agent 共享协议与校验
  runtime/
    agent-ahk/                Win7 / 旧机器兼容的 AHK 自动化运行时
  database/
    postgres/                 Schema bootstrap、migration、verify、deploy
  scripts/                    版本、打包、发布脚本
  docs/                       架构与运维文档
  .github/workflows/          CI / Release（不含 Electron）
  PacToolkits.sln             .NET 解决方案入口
  release-manifest.json       版本与兼容性唯一事实来源
```

## 各区域职责

| 路径 | 角色 | 说明 |
|------|------|------|
| `apps/desktop-avalonia` | **当前正式 UI** | Avalonia 桌面客户端；用户操作、配置、更新、诊断 |
| `apps/desktop-electron` | **未来 preview** | 仅占位；不连库、不替代 Avalonia、不进 release |
| `runtime/agent-ahk` | **Agent 运行时** | AutoHotkey v2；解析、注入、验证、任务执行 |
| `database/postgres` | **数据库** | SQL 与部署脚本；schema 演进与校验 |
| `packages/core` | **纯业务核心** | 无数据库、文件、日志、配置、UI 依赖 |
| `packages/application` | **用例层** | 页面/功能对应的应用服务与抽象 |
| `packages/infrastructure` | **基础设施** | Npgsql、仓储、DB 连接与迁移实现 |
| `packages/agent-contracts` | **Agent 协议** | 配置模型、命令/事件、运行时抽象 |

## .NET 解决方案

`PacToolkits.sln` 包含：

- `PacToolkits.Desktop.Avalonia`
- `PacToolkits.Core`
- `PacToolkits.Application`
- `PacToolkits.Infrastructure`
- `PacToolkits.Agent.Contracts`

`apps/desktop-electron` **不在** solution 内，也不参与 `dotnet build PacToolkits.sln`。

## 配置与版本

- 统一配置文件由桌面端 `AppConfigStore` 读写（Postgres + AutomationTools 等）
- Agent 通过 `--config` 启动参数读取同一份 JSON
- 所有发布版本以 `release-manifest.json` 为准，经 `scripts/export-version.sh` 同步到各子项目

## 相关文档

- [分层与依赖规则](./layering.md)
- [Avalonia 抽离计划](../migration/avalonia-extraction-plan.md)
- [发布流程](../operations/release-flow.md)
