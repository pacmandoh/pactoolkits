# 设计 token

主题资源用 `{DynamicResource Key}`，除非某框架集成明确要求静态查找。

## 加键之前的闸门

1. **现成 class：** 角色已有 Shad/Pac 样式 class 就用，尤其排版
2. **规范刻度：** 直接用现有圆角、图标尺寸或其它领域刻度
3. **语义规范：** 多处引用代表必须同步或能单独调的同一设计关系时，才加 token
4. **孤立值：** 就地写。选择器/属性已经表明用途时，字面量 `0` 几乎不会因为变成 token 更有意义

加任何东西之前，在 AXAML 和 C# 里搜声明和引用：

```bash
rg 'x:Key="CandidateKey"|CandidateKey' apps/desktop-avalonia/src --glob '*.{axaml,cs}'
rg 'x:Key=".*(Radius|IconSize|FontSize|Spacing|Margin|Padding)' apps/desktop-avalonia/src/Styles/Theme -g '*.axaml'
```

不要加没有引用的键。一次性键需要具体的框架要求或独立可调理由；「为了复用」本身不够。

## 资源归属

| 资源 | 规范位置 |
|------|----------|
| 颜色和画刷 | `Styles/Theme/*Colors.axaml`、`Brushes.axaml`，或现有功能色文件 |
| 字体与字号 | `Styles/Theme/Typography.axaml` |
| 排版选择器/class | `Styles/Components/Typography.axaml` |
| 圆角、图标尺寸、控件尺寸、间距、边距、内边距 | `Styles/Theme/Layout.axaml` |
| 组件/页面选择器 | `Styles/Components/` 或 `Styles/Pages/` 下对应文件 |

`Layout.axaml` 不要变成第二套排版刻度。优先 Shad `h1`–`h4`、`Large`、`Small`、`Caption` 及相关 class。只有设计有意不同且需要单独调时，才留语义字体 token。

## 刻度 vs 语义规范

- **刻度键**描述可复用的有序值：`LgCornerRadius`、`IconSizeSmall`
- **语义键**描述必须同步的设计关系：`TitleBarControlSize`、`TabContentMargin`
- 不要做只是给刻度值改名的组件别名。用 `IconSizeSmall`，不要 `DgHeaderIconSize = 14`；用 `XlCornerRadius`，不要 `SectionCardRadius = 6`
- 多个兄弟控件必须始终相等时，共享约束用一条语义键，不要每个控件单独设等值键
- Shad 或其它外部 API 要求的兼容别名可以用 `<StaticResource ... ResourceKey="CanonicalKey" />`；不要复制数字或重定义同一个 `x:Key`
- 数字碰巧相同可以成立，如果它们是彼此独立的规范。检验是：一个必须能改、另一个不动

## 间距与 Thickness

不要做平行的方向梯子，如 `MarginTopSm/Md/Lg` 和 `MarginBottomSm/Md/Lg`；同一后缀会因轴不同代表不同数字。按顺序优先：

1. 属性接受标量时，用现成标量间距键
2. 关系有稳定版式目的时，用重复的语义 `Thickness`，如 `TabContentMargin`
3. 孤立的 margin/padding 就地写字面量，尤其零

## 规范圆角与图标刻度

当前 Pac 圆角刻度在 `Styles/Theme/Layout.axaml`：

`SmCornerRadius`（2）· `MdCornerRadius`（3）· `LgCornerRadius`（4）· `XlCornerRadius`（6）

Shad 兼容键 `2XlCornerRadius`、`3XlCornerRadius`、`4XlCornerRadius` 是 `XlCornerRadius` 的静态别名；不是额外的 Pac 梯度。

当前图标刻度：

`IconSizeMini`（12）· `IconSizeSmall`（14）· `IconSizeCompact`（15）· `IconSizeDefault`（16）· `IconSizeHeader`（18）· `IconSizeDialog`（22）

匹配尺寸的图标直接用这些键。只有该组件必须单独调尺寸时，才加组件专用图标 token。

## 颜色

颜色资源用 **`Color`** 类型。

## 基础

`ForegroundColor` · `ForegroundLeadColor` · `BackgroundColor` · `MutedColor` · `BorderColor` · `BorderColor60` · `BorderColor30` · `OutlineColor` · `GhostColor` · `GhostHoverColor` · `GhostHoverColor50` · `SelectionColor`

## 主题

`PrimaryColor` · `PrimaryColor75` · `PrimaryColor50` · `PrimaryColor10` · `PrimaryForegroundColor` · `SecondaryColor` · `SecondaryColor75` · `SecondaryColor50` · `SecondaryForegroundColor` · `DestructiveColor` · `DestructiveColor75` · `DestructiveColor50` · `DestructiveColor10` · `DestructiveForegroundColor` · `AccentColor` · `ThemeColor`

## 通知（+ 5/10/20/30/60 色调）

`InfoColor` · `SuccessColor` · `WarningColor` · `ErrorColor`

Pac 另有 `WarningColor30` / `ErrorColor30` — `Styles/Theme/NotificationColors.axaml`（Dark/Light；DataGrid tone `:current`）

## Pac 扩展

`PurpleColor` · `PurpleColor10` · `PurpleColor20` · `PurpleColor60` — `Styles/Theme/PurpleColors.axaml`（Dark `#8B5CF6` / Light `#6D28D9`，与 `ChartCategoryPurpleColor` 同源）

## StatusPill 色调

| 语义 | Foreground | Light（15） | Strong（25） |
|------|------------|-------------|--------------|
| Done | `SuccessColor` | `SuccessColor10`/`20` | `SuccessColor20`/`60` |
| Warning | `WarningColor` | `WarningColor10`/`20` | `WarningColor20`/`60` |
| Danger | `ErrorColor` | `ErrorColor10`/`20` | `ErrorColor20`/`60` |
| Info | `InfoColor` | `InfoColor10`/`20` | `InfoColor20`/`60` |
| Purple | `PurpleColor` | `PurpleColor10`/`20` | `PurpleColor20`/`60` |

## Shell / 控件

`BusyAreaOverlayColor` · `CardBackgroundColor` · `DialogOverlayColor` · `DialogBackgroundColor` · `TitleBarBackgroundColor` · `WindowBackgroundColor` · `SidebarBackgroundColor` · `TabItemSelectedColor` · …

Shell 导航语义：`NavigationResources.axaml` · `ShellOverrides.axaml`

## 布局（`Layout.axaml`）

| Token | 用法 |
|-------|------|
| `ControlHeightCompact` | 筛选条输入、紧凑按钮（38px） |
| `FilterBarPadding` | `Border.FilterBarChrome` 的内边距 |
| `FilterBarGroupSpacing` | 筛选条网格的行列间距 |
| `FilterBarColumnPadding` | `Grid.FilterBarColumn` 的水平内缩 |

## 审查清单

- 归属的主题文件对吗？
- 先查过现成 Shad/Pac class？
- 加组件键之前查过现成规范刻度？
- 每个 `Sm/Md/Lg` 后缀都属于一条有序领域刻度？
- 相等的兄弟尺寸由一条共享规范表示？
- 至少两处有意义的引用，或写明了框架/独立可调理由？
- 没有重复键声明或复制的兼容值？
- 全库精确键搜索、Desktop Release 编译干净？
