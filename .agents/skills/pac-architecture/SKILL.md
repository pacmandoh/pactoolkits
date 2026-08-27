---
name: pac-architecture
description: >-
  PacToolkits 分层、依赖方向、DI、PageDataShell/PageDataAvailability、reload
  管线、代码该放哪。加功能、新页面、ViewModel、Service、Repo、跨层重构，或问
  Desktop / Application / Infrastructure / Core 时加载。动 apps/、packages/，或
  页面 reload / 空态 / Busy / Stale UI 之前先读。
---

# PacToolkits 架构

不确定时读 `docs/architecture/layering.md`、`docs/architecture/monorepo-layout.md`、`docs/architecture/desktop-layout.md`、`docs/architecture/agents.md`。

## 仓库地图

| 路径 | 职责 |
|------|------|
| `apps/desktop-avalonia/src/` | 生产 UI — Views、ViewModels、shell DI、仅 Desktop 的适配 |
| `apps/api-asp/` | PacAPI 进程（`PacToolkits.Api`）— API Key 换 JWT；领域路由走 Application 与 Infrastructure |
| `packages/core/` | 纯领域 — **零** 包引用 |
| `packages/application/` | 用例 — `Abstractions/<Domain>/`、`DTOs/<Domain>/`、`Services/<Domain>/`；另有 `Diagnostics/`、`Serialization/`、`Threading/`、`TextSearch/` |
| `packages/infrastructure/` | PostgreSQL — `Database/<Area>/`、`Repositories/<Domain>/` |
| `packages/agents-contracts/` | Desktop / Agents 协议 — **零** 依赖 |
| `runtime/agents/host/` | Agents Host（`Agents.exe`） |
| `runtime/agents/modules/` | Agents 模块（Injector 目前是 AHK） |
| `tests/` | xUnit；测试目录按被测层放置（见 desktop-layout） |
| `database/postgres/` | SQL 引导与迁移 |

## 依赖方向（禁止反向）

```
API depends on Application, Infrastructure   # csproj 引用路由真正需要的
Desktop depends on Application, Agents.Contracts, Logger
Host depends on Agents.Contracts
Infrastructure depends on Application, Core
Application depends on Core
```

**禁止：** Application 依赖 Infrastructure；Core 依赖 IO/UI/日志；ViewModel 依赖 Npgsql/SQL/`I*Repo`；Infrastructure 依赖 Avalonia。

**Desktop 与 Pg：** 领域数据 Desktop 只走 HTTP（见 `docs/architecture/api.md`、`layering.md`）。不要引用 Infrastructure、Npgsql，也不要注册 `AddPacToolkitsInfrastructure`。

## 新功能流程

1. 共享时在 `packages/application/Abstractions/<Domain>/` 与 `DTOs/<Domain>/` 放 **接口 + DTO**（PacAPI HTTP DTO 在 `DTOs/Api/`）
2. 用例在 `packages/application/Services/<Domain>/` — 编排 repo，不写 SQL
3. Repository 在 `packages/infrastructure/Repositories/<Domain>/` — 经 `IDb` / `PgDb` 写 SQL
4. 在 `AddPacToolkitsApplication()` / `AddPacToolkitsInfrastructure()` **注册**
5. **ViewModel 只注入 `I*Service`** — 不要 `*Repo` 或 `PgDb`
6. 仅 Desktop 的适配放 `Services/Infrastructure|Integration|Presentation|Workspace/<Sub>/`（PacAPI 在 `Infrastructure/Api/`；Agents Host 在 `Integration/Agents/`）
7. **页面**继承 `AppPageBase` 并走 reload 管线；文件在 `ViewModels/Pages/<Page>/` 与 `Views/Pages/<Page>/`

## DI 入口

唯一入口：`Composition/ServiceRegistration.cs` 的 `AddPacToolkitsUiServices()`。

不要在 ViewModel 里注册服务，也不要在各处 `new` 应用层/基础设施类型。

## 先复用再重写

