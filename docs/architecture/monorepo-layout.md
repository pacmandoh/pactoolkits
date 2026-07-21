# Monorepo 布局

PacToolkits 采用单仓库（monorepo）组织 Desktop、自动化运行时、数据库与共享 .NET 包。本文描述**当前**目录职责，而非历史路径。

## 顶层结构

```text
pactoolkits/
  apps/
    desktop-avalonia/src/     当前正式 Desktop（Avalonia）
  packages/
    core/                     纯领域 / 跨层工具（无 IO）
    application/              用例层：DTO、服务接口、应用服务
    infrastructure/           外部实现：PostgreSQL、仓储实现
    agents-contracts/          Desktop ↔ Agents 共享协议与校验
  runtime/
    agents/
      host/                       Host（Agents 入口进程，挂载 Modules）→ Agents.exe
      modules/injector/          Injector 模块（发布为 Modules/Injector）
      README.md
  database/
    postgres/                 Schema bootstrap、migration、verify、deploy
  scripts/                    版本、打包、发布脚本
  docs/                       架构与运维文档
  .github/workflows/          CI / Release
  PacToolkits.sln             .NET 解决方案入口
  release-manifest.json       版本与兼容性唯一事实来源
```

## 各区域职责

| 路径                              | 角色                 | 说明                                                                   |
| --------------------------------- | -------------------- | ---------------------------------------------------------------------- |
| `apps/desktop-avalonia`           | **当前正式 Desktop** | Avalonia 桌面客户端；用户操作、配置、更新、诊断                        |
| `runtime/agents/host`             | **Host（入口进程）** | `Agents.exe`；挂载 Modules；Agents 才是容器                            |
| `runtime/agents/modules/injector` | **Injector 模块**    | AutoHotkey v2；解析、录入、验证、仓库任务；发布布局 `Modules/Injector` |
| `database/postgres`               | **数据库**           | SQL 与部署脚本；schema 演进与校验                                      |
| `packages/core`                   | **纯业务核心**       | 无数据库、文件、日志、配置、桌面端依赖                                 |
| `packages/application`            | **用例层**           | 页面/功能对应的应用服务与抽象                                          |
| `packages/infrastructure`         | **基础设施**         | Npgsql、仓储、DB 连接与迁移实现                                        |
| `packages/agents-contracts`       | **Agents 协议**      | 配置模型、命令/事件、运行时抽象                                        |

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

- 统一配置文件由桌面端 `AppConfigStore` 读写（Postgres + Agents/Injector 等）
- Host 通过 `--config` 启动参数读取同一份 JSON
- 所有发布版本以 `release-manifest.json` 为准，经 `scripts/export-version.sh` 同步到各子项目

## 相关文档

- [分层与依赖规则](./layering.md)
- [Avalonia 抽离计划](../migration/avalonia-extraction-plan.md)
- [发布流程](../operations/release-flow.md)
