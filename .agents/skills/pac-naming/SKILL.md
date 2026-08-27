---
name: pac-naming
description: >-
  PacToolkits 命名：先 debit 再起名；短、跟旁边已有的名字；同目录 peer
  比较；稳定边界不乱改。在 apps/ 或 packages/ 写新符号前加载。新代码全跟；
  旧名只改碰到的。
---

# Naming（PacToolkits）

**一个概念一个词。** 名字跟旁边已有的一样短、直白。不为好看做大规模改名。

**新符号之前：** 列 debit、同目录里找最短 peer，再起名。

**范围：** 新代码全跟；旧代码只改这次碰到的。

---

## #1 — Debit（不要重复）

PascalCase 每一段都要有独立含义。文件夹、类型后缀、partial、闸门、参数类型、调用方已经提供的上下文，**不要再写进名字**。

常删：外层类型/目录/文件已经暗示的词；同义堆叠；成员名再重复类型（`Converters/Grid.cs`，不是 `BrushConverters.cs`）。

长度：公开成员 ≤ 3–4 段；文件 stem ≤ 2–3（bundle / partial 除外）。

**例子（只是示意）：** 闸门 `IsPanelOpen` 加 partial `DetailOps`，得到私有 `DrugText`，不是 `PanelReassignDrugText`。

---

## 转发与绑定

闸门或外层类型已经圈定范围时，去掉功能前缀。纯转发，名字不要再加一层。同一页上用途相同的 bind/command 名，跟 peer 对齐。

---

## 目录名已经包含职责

目录名算名字的一部分。角色目录里 stem 就是领域（`Dialogs/Dialogs.cs`、`Converters/Grid.cs`），不要再重复容器标签。相关的小文件放在一起；只有检索不便时才拆。

---

## 动词

名 **结果**。优先同文件里的仓库 peer。文件夹、类型、partial、闸门已经包含的词——动词名词都算——删掉；按 debit 判，不靠词表。

**稳定：** `AppPageBase.ReloadCoreAsync` — 不要改名。

---

## Token 词典

| Token | 用法 |
|-------|------|
| **Db** / **Pg** / **Sql** | 引擎 vs Postgres vs SQL 文本 |
| **Repo** / **Dto** / **Service** / **ViewModel** | 分层后缀 — 不用 `Svc` / `Vm` |
| **Msfx** / **Ahk** | 产品名 — 不要展开 |

---

## 样式（`Styles/`、`*.axaml`）

同一套 debit：文件名和根选择器已经圈定页面 — 内部选择器不要再重复页前缀（例如一个 `UserControl.SettingsPage` 根，子级用 `^ …`）。

### 设计 token 名

每个 token 只选一种命名模型：

- **规范刻度：** 稳定的领域刻度，如 `SmCornerRadius`、`IconSizeSmall`。同一领域里每个 `Sm/Md/Lg/...` 后缀必须指向同一条有序刻度。
- **语义规范：** 必须同步的设计关系，如 `TitleBarControlSize`、`TabContentMargin`。

不要只因为某个组件用了规范值就加语义别名（`DgHeaderIconSize = IconSizeSmall`、`SectionCardRadius = XlCornerRadius`）。直接用规范键。几个兄弟尺寸必须永远一致时，用一条共享语义规范，不要多个等值键。

避免方向加档的名字，如 `MarginTopSm`、`MarginBottomSm`：同一后缀很容易变成两条对不上的刻度。属性接受标量时用现成间距键；重复的版式关系用语义 `Thickness`；孤立值就地写字面量。

起新键之前，声明和引用处都搜一遍。没有引用就是死键；只用一次、尤其值是 `0` 的，需要框架要求或独立可调的理由。

---

## 稳定（不要改名）

配置/JSON 键、SQL、日志 event 字符串、本地化文案、上面列出的既定管线钩子。

---

## 清单（碰到的代码）

1. 列过 debit 了吗？每一段都有独立含义？
2. 不长于同目录 peer？
3. stem 没再叠文件夹角色？
4. 样式/键已 debit，只归属一条刻度或一条语义规范，并且放在该主题文件里？
5. 加别名之前查过现成规范 token/class？
6. 在稳定名单上 — 没动？

可选提示 grep：[references/naming-scan-patterns.md](references/naming-scan-patterns.md) · `bash .agents/skills/pac-naming/scripts/scan-naming-debit.sh`
