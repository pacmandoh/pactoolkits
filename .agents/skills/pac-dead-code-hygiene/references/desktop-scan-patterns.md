# Pac Desktop 扫描模式（仓库专用）

删排版 class 或 converter 前加载 **pac-desktop-ui**。删任何 VM 属性前读 [false-positive-guards.md](false-positive-guards.md)。

## 控件、样式、资源

| 信号 | 往哪找 | 动作 |
|------|--------|------|
| `App.axaml` converter 键在任何 `.axaml` 未用 | `rg YourConverterKey apps/desktop-avalonia/src/Views` + `Styles/` | 只剩自身和 `App.axaml` 引用时，删键和 converter 类 |
| `StaticResource` 键从未引用 | `rg KeyName apps/desktop-avalonia/src` | 从 `App.axaml` 和提供者删 |
| 被取代的 shell 控件 | `SectionPanel` / `SectionPanelStyles`（已移除） | 用 `CardPanel` + `CardPanelStyles`；KPI 走 `KpiTile` |
| 用全局画刷 converter 做行高亮 | `RowStateToBgBrush`、`LowStockToBgBrush`、`KpiPctToBrush` | 优先 `StatusPill`、`KpiPctToneHelper`、DataGrid 样式 |
| 孤儿 `DataGrid` / 布局 **class token** | `LowToneGrid`、`TraceStateGrid`、`TxnGrid` — `Styles/` 里零 `Selector=` | 从 `Classes=` 拿掉；删未用样式块 |
| VM 属性名 vs 排版 **class** | 遗留 C#/DTO 字段名可以；`TextBlock` 上过时 `Classes="..."` | 只修 AXAML class — 见 **pac-desktop-ui** / **pac-naming** |
| `Controls/` 里未用控件 | `rg YourControl apps/desktop-avalonia/src/Views -g "*.axaml"` | 删控件和专用样式 |

## ViewModel 状态与命令

| 信号 | 往哪找 | 动作 |
|------|--------|------|
| `IsBusy` 切换却没绑 `PageDataShell` / `IsLoadingData` | Settings、About、Tools | 用 `IsDbSchemaChecking`、`IsUpdateChecking`、区块 `*Busy`；或删没绑上的切换 |
| 与 `PageDataAvailability` 并行的标志 | `IsLoading`、`IsRefreshing`、自定义空态门 | 并进 `AppPageBase` 管线 |
| VM 属性零 View 绑定 | `rg PropertyName apps/desktop-avalonia/src/Views -g "*.axaml"` | Phase B：code-behind、测试、`GetNotifiableCommands` — 再删或补绑定 |
| 死 `[RelayCommand]` | Views **和** `GetNotifiableCommands()` **和** 代码调用都没有 | 删命令方法 |
| 空生命周期 override | `OnNavigatedTo` / `RunReloadAsync` 只调 `base` | 删 override |
| 嵌套 VM 行计算属性未绑定 | 只有 `OnPropertyChanged`（如 `ClientAliasRow`） | 删属性；绑底层字段 |
| Unlock VM 只写快照字段 | `GetSnapshot` 赋值，从未读 | 丢掉属性；私有冷却闸门留下 |
| Settings schema 标志只写 | 赋了值，从未读 | 删属性与赋值 |

## 对话框与导航

| 信号 | 往哪找 | 动作 |
|------|--------|------|
| 对话框注册了从未显示 | `DialogManager.Register` vs `IDialogService` 用法 | 删视图 + VM + 注册 |
| 页面 VM 注册了，不在侧栏 | `AppPageBase` + `ShowInSidebar == false` | 删之前确认导航命令（Settings/About） |
| 页面 VM 没有对应视图 | `FooViewModel` 没有 `FooView.axaml` | 补视图或删孤儿 VM |
| CompiledBinding 对不上 | 编译 warning AVLNxxxx / 缺成员 | 修绑定，或删死目标属性 |

## Application / Core 成员（Desktop 相邻）

| 信号 | 往哪找 | 动作 |
|------|--------|------|
| 未用 Application/Core 成员 | Record 计算属性从未读 | 删成员；兄弟 API 留下 |
| 死 DI 注册（从未注入） | 注册了，零 resolve/inject 点 | 见 [di-reflection-wiring.md](di-reflection-wiring.md) |
| 对外接口未用 | `TryGet`、默认接口成员未用 | 裁接口；留 `GetRequired` |
| 公开辅助只在单文件用 | 同文件一处调用 | 改 `private` 或内联 |
| Resolution DTO 字段从未读 | 填了值，没有引用 | 丢掉字段 |
| 孤儿维护脚本 | 不在 CI、README、工具闸门 | 接上或删 |

## 自动扫描

```bash
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-orphan-vm-props.sh
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-unused-styles.sh
```

## 手工 ripgrep

```bash
rg "StaticResource RowStateToBg" apps/desktop-avalonia/src/Views -g "*.axaml"
rg "IsBusy" apps/desktop-avalonia/src/Views/Pages/SettingsView.axaml
rg "LowToneGrid|DGRightCell" apps/desktop-avalonia/src/Styles -g "*.axaml"
rg "DbStatusText|AutoTaskSummary" apps/desktop-avalonia/src/Views -g "*.axaml"
rg "GetNotifiableCommands" apps/desktop-avalonia/src/ViewModels/Pages -g "*.cs"
rg "your-script.sh" .github tests/scripts README.md
```
