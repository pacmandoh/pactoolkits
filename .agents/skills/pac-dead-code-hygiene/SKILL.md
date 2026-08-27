---
name: pac-dead-code-hygiene
description: >-
  PacToolkits 死代码与重复审计：无引用、重复逻辑、架构漂移、孤儿 ViewModel 属性、
  死 DI、安全删除顺序。提到 cleanup、dead code、unused、orphan、死代码、清理、
  瘦身，或大功能后问删什么时加载。分层扫描读 references/；Phase A 跑 scripts/。
---

# 死代码与重复清理

## 配套 skill（加载顺序）

| 顺序 | Skill | 用于 |
|------|-------|------|
| 1 | **pac-dead-code-hygiene**（本篇） | 扫描、分类、删除 |
| 2 | **pac-architecture** | 漂移 — 重构，不要未经确认就删 |
| 3 | **pac-desktop-ui** | 删 AXAML class/converter |
| 4 | **pac-naming** | 改名 vs 删除的边界；碰到的符号做 debit + peer |
| 5 | **pac-dotnet-build** | 删完编测 |
| 6 | **pac-git** | 只含清理的提交/PR |

完整流程：[references/audit-workflow.md](references/audit-workflow.md)

## 参考索引（按需读）

| 何时 | 读 |
|------|----|
| Phase A Desktop UI/VM | [references/desktop-scan-patterns.md](references/desktop-scan-patterns.md) |
| DI、页面、对话框、`AppViews` | [references/di-reflection-wiring.md](references/di-reflection-wiring.md) |
| Application / Infrastructure / Core | [references/package-layer-patterns.md](references/package-layer-patterns.md) |
| 任何删除之前 | [references/false-positive-guards.md](references/false-positive-guards.md) |
| 阶段、严重度、PR 范围 | [references/audit-workflow.md](references/audit-workflow.md) |

## Phase A 脚本

从仓库根跑（只 WARN — Phase B 再确认）：

```bash
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-dead-di.sh
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-orphan-vm-props.sh
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-unused-styles.sh
```

## 分层对照

| 泛称 | PacToolkits |
|------|-------------|
| Page / Route | `Views/Pages/<Page>/*.axaml`、`AppPageBase` + 侧栏注册 |
| ViewModel | `ViewModels/Pages/<Page>/` |
| Service | `packages/application/Services/<Domain>/`；Desktop `Services/{Infrastructure,Integration,Presentation,Workspace}/<Sub>/` |
| Repository | `packages/infrastructure/Repositories/<Domain>/` |
| Store / Hook | ViewModel 状态 + `AppPageBase` 管线；不要并行状态机 |
| Composable | 可复用控件在 `Controls/<Category>/`，样式在 `Styles/` |
| DTO | `packages/application/DTOs/<Domain>/`（PacAPI DTO 在 `DTOs/Api/`） |
| Helper | `Ui/<Area>/` 或对应 Desktop Services 子系统；不要建 `Common/` |

---

## 1. 无引用产物

全库扫零引用（`rg`、IDE Find References、DI 注册 grep）。

| 产物 | 往哪找 | 何时删 |
|------|--------|--------|
| `.cs` 文件 | 无类型/成员引用；不在 DI；不在 `AddPageViewModels` | 确认孤儿 |
| `.axaml` 视图 | 无 `{x:Type ...}` 导航；无 DataTemplate；无 code-behind 引用 | 孤儿页 |
| ViewModel | 未在 `ServiceRegistration` 注册；不在 pages 枚举 | 死页面 VM |
| `I*Service` / `*Service` | 接口未用；实现只自引用 | 合并或删 |
| `I*Repo` / `*Repo` | 无 Application Service 调用方 | 删方法或整个 repo |
| 控件（`Controls/`） | 任何 `.axaml` 都没用 | 删控件和样式 |
| 样式（`Styles/` 里 `.axaml`） | 选择器从未匹配；视图从未设 class | 删样式 |
| 资源（`Assets/`） | 无 `avares://` 或 `Source=` | 删资源 |
| DTO / 枚举 | 无 mapper、service、测试使用 | 删或合并 |
| SQL 迁移/脚本 | 不在引导链 / 文档 / CI | 先问用户 |
| 测试文件 | 只测已删类型 | 一起删 |

**DI 注册不等于在用。** 见 [references/di-reflection-wiring.md](references/di-reflection-wiring.md)。

```bash
rg "class YourType" -g "*.cs"
rg "YourType" apps/desktop-avalonia/src/Composition/ServiceRegistration.cs
rg "YourType" apps/desktop-avalonia/src/Views -g "*.axaml"
```

