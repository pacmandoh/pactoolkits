---
name: pac-desktop-ui
description: >-
  PacToolkits Desktop UI — ShadUI 0.2.4、排版、布局分区、Pac 控件（PageDataShell、
  CardPanel、StatusPill）与 AXAML 约定。任何 .axaml、Styles/、新页面、shell chrome、
  桌面 UI 改动时加载。对照规范页（Dashboard、Settings）和 Styles/ 实现，不要把 ShadUI
  Demo MainWindow 当实现。`PageDataShell` 与 reload 管线配对 pac-architecture。
paths:
  - "**/*.axaml"
  - "**/Styles/**"
---

# Desktop UI（PacToolkits）

**包：** ShadUI **0.2.4** · `xmlns:shad="clr-namespace:ShadUI;assembly=ShadUI"`  
**注册：** 在 `App.axaml` 挂 `<shad:ShadTheme />`  
**页面数据（`PageDataShell`） / reload：** **pac-architecture**

**规范页和样式（发明 markup 之前先读）：**

| 场景 | 位置 |
|------|------|
| KPI / 筛选 / DataGrid 页 | `apps/desktop-avalonia/src/Views/Pages/Dashboard/Dashboard.axaml` |
| 筛选条 + 延迟面板 | `apps/desktop-avalonia/src/Views/Pages/Inventory/InventoryOverview.axaml`（药键修正用 `FilterBarChrome`；药品/规格与 Dashboard 相同） |
| Settings / 表单 / 导航布局 | `apps/desktop-avalonia/src/Views/Pages/Settings/Settings.axaml` |
| 窗口 + 侧栏 shell | `apps/desktop-avalonia/src/Views/Shell/MainWindow.axaml` |
| 排版 | `Styles/Components/Typography.axaml`、`Styles/Theme/Typography.axaml` |
| 布局 chrome | `Styles/Pages/Layout.axaml`、`Styles/Components/Shell.axaml` |
| 主题 token | `Styles/Theme/`、[references/tokens.md](references/tokens.md) |
| Pac 控件 | `Controls/Feedback/`、`Controls/Layout/`、`Controls/Primitives/` …（`PageDataShell`、`CardPanel`、`EmptyStatePanel`、`StatusPill`、`KpiTile`）。布局规则：**pac-architecture** / `docs/architecture/desktop-layout.md` |

