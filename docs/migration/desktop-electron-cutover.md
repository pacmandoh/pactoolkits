# Desktop 命名、Manifest V2 与 Electron 切换路线图

本文档记录 **foundation 迁移（步骤 1–6）** 之后尚未执行的 **步骤 7–10**，以及与 Avalonia 抽离计划的关系。

## 已完成（步骤 1–6）

| 步骤 | 内容                                                            | 状态 |
| ---- | --------------------------------------------------------------- | ---- |
| 1    | Agent ID、目录、图标、二进制与 Artifact 统一命名                | ✅   |
| 2    | `pacinjector.exe` 旧路径兼容与配置迁移                          | ✅   |
| 3    | Agent 构建与 Desktop 实现解耦（独立 workflow）                  | ✅   |
| 4    | `package-desktop` 聚合 Agent + Desktop                          | ✅   |
| 5    | Manifest V2 + `bump/export/check-version.sh`                    | ✅   |
| 6    | 多 Agent 管理模型（`IAgentManager`、`AhkInjectorAgentRuntime`） | ✅   |

组件 ID 与路径约定见 [发布流程](../operations/release-flow.md)。

## 待执行（步骤 7–10）

### 步骤 7 — Electron Preview 功能对齐

**目标：** `apps/desktop-electron` 可演示核心流程，**不进入正式 Feed**（无独立 preview CI / artifact）。

| 项       | 说明                                                                          |
| -------- | ----------------------------------------------------------------------------- |
| Manifest | `components.desktop.implementation` 仍为 `avalonia`；Electron 仅本地/实验构建 |
| 范围     | 壳层 + 导航 + 与 .NET 宿主或 API 的集成方案（待定）                           |
| 禁止     | 复制 Postgres 访问；不得发布到 `/feed/pactoolkits/{channel}/`                 |

**验收：**

- 产物命名符合 kebab-case 规范
- 正式 `release.yml` 不依赖 Electron preview job

### 步骤 8 — Avalonia → Electron 升级与配置迁移验证

**目标：** 已安装 Avalonia 包的用户，在切换实现后保留配置与数据。

| 项    | 说明                                                                                                                   |
| ----- | ---------------------------------------------------------------------------------------------------------------------- |
| 不变  | `packId=pactoolkits`、Feed 路径、用户配置目录 `%AppData%\PacToolkits`                                                  |
| 迁移  | `agents.*`、`AutomationTools.*` 继续由 `AppConfigStore` 读写；Electron 读取同一 JSON                                   |
| Agent | 仍从 `Agents/injector/` 启动；配置若为 legacy `Tools/pacinjector.exe` 或旧标准路径，且 bundled 新 exe 存在，则自动迁移 |
| DB    | `database-postgres` 版本规则不变                                                                                       |

**验收：**

- 模拟升级：Avalonia 安装包 → Electron 安装包，配置与 DB 连接可用
- Agent 路径迁移日志可追溯

### 步骤 9 — 切换 `desktop.implementation`

**前置：** 步骤 7–8 完成且产品确认。

```json
"components": {
  "desktop": {
    "implementation": "electron",
    "packageId": "pactoolkits",
    ...
  }
}
```

| 项        | 说明                                                                                        |
| --------- | ------------------------------------------------------------------------------------------- |
| CI        | `package-desktop` 默认聚合 Electron 构建产物                                                |
| 正式 Feed | 仅发布 `pactoolkits-desktop-win-x64-<product.version>`（implementation 在 manifest 中声明） |
| Avalonia  | 仍可保留 `build-desktop-avalonia.yml` 若干版本用于回滚，但不进 stable Feed                  |

### 步骤 10 — 删除 Avalonia 项目及专属 CI

**前置：** Electron stable 运行至少一个完整版本周期。

- 删除 `apps/desktop-avalonia/`
- 删除 `build-desktop-avalonia.yml`
- 更新 `PacToolkits.sln`、README、Rider 启动配置
- 保留 `runtime/agents/`、`database/postgres/`、`packages/*`

## Agent 接口分层（当前）

```text
SettingsViewModel / MainWindowViewModel
  → IAgentManager.GetRequired(AgentIds.InjectorAhk)
    → IAgentRuntime (AhkInjectorAgentRuntime 实现)
```

- **`IAgentRuntime`**：多 Agent 通用契约（`agent-contracts`）
- **`IAgentManager`**：Desktop 侧 Agent 注册表与运行时访问入口
- 已删除 **`IAgentRuntimeService`**、**`IAutomationRuntimeService`** 及 **`AutomationRuntimeServiceAdapter`**

未来新增 Agent 时：注册到 `AgentManager`，页面通过 `IAgentManager` 或新的应用层 facade 访问。

## 相关文档

- [Avalonia 抽离计划](./avalonia-extraction-plan.md)
- [Monorepo 布局](../architecture/monorepo-layout.md)
- [发布流程](../operations/release-flow.md)
