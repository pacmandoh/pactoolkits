# Pac 约定（建立在 ShadUI 之上）

| 场景 | 做法 |
|------|------|
| 数据页根 | `PageDataShell` + `PageDataAvailability` |
| 区块容器 | `controls:CardPanel`（列表/DataGrid）；表单/About 可用 `shad:Card` |
| 列表空态 | `EmptyStatePanel` |
| 筛选搜索 | `TextBox.SearchBox Clearable` + `TextBoxAssist.Clearable` |
| 药品 AutoComplete | `ElementAssist.Classes="Clearable"`；筛选走 `AutoCompleteFilter` + `AutoCompleteCommit`；同一用途沿用 Dashboard / ScanCode 的 VM/命令名 |
| 状态标签 | `shad:Badge` / `StatusPill` |
| 异步按钮 | `ButtonAssist.ShowProgress` |
| 对话框/Toast | 注入 `DialogManager`/`ToastManager` |
| 筛选条 | `FilterBar*` class；`ControlHeightCompact`；窄 Combo 用 `FilterBarComboAuto` |
| ComboBox 占位覆盖 | 覆盖 `TextBlock` 上 `Classes="InputPlaceholder"` |
| 业务图标 | `icons:AppIcon` |

## 规范页

- **Dashboard** — `PageDataShell`、筛选 chrome、`CardPanel`、`KpiTile`、`StatusPill`、DataGrid
- **Settings** — `SettingsLayout`、表单字段、`shad:Card`、设置导航

## 反模式

- 输入框旁另加清除按钮
- 搜索图标放左边（用 InnerRightContent）
- 写死 hex 颜色
- Shad 排版 class 已能表达角色时还写死字号
- 字号 token 放在 `Styles/Theme/Typography.axaml` 之外
- 给规范圆角/图标/字号刻度做组件别名
- 方向性 `Sm/Md/Lg` 边距梯子
- 没有引用、一次性零值、或同值别名却没有独立可调价值
- 并行空态/busy/重连 UI — 用 `PageDataShell`（**pac-architecture**）
- 用 ShadUI Demo `MainWindow` 重建 shell — 改 `MainWindow.axaml`