| 事 | 用 |
|----|----|
| 数据访问 | API / Infrastructure：`IDb` / `PgDb`。Desktop 只走 PacAPI HTTP |
| Schema 门 | API：`IDbAccessGuard`（`PgDb`）。Desktop 页面只认 `ConnectionView` |
| 页面 reload | `RunReloadAsync` / `RunLocalReloadAsync` + `PageReloadBehavior` |
| 用户反馈 | `IToastService`、`IDialogService` |
| PacAPI / 页面错误 | 加载失败只记日志；连通性 toast 仅探测图标；`CanToastError` 过滤用户操作 |
| 传输错误 | `TransportErrors`（`Services/Infrastructure/Connectivity/`） |
| Shell 连通性横幅 | `ConnectivityBanner` + `ConnectionView`（`Services/Presentation/Connectivity/`） |
| 断连仍显示上次数据 | `PageReconnectPolicy` + `PageDataAvailability.Stale` / `AwaitingService` |
| 归一化 | `InputNormalizer`、`ClientDisplayResolver` |
| Schema 版本 | `SemVerRange.Classify` + `IDbSchemaGate` / `DbSchemaCompatibility` |
| 区块空态 | `EmptyStatePanel` + `SectionEmptyPolicy` + `SectionEmptyCopy` |
| 页面壳 | `PageDataShell`（`Controls/Feedback/`）+ `PageDataAvailability`（`Contracts/Presentation/`） |

## Desktop 文件夹与命名空间

规范见 **`docs/architecture/desktop-layout.md`**。

摘要：

- **不要**建 `Common/`、`Helpers/`、`Utils/`、`Misc/`
- Desktop Services：**文件夹与命名空间一致**（如 `.Services.Infrastructure.Api`）
- Controls / Behaviors / 页面 Views·ViewModels：物理子目录可以；命名空间保持稳定（`.Controls`、`.Behaviors`、`.Views.Pages`、`.ViewModels.Pages`；Shell 仍是 `.Views` / `.ViewModels`）
- Application / Infrastructure 包：物理按领域分目录；公开命名空间保持稳定（`.Application.*` / `.Infrastructure.*`）
- **不要**引入 `.Services.Infrastructure.System`（会和 BCL `System.*` 名称冲突）。剪贴板 / UiBehavior 用 `Infrastructure/Platform/`
- `AppPageBase` 放在 `ViewModels/` 根 — 不要放到 Api 或 Connectivity

## Desktop UI 状态（三层）

Busy 只表示正在拉数；断连与阻断由横幅、空态 Hint 说明，不与 Busy 叠同一条故障。

### 第 1 层 — Shell 连通性（全局）

- **负责：** `MainWindowViewModel` + `IApiAvailabilityService`
- **显示：** `ConnectivityBanner` + `ConnectionView`
- **UI：** 标题栏 / 状态条、顶栏 `ShellConnectivityBanner`、底栏 `ShellStatusBar` 服务阻断条
- **闸门：** 业务横幅、刷新按钮、工作区刷新以 `ConnectionView` 的 Up 为准；协议 / schema 不合则阻断（Lookup 清空、AccessBlocked）；PostgreSQL 不可达是不通，与 API 进程挂掉同一条路
- **Toast：** 连通性错误仅探测图标点击；页面加载/自动刷新只记日志
- 页面 **不要** 在区块 toast 或空态里另写断连文案

### 第 2 层 — 页面可用性（`AppPageBase`）

- **枚举：** `PageDataAvailability` — `NotLoaded`、`AwaitingService`、`AccessBlocked`、`Loading`、`LoadFailed`、`Stale`、`Ready`
- **壳：** 页面根用 `PageDataShell` — 不可用空态 + 内容区交互锁（`IsInteractionEnabled` 由 `CanPage` 决定）。断连文案在 Shell `ConnectivityBanner`。拉数 Busy 在页面 `BusyArea`。顶栏刷新更新页面主内容
- **规则：**
  - `IsBusy` = 只表示正在拉数，等 PacAPI 时不要 Busy
  - `ShowPageUnavailable` = 首次加载门、`AccessBlocked`、或 `LoadFailed` — **不是** `Stale`
  - `IsShowingStaleData` / `Stale` = 首次成功后断连；旧内容继续显示
  - PacAPI 非 Up：业务页不拉数、不 Busy；恢复后由 Shell 工作区静默刷新
  - 手动刷新：连接 Up 时照常显示 busy
  - 非传输的拉数错误变成 `LoadFailed` 空态；管线不得抛未处理异常
  - PacAPI 传输失败：探测 Down 时等 Shell；探测仍 Up 时可自行退避重试（不要标成 Ready）
  - `SupportsStaleWhileReconnect` — 非只读页（如 Settings）重写为 `false`
  - 策略在 `PageReconnectPolicy` — 不要每页复制断连/Stale 逻辑
  - 页面可用性同步：`AppPageBase.SyncPageAvailability()`；页面用 `ConnectionView` 判 Up / Down / Blocked
  - 断连/阻断的 UI 锁只有 `PageDataShell` 遮罩（`IsInteractionEnabled` 由 `CanPage` 决定）；不要为断连逐控件 `IsEnabled`

