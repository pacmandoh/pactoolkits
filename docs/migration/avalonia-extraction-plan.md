# Avalonia 抽离与 Monorepo 迁移计划

本文记录从单体 `pactoolkits-ui` 到当前 monorepo 的分阶段迁移状态与后续原则。

## 目标

- Desktop 组件为 **`desktop.<impl>`**；当前正式实现为 **`desktop.avalonia`**
- 业务逻辑沉入 `packages/`，便于测试与复用
- Agents（`agents` 容器 + `modules/injector` AHK 模块，发布 `Modules/Injector`）与 PostgreSQL migration **保留**，不重写
- 版本由 **`release-manifest.json` schema v2** 统一驱动

## 阶段完成情况

| 阶段           | 内容                                                      | 状态 |
| -------------- | --------------------------------------------------------- | ---- |
| 0–2            | 目录迁移、命名空间统一、`PacToolkits.sln`                 | ✅   |
| 3              | DTO / 契约迁入 `packages/application`、`agents-contracts` | ✅   |
| 4              | 仓储接口迁入 Application Abstractions                     | ✅   |
| 5              | Npgsql 与仓储实现迁入 Infrastructure                      | ✅   |
| 6              | 页面 ViewModel 改用 Application 服务                      | ✅   |
| 7              | Agents 边界：`IAgentsManager` + `AgentsRuntime`           | ✅   |
| 9              | 架构文档与 README 更新                                    | ✅   |
| **Foundation** | Agents 命名、Manifest V2、CI 拆分、`package-desktop`      | ✅   |

## 已从 Avalonia 移出的内容

- `DataAccess/` 目录（仓储 → Infrastructure）
- ViewModel 内联 SQL / 直接 `Npgsql` 引用
- 页面级业务解析（→ Application Services）
- Agents 配置校验逻辑（→ `AgentsConfigValidator`）

## 仍留在桌面端的内容（合理保留）

- Avalonia Views / ViewModels / Styles / Behaviors
- 桌面壳层服务：Toast、Dialog、Update、Clipboard、UiBehavior
- `AgentsRuntime` + `AgentsManager`：Host 进程启停（ViewModel 经 `IAgentsManager` 访问）
- `AppConfigStore`：统一 JSON 配置（Schema v2 `Agents` host + `Injector`；读入时仅迁移 Main `AutomationTools`）
- MSFX HTTP 客户端等与桌面集成强相关的适配层

## 禁止事项（迁移期间持续有效）

1. 搬目录同时重写业务逻辑
2. 删除 Agents 容器 / Injector 模块或 PostgreSQL migration
3. 破坏 Manifest V2 字段语义或 `packId=PacToolkits`
4. Core 引用 Avalonia / Npgsql
5. Application 引用 Infrastructure
6. 在 ViewModel 中新增 SQL 或 Npgsql

## 验收清单

```bash
./scripts/export-version.sh
./scripts/check-version.sh
dotnet build PacToolkits.sln -c Release --no-restore
```

功能验收：

1. Avalonia 应用能启动
2. Dashboard / Inventory / ScanCode 页面能加载
3. Agents CI 输出 `Agents.exe`（及 `Modules/Injector/Injector.exe`）至 `artifacts/agents/...`
4. `package-desktop` 将 Agents 写入 `Agents/`（容器）与 `Agents/Modules/Injector/`
5. ViewModel 不直接依赖 Npgsql
6. Application / Core 不依赖 Avalonia
