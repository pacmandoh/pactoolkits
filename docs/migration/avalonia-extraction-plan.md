# Avalonia 抽离与 Monorepo 迁移计划

本文记录从单体 `pactoolkits-ui` 到当前 monorepo 的分阶段迁移状态与后续原则。

## 目标

- Desktop 组件 ID 固定为 **`desktop`**；当前正式实现为 **Avalonia**（`components.desktop.implementation = avalonia`）
- 业务逻辑沉入 `packages/`，便于测试与未来 Electron 复用
- AHK Agent（`agent-injector-ahk`）与 PostgreSQL migration **保留**，不重写
- 版本由 **`release-manifest.json` schema v2** 统一驱动

## 阶段完成情况

| 阶段           | 内容                                                     | 状态                                                               |
| -------------- | -------------------------------------------------------- | ------------------------------------------------------------------ |
| 0–2            | 目录迁移、命名空间统一、`PacToolkits.sln`                | ✅                                                                 |
| 3              | DTO / 契约迁入 `packages/application`、`agent-contracts` | ✅                                                                 |
| 4              | 仓储接口迁入 Application Abstractions                    | ✅                                                                 |
| 5              | Npgsql 与仓储实现迁入 Infrastructure                     | ✅                                                                 |
| 6              | 页面 ViewModel 改用 Application 服务                     | ✅                                                                 |
| 7              | Agent 边界：`IAgentManager` + `AhkInjectorAgentRuntime`  | ✅                                                                 |
| 8              | `apps/desktop-electron` 空壳占位                         | ✅                                                                 |
| 9              | 架构文档与 README 更新                                   | ✅                                                                 |
| **Foundation** | Agent 命名、Manifest V2、CI 拆分、`package-desktop`      | ✅                                                                 |
| **7–10**       | Electron 对齐、切换 implementation、删除 Avalonia        | ⏳ 见 [desktop-electron-cutover.md](./desktop-electron-cutover.md) |

## 已从 Avalonia 移出的内容

- `DataAccess/` 目录（仓储 → Infrastructure）
- ViewModel 内联 SQL / 直接 `Npgsql` 引用
- 页面级业务解析（→ Application Services）
- Agent 配置校验逻辑（→ `AgentConfigValidator`）

## 仍留在桌面端的内容（合理保留）

- Avalonia Views / ViewModels / Styles / Behaviors
- 桌面壳层服务：Toast、Dialog、Update、Clipboard、UiBehavior
- `AhkInjectorAgentRuntime` + `AgentManager`：Agent 进程启停（ViewModel 经 `IAgentManager` 访问）
- `AppConfigStore`：统一 JSON 配置（含 `agents.agent-injector-ahk`）
- MSFX HTTP 客户端等与桌面集成强相关的适配层

## 禁止事项（迁移期间持续有效）

1. 搬目录同时重写业务逻辑
2. 用 Electron 替代 Avalonia 作为**正式** Desktop（Preview 除外）
3. 删除 AHK Agent 或 PostgreSQL migration
4. 破坏 Manifest V2 字段语义或 `packId=pactoolkits`
5. Core 引用 Avalonia / Npgsql
6. Application 引用 Infrastructure
7. 在 ViewModel 中新增 SQL 或 Npgsql

## Electron Preview（步骤 7+）

`apps/desktop-electron` 为 Nuxt + Electron 预留，当前：

- 不参与正式 Feed / release
- 不连接数据库

详见 [desktop-electron-cutover.md](./desktop-electron-cutover.md)。

## 验收清单

```bash
./scripts/export-version.sh
./scripts/check-version.sh
dotnet build PacToolkits.sln -c Release --no-restore
```

功能验收：

1. Avalonia 应用能启动
2. Dashboard / Inventory / ScanCode 页面能加载
3. Agent CI 输出 `pactoolkits-injector.exe` 至 `artifacts/agents/...`
4. `package-desktop` 将 Agent 写入 `Agents/injector/`
5. ViewModel 不直接依赖 Npgsql
6. Application / Core 不依赖 Avalonia