---

## 2. 遗留、重复、兼容代码

| 信号 | PacToolkits 例子 | 动作 |
|------|------------------|------|
| `[Obsolete]` | 无调用方 | 删类型和调用方 |
| `*Old*`、`*Legacy*`、`*V1*` | 被取代的 MSFX 路径或 VM | 并进规范路径或删 |
| 重复实现 | 两套分类器、两套空态、两套 reload 闸门 | 留 `AppPageBase` / `PageDataShell` / `ConnectivityBannerFactory` |
| 未用 shim | `#if`、`// TODO remove after`、未用兼容包装 | 删 |
| 死入口 | 未用菜单项、死 `RelayCommand`、未用侧栏页 | 删 VM + 视图 + 注册 |
| 死配置键 | `appsettings` / `dbconfig.json` 字段从未读 | 从模型和文档删 |
| 绕过 PacAPI 直打 DB | VM 直接调 `PgDb` 或 repo | 挪到 Application Service；删旁路 |

---

## 3. 不可达与无意义代码

| 信号 | 动作 |
|------|------|
| 闸门重构后分支恒真/恒假 | 简化或删分支 |
| `if (false)`、`#if false` 块 | 删 |
| 重复 `if/else` 链 | 抽共享辅助或 switch |
| 空 `catch { }` / `catch (Exception) { }` 且不打日志 | 经 `AppLog` 记，或不必要时去掉 try |
| 空方法体 | 删 override 或补实现 |
| 纯转发包装 | 内联，除非 DI/测试需要 |
| `throw new NotImplementedException()` | 实现，或删调用方 |
| 死 `switch` 分支 / 枚举成员 | 删成员和所有 switch |

---

## 4. 未用成员（活类型内部）

确认类型本身仍在用之后，按文件看：

- 从未引用的私有方法/字段
- 程序集外从未调用的公开 API（若不是 DI 要注册的，考虑裁）
- AXAML 未绑定的 ViewModel 属性 — 见 [references/desktop-scan-patterns.md](references/desktop-scan-patterns.md)
- 绑到不存在属性的 AXAML（编译/analyzer warning）
- 未用 `using`、只有一份的多余 `partial`
- 接口成员只实现、从未经接口调用

删 VM 属性或命令前 **一律**过 [references/false-positive-guards.md](references/false-positive-guards.md)。

---

## 5. 依赖、配置、脚本

| 项 | 查 |
|----|----|
| NuGet 包 | 试删后无 `using`/类型引用；看 `.csproj` |
| `apps/desktop-avalonia/src/*.csproj` | 未用 PackageReference |
| 环境变量 | grep 键；死了从 launchSettings / 文档删 |
| `scripts/*.ps1`、`*.sh` | CI、README、或 package.json scripts 有没有引用 |
| `database/postgres/*.sql` | 引导 README 或迁移 runner 有没有引用 |
| 过时的 `.agents/skills/pac-*` 或文档 | 删重复源 — 每个主题一份规范 skill |

不要在没核对 workflow 文件的情况下删 CI 引用的包或脚本。

---

## 6. 重复逻辑（影响最大 — P2）

优先合并 — 细节见 [references/package-layer-patterns.md](references/package-layer-patterns.md)：

| 领域 | 规范位置 |
|------|----------|
| 业务规则 | `packages/application/Services/` |
| SQL / 持久化 | `packages/infrastructure/Repositories/` |
| PostgreSQL 传输 / schema 门 | `DbTransportErrorClassifier`、`IDbAccessGuard` |
| 页面 reload / 可用性 | `AppPageBase`、`PageReconnectPolicy` |
| Shell 连通性文案 | `MainWindowViewModel`、`ConnectivityBannerFactory` |
| 输入归一化 | `InputNormalizer`、`ClientDisplayResolver` |
| Converter / 值辅助 | `Converters/`、`Ui/`、或共享 Controls — 不要复制进每个 View |
| AXAML 重复块 | 共享 `Styles/` 或 `Controls/` |

**重复迹象：** 两份 repo 同一段 SQL；三个 VM 同一套空面板条件；两个服务同一套连接串解析。

---

## 7. 注释与过时标记

| 信号 | 动作 |
|------|------|
| 注释掉的代码 >5 行 | 删（git 历史还在） |
| `TODO` / `FIXME` / `HACK` | 核对是否仍有效；修或删注 |
| 注释和代码矛盾 | 改代码或改注释 |
| 只叙述已完成搬迁或已取代路径的注释/文档 | 删 |

