# Avalonia 抽离与 Monorepo 迁移计划

本文记录从单体 `pactoolkits-ui` 到当前 monorepo 的分阶段迁移状态与后续原则。详细步骤见仓库外《PacToolkits Monorepo 重构规则》。

## 目标

- UI 保持 Avalonia 为**唯一正式客户端**
- 业务逻辑沉入 `packages/`，便于测试与未来 Electron preview 复用
- AHK Agent 与 PostgreSQL migration **保留**，不重写
- `release-manifest.json` 语义不变

## 阶段完成情况

| 阶段 | 内容 | 状态 |
|------|------|------|
| 0–2 | 目录迁移、命名空间统一、`PacToolkits.sln` | ✅ |
| 3 | DTO / 契约迁入 `packages/application`、`agent-contracts` | ✅ |
| 4 | 仓储接口迁入 Application Abstractions | ✅ |
| 5 | Npgsql 与仓储实现迁入 Infrastructure | ✅ |
| 6 | 页面 ViewModel 改用 Application 服务 | ✅ |
| 7 | Agent 边界：`agent-contracts` + 运行时抽象 | ✅ |
| 8 | `apps/desktop-electron` 空壳占位 | ✅ |
| 9 | 架构文档与 README 更新 | ✅（本文档体系） |

## 已从 Avalonia 移出的内容

- `DataAccess/` 目录（仓储 → Infrastructure）
- ViewModel 内联 SQL / 直接 `Npgsql` 引用
- 页面级业务解析（→ Application Services）
- Agent 配置校验逻辑（→ `AgentConfigValidator`）

## 仍留在桌面端的内容（合理保留）

- Avalonia Views / ViewModels / Styles / Behaviors
- UI 壳层服务：Toast、Dialog、Update、Clipboard、UiBehavior
- `AhkRuntimeService`：进程启停（实现 `IAutomationRuntimeService`）
- `AppConfigStore`：统一 JSON 配置文件读写
- MSFX HTTP 客户端等与桌面集成强相关的适配层

## 禁止事项（迁移期间持续有效）

1. 搬目录同时重写业务逻辑
2. 用 Electron 替代 Avalonia 作为正式 UI
3. 删除 AHK Agent 或 PostgreSQL migration
4. 修改 `release-manifest.json` 字段语义
5. Core 引用 Avalonia / Npgsql
6. Application 引用 Infrastructure
7. 在 ViewModel 中新增 SQL 或 Npgsql

## 未来：Electron Preview

`apps/desktop-electron` 为 Nuxt + Electron 预留，当前：

- 不参与 CI / release
- 不连接数据库
- 不实现业务页面

若启动实现，应：

1. 通过 HTTP/IPC 调用已抽出的 Application 能力，或
2. 嵌入 .NET 宿主加载 Application + Infrastructure（具体方案待定）

**不得**在 Electron 层复制 Postgres 访问逻辑。

## 验收清单（重构完成标准）

```bash
dotnet build PacToolkits.sln
./scripts/check-version.sh
./scripts/export-version.sh
```

功能验收：

1. Avalonia 应用能启动
2. Dashboard / Inventory / ScanCode 页面能加载
3. Agent 仍可由 CI 打包为 `pacinjector.exe`
4. GitHub Actions 路径指向新 monorepo 布局
5. ViewModel 不直接依赖 Npgsql
6. Application / Core 不依赖 Avalonia