### 第 3 层 — 区块空态（列表/图）

- **控件：** 只用 `EmptyStatePanel` — 不要并行空态系统
- **门：** `ShowSectionEmpty(isContentEmpty)` — 空时一律优先 `EmptyStatePanel` 而不是空表格；文案经 `SectionEmptyCopy.GetTitle/GetHint`
- **`AccessBlocked`：** shell 横幅说明连通性/版本；`PageDataShell` 经 `ShowPageUnavailable` 显示不可用空态 — 区块列表仍由 `ShowSectionEmpty` 把门
- **叠放：** `BusyArea`（区块），然后 `EmptyStatePanel`，然后 DataGrid/内容

### Reload 管线

| 组件 | 职责 |
|------|------|
| `PageReloadBehavior` | 并发 reload 只留最后一次 |
| `PageReloadBusyDelay` | 拉数时 300ms 延迟 busy（Stale 静默刷新跳过） |
| `AppPageBase.ExecuteReloadPipelineAsync` | 先 preflight，再拉数，再可用性 |

### 加数据页

1. ViewModel 继承 `AppPageBase`；只在需要时重写 `SupportsStaleWhileReconnect`。文件在 `ViewModels/Pages/<Page>/`
2. 根视图用 `PageDataShell` 包内容。文件在 `Views/Pages/<Page>/`
3. 区块空态用 `ShowSectionEmpty`；为 `Is*Empty` 绑定重写 `OnPageAvailabilityChanged`
4. 用 `CanToastError(ex)` — 页面层不要 toast 传输 / 重连窗口错误
5. 顶栏和区块拉数 Busy 用 `RunLocalReloadAsync` / `RunLocalBusyAsync` — 不要用不经 reload 管线的 `AsyncRelayCommand` 调用 PacAPI
6. 以 `ConnectionView` 为门；不要本地 Pg 监视 / AccessGuard

## 测试

- xUnit 在 `tests/`：`PacToolkits.Desktop.Tests`、`PacToolkits.Api.Tests`、`PacToolkits.Agents.Contracts.Tests`、`PacToolkits.Desktop.UiTests`。日常按改动层选项目；push 仍走 **pac-git** 的解决方案闸门
- `Desktop.Tests` 按层分（`Application/`、`Infrastructure/Api/`、`ViewModels/` …）；共享辅助在 `TestSupport/`
- Postgres 集成挂在 `Category!=PostgresIntegration` 后面
- 纯 Core/Application 逻辑：尽量不依赖 Avalonia
- agent 流程里不要 `dotnet restore` — 见 **pac-dotnet-build**

## 反模式（不要引入）

- 在 `PageDataShell` / shell 横幅旁边另行实现空态/busy/重连/Stale UI
- 每页自己的断连 toast
- Application 或 ViewModel 里写 SQL 或 Npgsql
- 不经 reload 管线的 `AsyncRelayCommand` 调用 PacAPI
- 复制 `Ui/`、Desktop Services、Application Services 里已有的辅助
- 把本该放在 Application 的接口，和新的 `*Service` 一并放在 desktop 实现旁
- 每页自己写 Stale/断连可用性，而不用 `PageReconnectPolicy`
- 建 `Common/`，或把新类型放到项目根
- 整库 Vertical Slice（`Features/X/{Views,ViewModels}`），而不是平行 MVVM 目录