**ShadUI Demo**（[accntech/shad-ui](https://github.com/accntech/shad-ui/tree/main/src/ShadUI.Demo)）只当原始 Shad API 参考。**不要**用 Demo `MainWindow` 作为应用 shell — 改 Pac 的 `MainWindow.axaml` 和现有 shell 样式。

---

## 流程

1. 打开上表里最接近的 **规范页**，按其结构实现
2. 选 **Shad 排版 class** 和 **分区布局 class**（下面几节）
3. 加资源键之前走 [references/tokens.md](references/tokens.md) 的 token 闸门：先复用 class，再复用规范刻度，只有独立设计规范才加语义 token；否则孤立值就地写
4. 颜色走主题 token — 不要 hex 字面量（[references/tokens.md](references/tokens.md)）
5. 数据页：根用 `PageDataShell` 包裹（**pac-architecture**）
6. 区块列表：`CardPanel`，空时加 `EmptyStatePanel`
7. `dotnet build` desktop 项目（**pac-dotnet-build**）

---

## Pac 控件（继续用这些）

| 场景 | 用 |
|------|----|
| 数据页根 | `PageDataShell` + `PageDataAvailability` |
| 区块容器 | `controls:CardPanel`（列表/DataGrid）；表单可用 `shad:Card` |
| 列表空态 | `EmptyStatePanel` |
| 筛选搜索 | `Classes="SearchBox Clearable"` + `TextBoxAssist.Clearable` |
| 状态 / KPI 徽章 | `controls:StatusPill`（不要自行用 Border 画胶囊） |
| 业务图标 | `icons:AppIcon`（通用 UI 字形才用 Shad `Icons`） |
| 对话框 / Toast | 注入 `DialogManager` / `ToastManager` — 不要直接 `new Window` |
| 异步按钮 | `ButtonAssist.ShowProgress` |

完整约定：[references/pac-conventions.md](references/pac-conventions.md)

---

## 排版

样式：`Styles/Components/Typography.axaml` · `Styles/Theme/Typography.axaml`

**默认：**

- 次要 / 提示 / 标签文案：`Classes="Caption Muted"`
- 区块标题：`SectionTitle` · 卡片内分组：`SubsectionTitle`
- 对话框标题：`DialogTitle` + `Caption Muted` 副标题
- KPI 数值：`h3` · 分页 / 序号列：`Caption Muted`
- **不要**用 `Opacity` 做弱文案 — 用 `Caption Muted`
- 颜色：`{DynamicResource ForegroundColor}`、`MutedColor`、`SuccessColor` 等 — 不要 hex

| Shad class | 用法 |
|------------|------|
| `h1`–`h4` | 主视觉 / 指标 |
| `p`、`Lead`、`Large`、`Small` | 正文 / 导语 / 数值 |
| `Caption` / `Caption Muted` | 紧凑文字 / 所有次要文案 |
| `Small Muted` | 稍大的次要（内容头标题） |
| `Error` | 校验字符串 |

| Pac 语义（Shad 没有 1:1 时） | 用法 |
|------------------------------|------|
| `SectionTitle` | 页 / 卡片区块头 |
| `SubsectionTitle` | 卡片内分组、空态标题 |
| `DialogTitle` / `DialogTitleLg` | 对话框 chrome |
| `AboutSubtitle` / `AboutHeroVersion` | 仅 About 页 |

Pac 把 `TextBlock.Small` 和 `TextBlock.Caption` 设成 `FontWeight=Normal`（CJK）— 保持这个覆盖。

先用 Shad 排版 class，再考虑字号 token。保留的 `FontFamily`/`FontSize` token 一律放 `Styles/Theme/Typography.axaml`；`Layout.axaml` 不要变成第二套字号刻度。只有设计有意不同于 Shad、且必须能单独调时，才加语义排版 token。

**StatusPill：** 语义图标+标签胶囊（KPI %、主状态）。色调 class：`ToneDone25`、`ToneWarning25`、`ToneDanger25`、`TonePurple15` …

---

## 布局 class 分区

按 **UI 分区** 加前缀 — `Shell` 只给固定窗口 chrome；可滚动内容用 `Content*`。

| 分区 | 前缀 | 例子 |
|------|------|------|
| 主内容 chrome | `Content*` | `ContentHeader`、`ContentWorkspace`、`ContentHeaderPadding` |
| 标题栏 | `ShellTitleBar*` | `ShellTitleBarCapsule`、`ShellTitleBarPathBar` |
| 标题栏右侧操作 | `TitleBarAction*` | `TitleBarActionIconSize` |
| 底栏状态 | `ShellStatus*` | `ShellStatusBar`、`ShellStatusItem` |
| 侧栏 | `Sidebar*` | `SidebarNavHost`、`SidebarFunctionAreaText` |
| 页面根 | `PageRoot*` | `PageRoot`、`PageRootLayout` |
| 共享筛选条 | `FilterBar*` | `FilterBarChrome`、`FilterBarColumns`、`FilterBarColumn`、`FilterBarDivider` |
| Dashboard 范围 | `Dashboard*` | `DashboardLayout`、`DashboardTabPanel` |
| Settings 范围 | `Settings*` | `SettingsLayout`、`SettingsNavHost` |

**筛选条：** 放在 `Border.FilterBarChrome`（内部用 `FilterBarPadding`；外层 `Margin=0`）。字段行：`Grid.FilterBarColumns` + `FilterBarColumn`；竖分隔：`FilterBarDivider`。内部控件从 `Inputs.axaml` 继承 `ControlHeightCompact`。窄范围 ComboBox：`Classes="FilterBarComboAuto"`。规格/药品占位覆盖：`TextBlock.InputPlaceholder`。各页药品/规格命令对齐：`ApplyDrugFilterCommand`、`ClearDrugSpecFilterCommand`（用 `IsReassignOpen` 等闸门，不要 `ClearReassign*`）。

**延迟模板：** 页面上 `FindControl` 失败时，从模板根绑定筛选 — 与规范页相同；名字还没注册则 `Loaded` 后再试。

`PageDataShell` 是 **控件名**，不是布局 class。

Token：`Styles/Theme/Layout.axaml`（布局）· `Styles/Theme/Typography.axaml`（字号刻度）。

## 设计 token 闸门

加或改名资源之前读 [references/tokens.md](references/tokens.md)。尤其：

- 圆角、图标尺寸直接用规范键；不要加只是重复同值的组件别名
- 兄弟控件必须同步时，共享约束用一条语义键，不要每个控件单独设等值键
- 不要做方向性 `Sm/Md/Lg` 厚度梯子；不要用轴后缀表示另一条对不上的数字刻度
- 一种版式关系优先用有含义的重复语义 `Thickness`；孤立值就地写，尤其 `0`
- 加键之前搜声明和全部 AXAML/C# 引用。没有引用的键、一次性别名，需要具体的框架或独立可调理由

---

## ShadUI 速查（规范页没有例子时）

| 控件 | Classes / Assist |
|------|------------------|
| Button | `Primary`、`Secondary`、`Destructive`、`Outline`、`Ghost`、`Icon` · `ButtonAssist.ShowProgress` |
| Badge / 胶囊 | `shad:Badge` 或 `StatusPill` |
| TextBox 搜索 | `SearchBox Clearable` + `TextBoxAssist.Clearable` |
| DataGrid | Demo `data-table` 路由 — 先对齐现有列表页 |
| Card 表单 | `shad:Card` Header/Footer |

---

## 反模式

- TextBox/AutoComplete 旁边另加清除 `Button`（用 Assist clearable）
- 写死颜色 hex — 用主题 token
- `Layout.axaml` 里放字号 token，或 Shad 排版 class 已经表达角色时还写死字号
- 只是给规范刻度改名的组件圆角/图标别名
- 方向尺寸梯子，如 `MarginTopSm/Md/Lg`、`MarginBottomSm/Md/Lg`
- 选择器/属性已经表明含义的零值或一次性 token
- 重复的空态/busy/重连 UI — `PageDataShell` + shell 横幅（**pac-architecture**）
- 用 ShadUI Demo `MainWindow` 重建应用 shell
- 自制状态胶囊 — 用 `StatusPill`
- 新页面上另行实现输入包装或面板壳

---

## 新页面清单

1. 对照最接近的规范页实现（`Dashboard` 或 `Settings`）
2. 数据页用 `PageDataShell`
3. 次要文字 `Caption Muted`；区块头 `SectionTitle`
4. chrome 容器用分区前缀的布局 class
5. 颜色走主题 token；排版走 Shad class
6. 加任何资源键之前跑设计 token 闸门
7. 需要时经 DI 做 Dialog/Toast
8. `dotnet build` desktop 项目
