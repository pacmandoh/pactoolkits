# Desktop 目录与 namespace

Desktop 使用 Avalonia MVVM，packages 侧保持 Clean Architecture。大目录按职责或业务域分子目录。禁止 `Common`、`Helpers`、`Utils`、`Misc`

页面状态与 DI 边界见 [layering.md](./layering.md)、[desktop-state.md](./desktop-state.md)。本文约定路径与 namespace

## 顶层（`apps/desktop-avalonia/src/`）

```text
Composition/          AddPacToolkitsUiServices
Navigation/           AppViews、ViewLocator
Diagnostics/          AppLog、LogTrace、NavPerf
Contracts/Presentation/
Controls/<Category>/
Behaviors/<Category>/
Converters/
Ui/<Area>/            Interaction、Formatting、State、Collections…
Services/
  Infrastructure/     Api、Configuration、Files、Logging、Platform、Connectivity…
  Integration/        Agents、Msfx、Update
  Presentation/       Connectivity、EmptyState、Tasks、Unlock、Update
  Workspace/          Inventory、Refresh
ViewModels/
  Pages/<Page>/
  Shell/
  Dialogs/
  Support/Catalog/
Views/
  Pages/<Page>/
  Shell/
  Dialogs/
Styles/  Assets/  Resources/
```

`App.axaml`、`Program.cs` 等入口可放在 `src/` 根下

## namespace

### 目录与 namespace 对齐

| 目录                             | namespace                        |
| -------------------------------- | -------------------------------- |
| `Composition/`                   | `.Composition`                   |
| `Navigation/`                    | `.Navigation`                    |
| `Diagnostics/`                   | `.Diagnostics`                   |
| `Contracts/Presentation/`        | `.Contracts.Presentation`        |
| `Ui/<Area>/`                     | `.Ui.<Area>`                     |
| `ViewModels/Support/Catalog/`    | `.ViewModels.Support.Catalog`    |
| `Services/Infrastructure/<Sub>/` | `.Services.Infrastructure.<Sub>` |
| `Services/Integration/<Sub>/`    | `.Services.Integration.<Sub>`    |
| `Services/Presentation/<Sub>/`   | `.Services.Presentation.<Sub>`   |
| `Services/Workspace/<Sub>/`      | `.Services.Workspace.<Sub>`      |

`Services/Integration/Agents/` 使用 `.Services.Integration.Agents`

剪贴板与 `UiBehavior` 在 `Infrastructure/Platform/`（namespace 同名）。禁止 `.Services.Infrastructure.System`：同层代码中的 `System.IO`、`System.Net` 会被解析到该命名空间

**Infrastructure** 是 Desktop 自己赖以运行的技术实现（配置、文件、日志、平台、HTTP 传输）。**Integration** 是与独立运行时或外部系统的适配（Agents Host、码上放心 HTTP、更新后端）

PacAPI 客户端在 `Services/Infrastructure/Api/`。同目录两类前缀：**`PacApi*`** 是连宿主的传输与协议（`PacApiClient`、选项、异常、Handler）；**`Api*`** 是用 `PacApiClient` 实现 Application 抽象的域适配（`ApiDashboard`、`ApiSync` 等）以及 Shell 可用性（`ApiAvailabilityService`）。共享 HTTP DTO 在 `packages/application/DTOs/Api/`，不放在 Desktop Api 目录。详见 [api.md](./api.md)

Agents Host 在 `Services/Integration/Agents/`（`AgentsRuntime`、`AgentsLink`、`AgentsHostLauncher`），面向 Host 协议与启停

### 物理子目录与稳定 namespace

| 物理目录                   | namespace           |
| -------------------------- | ------------------- |
| `Controls/<Category>/`     | `.Controls`         |
| `Behaviors/<Category>/`    | `.Behaviors`        |
| `Converters/`              | `.Converters`       |
| `Views/Pages/<Page>/`      | `.Views.Pages`      |
| `ViewModels/Pages/<Page>/` | `.ViewModels.Pages` |
| `Views/Shell/`             | `.Views`            |
| `ViewModels/Shell/`        | `.ViewModels`       |

`AppPageBase`、`IPageLifecycleAware`、`ViewModelBase` 在 `ViewModels/` 根。页面与 Shell 平行分目录，不使用 `Features/<X>/Views` 结构

## packages

Application 顶层为 `Abstractions`、DTOs、Services，以及 Diagnostics、Serialization、TextSearch、Threading。前三者按业务域分子目录；PacAPI 的 HTTP DTO 在 `DTOs/Api/`。Diagnostics、Serialization、Threading 在顶层

Infrastructure 的 Database、Repositories 按区域或业务域分子目录

public namespace 为 `.Application.*` 与 `.Infrastructure.*`（不随物理子目录变化）

API 宿主目录为 `Auth/`、`Changes/`、`Endpoints/`、`Hosting/`

## 测试

| 项目                                 | 约定                                                              |
| ------------------------------------ | ----------------------------------------------------------------- |
| `tests/PacToolkits.Desktop.Tests/`   | 按被测层分子目录；共用设施在 `TestSupport/`                       |
| `tests/PacToolkits.Api.Tests/`       | `Auth/`、`Changes/`、`Hosting/`、`TestSupport/`                   |
| `tests/PacToolkits.Desktop.UiTests/` | 按 Behaviors、Controls、Dialogs 等分组；共用设施在 `TestSupport/` |

测试文件放在被测类型对应目录下，不堆在测试项目根

## 新增文件

1. 放入已有业务或职责目录，不新建杂项目录
2. Desktop Service 放入上表子系统，并使用对应子 namespace
3. Controls、Behaviors、页面 MVVM 可有物理子目录；public namespace 与 `x:Class` 保持上表约定
4. `.axaml` 与 `.axaml.cs` 同目录、同名成对；同一 partial 组 namespace 一致
5. DI 仅通过 `Composition/ServiceRegistration.cs` 的 `AddPacToolkitsUiServices()` 注册
