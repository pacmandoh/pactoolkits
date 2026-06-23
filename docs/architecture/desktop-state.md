# 桌面状态模型

Avalonia Desktop（`apps/desktop-avalonia`）将**全局连接**、**页面数据可用性**、**区块空态**拆成三层，避免单一 `IsBusy` 或重复 toast/banner 表达同一事件。

相关实现：`apps/desktop-avalonia/src/ViewModels/AppPageBase.cs`、`Controls/PageDataShell.axaml`、`Services/Application/ConnectivityBannerFactory.cs`。

## 三层职责

```mermaid
flowchart TB
  subgraph L1["Layer 1 — Shell 连接"]
    MW["MainWindowViewModel"]
    Banner["ConnectivityBanner"]
    Status["ShellStatusBar"]
    MW --> Banner
    MW --> Status
  end

  subgraph L2["Layer 2 — 页面可用性"]
    Base["AppPageBase"]
    Shell["PageDataShell"]
    Base --> Shell
  end

  subgraph L3["Layer 3 — 区块空态"]
    Empty["EmptyStatePanel"]
    Policy["SectionEmptyPolicy"]
    Policy --> Empty
  end

  L1 -.->|不重复 toast| L2
  L2 -.->|ShowPageUnavailable / Stale| L3
```

| 层         | 所有者                                                                 | 呈现                                                               | 典型状态                                                                                    |
| ---------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------- |
| Shell 连接 | `MainWindowViewModel`、`IDbConnectionMonitorService`、`IDbAccessGuard` | 顶栏 DB 图标、侧栏 DB 卡片、`ConnectivityBanner`、`ShellStatusBar` | 探测中（无 banner）、已知断开、AccessGuard 阻断                                             |
| 页面可用性 | `AppPageBase`、`PageDataAvailability`                                  | `PageDataShell`（不可用空态、stale 条、加载 busy）                 | `NotLoaded`、`AwaitingDatabase`、`AccessBlocked`、`Loading`、`LoadFailed`、`Stale`、`Ready` |
| 区块空态   | 各页 ViewModel + `SectionEmptyCopy`                                    | `EmptyStatePanel`                                                  | 列表/图表无数据时的标题与提示                                                               |

## Layer 1 — Shell 连接

- 连接/断开/恢复 toast **仅**在 `MainWindowViewModel` 发出。
- `ConnectivityBannerFactory`：**不**在 DB 探测中显示 info banner；仅在 AccessGuard 阻断或**已知断开**时显示 warning。
- 各数据页**不得**再 toast 传输层断连或 guard 阻断类错误（见 `CanToastError`）。

## Layer 2 — 页面可用性

### `PageDataAvailability`

| 值                               | `ShowPageUnavailable` | `IsBusy`         | 用户可见                          |
| -------------------------------- | --------------------- | ---------------- | --------------------------------- |
| `NotLoaded` / `AwaitingDatabase` | 是                    | 否               | 等待数据库或首次加载门闸          |
| `AccessBlocked`                  | 是                    | 否               | 版本/迁移阻断；配合 shell banner  |
| `LoadFailed`                     | 是                    | 否               | 非传输类加载失败空态              |
| `Loading`                        | 否                    | 是（延迟 300ms） | 正在拉取数据                      |
| `Stale`                          | 否                    | 否（静默刷新）   | 断连后保留上次成功内容 + stale 条 |
| `Ready`                          | 否                    | 否               | 正常展示                          |

规则摘要：

- `IsBusy` 只表示**正在取数**，等待数据库连接时不用 busy 遮罩。
- `ShowPageUnavailable` 包含首次加载门闸、`AccessBlocked`、`LoadFailed`，**不包含** `Stale`。
- 断连后若页面曾加载成功 → `Stale`；DB 信号恢复时可静默后台刷新（无 busy）。
- 手动刷新仍走常规 busy。
- 只读页默认 `SupportsStaleWhileReconnect = true`；设置等非只读页可覆写为 `false`。
- 策略集中在 `PageStaleWhileReconnectPolicy`，各页不要复制断连/stale 分支。

### Lookup 与 AccessGuard

`LookupCatalogService` 在 AccessGuard 阻断时清空缓存并返回空；各页通过 `OnLookupCatalogSuspended()` 清空 `DrugOptions` 等自动完成源，避免版本不匹配时仍推荐库内药品。

## Layer 3 — 区块空态

- 唯一空态控件：`EmptyStatePanel`（不要并行第二套 empty 系统）。
- `ShowSectionEmpty(isContentEmpty)` 决定是否显示区块空态；文案经 `SectionEmptyCopy`。
- `AccessBlocked` 时：shell banner 说明全局原因，`PageDataShell` 显示不可用空态；列表区仍由 `ShowSectionEmpty` 门闸。
- 视觉栈：`BusyArea（区块）→ EmptyStatePanel → DataGrid/内容`。

## 重载流水线

| 组件                                     | 职责                                       |
| ---------------------------------------- | ------------------------------------------ |
| `PageReloadBehavior`                     | 单飞取消门闸                               |
| `PageReloadBusyDelay`                    | 取数 busy 延迟 300ms（stale 静默刷新跳过） |
| `AppPageBase.ExecuteReloadPipelineAsync` | 预检 → 取数 → 更新 `PageDataAvailability`  |

新数据页接入步骤见 `.cursor/rules/architecture.mdc` 中 “Adding a data page”。

## 反模式

- 与 `PageDataShell` / shell banner 并行的第二套 empty、busy、重连 UI。
- 页面级 DB 断连 toast。
- 未走 `RunReloadAsync` / guard 预检的裸 `AsyncRelayCommand` 直连接口库。