```bash
rg "TODO|FIXME|HACK" apps packages tests --glob "*.cs"
```

---

## 8. 设计 token 漂移（Desktop P1/P2）

配 **pac-desktop-ui**，改 AXAML 资源前读它的 `references/tokens.md`。

| 信号 | 严重度 | 动作 |
|------|--------|------|
| 资源声明没有任何 AXAML/C# 引用 | P1 | 过全库/动态资源门后再删 |
| 零值 token 只有一处引用，且选择器/属性已经表明用途 | P1 | 把零内联；只为必需覆盖或框架约束才留 |
| 组件 token 等于规范圆角/图标/字号刻度 | P2 | 引用改成规范键；删语义别名 |
| 几个兄弟键相等是因为控件必须同步 | P2 | 换成一条共享语义规范 |
| `Sm/Md/Lg` 在不同轴或组件族代表不同值 | P2 | 拿掉误导梯子；用一条规范刻度或语义关系 |
| `FontSize`/字体 token 放在 `Layout.axaml` | P2 | 有理由的字号 token 挪到 `Theme/Typography.axaml`；先考虑 Shad class |
| 重复写死字号，复制了 Shad 排版角色 | P2 | 套 Shad class，去掉本地字号 |

每个候选：在全库搜精确键，含 `Styles/`、code-behind、测试、动态 `Classes.Add`/资源查找。同一资源域里名字 **和值** 都比；值重复不一定错，但没有独立可调价值的别名是错。

安全合并顺序：

1. 找出规范刻度/class，或真正的共享语义规范
2. 替换全部引用
3. 删多余声明
4. 再跑精确键搜索和 `git diff --check`
5. 按 **pac-dotnet-build** 编 Desktop 项目

---

## 9. 架构漂移（P3 — 当重复处理）

标出来重构或删除 — 见 **pac-architecture**：

- ViewModel 注入 `*Repo`、`PgDb`，或直接用 Npgsql
- Application 项目引用 Infrastructure 或 Avalonia
- 新页面绕开 `AppPageBase` reload 管线
- 每页复制断连/Stale/空态逻辑，而不是共享策略
- 本该在 `packages/application/Abstractions` 的 Desktop `*Service` 接口
- AXAML code-behind 里的业务逻辑，或 View 里超过 50 行非 UI 逻辑
- `MainWindowViewModel` 之外第二套连通性 toast
- 与 `PageDataAvailability` 并行的新 `IsBusy`/`IsLoading` 标志

**不要**把漂移重构和 P0/P1 删除 PR 混在一起。

---

## 10. 安全删除顺序

1. 证明零引用（全库 `rg`）+ 假阳性清单
2. 先删实现，再接口，再 DI 注册
3. View + ViewModel + 样式 + 资源一起删
4. 只针对已删代码的测试一起删
5. 再编再测（见 **pac-dotnet-build**）：

```bash
dotnet build PacToolkits.sln -c Release --no-restore -v minimal
dotnet test tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj -c Release --no-restore --no-build -v minimal --filter "Category!=PostgresIntegration"
```

---

## 11. 未经确认不要删

- AHK injector 用的 `runtime/agents/**`
- 引导/迁移链上的 `database/postgres/**`
- 只经反射接线的类型（`AddPageViewModels`、JSON 多态、源生成）
- `PostgresIntegration=true` 时才运行的 `PostgresIntegrationTests`

---

## 12. 审计报告格式

每条发现：

- **位置：** `path:line`
- **类型：** dead | duplicate | superseded | drift
- **严重度：** P0 | P1 | P2 | P3（见 [references/audit-workflow.md](references/audit-workflow.md)）
- **依据：** 零引用 | 被 `<canonical>` 取代 | 编译 warning | 脚本 WARN
- **假阳性核对：** [ ] code-behind [ ] 测试 [ ] 反射 [ ] JSON [ ] GetNotifiableCommands
- **一并删除：** 要一起删的文件/类型
- **如何核对：** `rg ...` 或编测命令
- **动作：** delete | merge into `<target>` | defer（原因）

按严重度分组输出（P0 在前），再按目录。

---

## 13. 防止再长回来

- 扩展 `AppPageBase`、`PageDataShell`、`PgDb`、Application Services — 不要并行实现另一套
- 共享逻辑先放 Application、`Ui/`、或 Desktop Services；ViewModel 不要承担业务逻辑
- 新 Desktop 资源过 `pac-desktop-ui` 设计 token 闸门；不要加没有独立设计规范的别名
- 加文件前跟 **pac-architecture**
