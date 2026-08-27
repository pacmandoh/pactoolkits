---
name: pac-git
description: >-
  PacToolkits git 提交与 push。每次 git commit / git push、用户要求提交或 push、
  起草提交说明、跑提交时格式检查、或修复 push 前编测/工具闸门失败时加载。含
  Conventional Commits、各 agent 的作者行为、提交与 push 闸门边界。
---

# PacToolkits Git 提交与 Push

push 前编测参数见 **pac-dotnet-build**。

---

## 提交说明格式

标题用 [Conventional Commits](https://www.conventionalcommits.org/)：

```
type(scope): short imperative summary
```

例子：`fix(desktop): cast cross-context compiled bindings`、`chore: bump package deps`。

**scope：**

| scope | 路径 / 用法 |
|-------|-------------|
| **`desktop`** | `apps/desktop-avalonia` — 生产 Desktop（Avalonia）；UI、VM、AXAML、仅 Desktop 适配的默认 |
| **`agents`** | `runtime/agents`、Agents host/模块 CI/发布脚本 |
| **`application`** / **`infrastructure`** / **`core`** | 对应 `packages/*` 层 |
| **省略** | 横切或从 diff 一眼能看出来（例如整库 chore） |

不要用已退役的 **`ui`** / **`electron`**，也不要用长形式 **`desktop-avalonia`**。

**正文可选。** 整条说明要短、要清楚。标题已经说完就只写标题；从标题看不出的原因、多件独立改动、破坏性说明等，用 `- ` 要点补。不要为了压行数硬只写标题，也不要略掉有用上下文。diff 里已经显然的实现细节不要写。

有正文时 **只用要点**（标题后空一行，然后要点）：

```
fix(desktop): cast cross-context compiled bindings in msfx and tools views

- use explicit ViewModel casts for DataTemplate commands in MsfxLinkView and ToolsCenterView
- fix `DataGrid` column visibility bindings so compiled bindings resolve parent DataContext at build time
```

正文规则（仅当有正文时）：

- 每行必须以 `- `（短横 + 空格）开头
- `- ` 后面第一个字母小写
- 一条一点；正文不要散文段
- 不要为了凑格式加正文；标题够了就跳过

### 包版本 bump

- **1–2 个包：** 只写标题 — `` chore: bump `Package` to `x.y.z` ``；两个包用 `and` 连
- **3 个及以上：** 标题固定 `chore: bump package deps`；正文逐个列出

```
chore: bump package deps

- `Microsoft.Extensions` and `Microsoft.AspNetCore` to `10.0.11`
- `Microsoft.Extensions.Http.Resilience` to `10.9.0`
- `Lucide.Avalonia` to `0.2.17`
```

包名和版本加反引号。不要把三个及以上 bump 写入标题。

### 按信息量判短

按 **信息量** 判短，不按字符数。短句也可以是废话；必要的一行可以更长，因为它点了代码符号。

- 标题一句直接概括这次改动。去掉重复的 scope/平台上下文、叠着的解释、空形容词、以及该留在 diff 或可选要点里的实现细节
- 每条正文要点都要提供标题或其它要点没说过的信息。只复述标题的删掉
- 优先直接动宾。条件已由标题交代时，压缩 `when ... so ...`、`in order to ...`、`so that ...` 这类架子
- 会影响以后维护、且标题没交代的原因可以留一句；diff 已经展示的逐步叙述删掉
- 标题已经圈定的共享条件（例如最大化 Windows 窗口 chrome）不要每条要点再重复

**禁止的架子（提交前改掉）：**

| 禁 | 改成 |
|----|------|
| `when X changes` / `when X is empty` | 名词短语或动宾：`install-channel auto-converge` 优于 `on install-channel change`；`empty-stamp channel mismatches` |
| `through X` / `via X` 当空胶水 | 点出动作：`move normalize into \`AppUpdatePolicy\``，不是 `normalize through \`AppUpdatePolicy\`` |
| `from architecture docs` / `in the docs` | 点文档名，或标题已是 `docs:` 时干脆不写路径 |
| `for … so that …` / `in order to …` | 删；除非原因标题没说、且需要额外说明 |

坏 vs 好：

```text
# bad
- stamp SeenInstalledChannel and auto-converge when the install channel changes
- normalize UpdateOptions through AppUpdatePolicy
- update architecture docs from the old layout
- rewrite docs around the previous tree

# good
- add `SeenInstalledChannel` stamp and install-channel auto-converge
- move `UpdateOptions` normalize into `AppUpdatePolicy`
- document Desktop paths in `desktop-layout.md`
- align `layering.md` with current package folders
```

例子 — 留下有用机制，不要重复标题里的上下文：

```text
fix(desktop): inset maximized window chrome on Windows

- offset `BorderOnly` overscan with Win32 frame margins
- clear `RootCornerRadius` around caption buttons
```

### 标题和正文里的代码标识符

**代码符号**用反引号包 — 和文档行内代码一样：

- 控件 / 样式 / 模板名：`SectionGrid`、`SectionPanel`、`DataGridPager`、`KpiTile`
- 控件上的 ShadUI / AXAML **class**：`Icon`、`Ghost`、`Primary`、`Outline`、`IndexedGrid`
- 模板部件：`PART_TopRightCorner`、`PART_BorderContainer`
- 类型、类、方法、属性：`QueueTabPageReload`、`SidebarItem`、`ReloadCoreAsync`
- 点到具体键时的 token / 资源键：`DialogBackgroundColor`、`DataGridPagerComboHeight`

**复数 / 语法：** 只给 **符号** 加反引号 — 写 `` `DataGrid` index columns ``、`` all `DataGrid` lists ``，或英文复数 *DataGrids* — 不要 `` `DataGrid`s ``（`s` 落在反引号外）。

**不要**给整个 scope 或普通词加反引号（`desktop`、`fix`、`refactor`）。

**标题也一样：** 摘要点到类型、视图、服务、文件时包起来 — 例如 `` fix(desktop): cancel reload in `MsfxMappingBatchDialogViewModel` ``，不要在标题里裸写 `MsfxMappingBatchDialogViewModel`。

**UI 文案：** 提交说明是英文。标签/文案改动用英文描述（`rename usage column header`）— **不要**把本地化（例如中文）UI 字符串写入标题或要点。

### 提交说明写什么

写 **代码或产品行为** — 这次 diff 对仓库做了什么。

**标题和正文禁止：**

- 内部任务 / 路线图标签：`P0`、`P1`、`PR-0A`、`P0C`、迭代名、规划文档章节号
- 把验收清单写进提交说明（`manual smoke`、`§3.6`、rollout 周标签）
- Agent/会话元数据（`landed`、`handoff`、`as discussed`）

用功能和类型名（`MsfxMappingBatchDialogViewModel`、`BackgroundTaskRunner`、`log-events.md`）。

**不要把这些规则再写入 git 仓库文档**（不要 `docs/development/git-commit.md` 之类）。提交约定只放 `.agents/skills/pac-git/`。

### Desktop AXAML / 样式表提交 — 选对 **type**

**样式表工作不要用 `style(desktop)`。** Conventional Commits 的 `style` = 无行为变化、只格式。Pac 的 `Styles/*.axaml` 以及排版或 class 抽取用 **`refactor`**、**`fix`** 或 **`chore`**。

| 意图 | Type | 标题模式 |
|------|------|----------|
| 只跑 xamlstyler（缩进、属性顺序） | **`style(desktop)`** | `` format `InventoryOverviewView` markup `` · `` format `InventoryOverviewView` and `MsfxMappingBatchDialogView` markup `` |
| 排版 / 可见 UI 规则修复 | **`fix(desktop)`** | `` use `Caption Muted` on msfx panels `` · `` replace muted `Opacity` with `Caption Muted` in `MsfxMappingBatchDialogView` `` |
| 样式表抽取、token、class 抽取 | **`refactor(desktop)`** | `` extract `DeferredContentHost` styles `` · `` add `CardPanel` styles and layout tokens `` |
| 删死 markup class（不是有意改视觉） | **`chore(desktop)`** 或 **`refactor(desktop)`** | `` remove dead markup classes in settings views `` |

**Markup 格式规则**（仅 type **`style`**）：

| 碰到的文件 | 标题模式 |
|------------|----------|
| **1–2** 个视图/控件 AXAML | 点每个根类型；优先 **`markup`** 而不是 **`axaml`** — 控件和对应 `Styles/*.axaml` 都只是 xamlstyler 时，说 `` format `CircleProgressRing` control and styles markup `` |
| **很多** 视图/样式 AXAML | `` format desktop axaml markup `` · `` format msfx panel markup `` |

**禁止：** `` style(desktop): fix ... `` · `` style(desktop): add ... styles `` · `` style(desktop): extract ... `` — type 和动词必须对得上。

---

## 作者

- **禁止**加 `Co-authored-by: Cursor` 或任何 Cursor 合著 trailer
- **Codex：** 普通 `git commit`。Codex 不会写入合著 trailer，所以不要仅为作者问题用 `git commit-tree`、`git reset` 或改历史
- **仅 Cursor：** 若其普通提交路径会注入 Cursor 合著 trailer，用 `git commit-tree` + `git reset --mixed` 避开客户端行为：

```bash
TREE=$(git write-tree)   # 只改正文时也可用 git rev-parse 'HEAD^{tree}'
PARENT=$(git rev-parse HEAD)
NEW=$(git commit-tree "$TREE" -p "$PARENT" -m "$(cat <<'EOF'
type(scope): subject
EOF
)")
# 需要正文时：
# NEW=$(git commit-tree "$TREE" -p "$PARENT" -m "$(cat <<'EOF'
# type(scope): subject
#
# - first bullet in lowercase
# - second bullet in lowercase
# EOF
# )")
git reset --mixed "$NEW"
```

## 何时提交

- 只有用户明确要求时才建提交
- 不清楚就先问
- 除非明确要求，否则不要改 git config、强推 main、或跳过 hook

## 提交时格式闸门

编辑完成后、**提交前立刻**跑一次格式检查：

```bash
FORMAT_CHANGED=1 ./scripts/check-format.sh
```

`FORMAT_CHANGED=1` 把 **所有** 格式器（dotnet / shfmt / prettier / xaml / ahk）限制在各自根下 git 已改的文件。CI（`.github/workflows/test.yml`）不带这个环境变量，仍跑全树 — 不要削弱那份 job。

- 不要每次改完代码或跟着每次增量编测就跑格式检查
- 失败则 `FORMAT_CHANGED=1 ./scripts/format.sh`，看 diff，再 `FORMAT_CHANGED=1 ./scripts/check-format.sh`，通过后再提交
- 检查通过只覆盖「即将提交的那些文件还没再被改过」
- 本地复现 CI 格式失败：跑 **不带** `FORMAT_CHANGED` 的 `./scripts/check-format.sh`

## 提交安全

- 不要提交机密（`.env`、凭据）
- 在 Cursor 里，若用了客户端合著规避，用 `git log -1 --format=full` 核对

---

## 何时 push

- 只有用户 **明确要求** 时才 push
- 除非明确要求，否则不要强推 `main`/`master`/`beta`
- 除非明确要求，否则不要跳过 hook（`--no-verify`）

## Push 前闸门（必过）

**全部步骤通过之前不要 push。** 对齐 CI（`.github/workflows/test.yml`）。

在仓库根跑，Shell **`required_permissions: ["all"]`**。

```bash
# 0. 后续步骤因缺 assets / NU1100 失败时，restore 一次
dotnet restore PacToolkits.sln

# 1. 确认提交时格式检查已过（FORMAT_CHANGED=1）
# 那次检查之后若又改了已跟踪文件，才重跑

# 2. 编 + 测
dotnet build PacToolkits.sln -c Release --no-restore -v minimal
dotnet test PacToolkits.sln -c Release --no-restore --no-build -v minimal --filter "Category!=PostgresIntegration"

# 3. 工具脚本
chmod +x scripts/*.sh tests/scripts/*.sh
./tests/scripts/run-tooling-tests.sh
```

Build 输出必须 **0 Warning(s)** — 见 **pac-dotnet-build** 零 warning 规则。

第 0 步之后，编测优先 `--no-restore`。第 0 步 **仅** 在本 push 前闸门缺 assets 时允许。

## 某步失败时

1. **先修根因**（格式、编译错、测试失败、脚本失败）
2. **重跑受影响的编测/工具检查。** 提交时格式已通过则保留，除非之后又改了已跟踪文件
3. 新改动 **先提交再 push**；那次提交自己跑一次格式检查。Codex 用普通 `git commit`；`commit-tree` 规避仅 Cursor
4. 闸门在即将 push 的那些提交上通过之后才 push

### 提交时格式失败

`FORMAT_CHANGED=1 ./scripts/check-format.sh` 失败时：

```bash
FORMAT_CHANGED=1 ./scripts/format.sh
git diff --stat
FORMAT_CHANGED=1 ./scripts/check-format.sh   # 提交前必须过
```

然后提交审过的格式改动。push 前不要再跑格式检查，除非成功那次之后又改了已跟踪文件。

本机工具：`dotnet`、`shfmt`、`node`/`npx`（prettier）— 与 CI 相同。

## Push 命令

闸门通过后：

```bash
git push -u origin HEAD   # 该分支第一次 push
# 或
git push
```

先用 `git status` 看分支状态。不要 push 机密或无意的未跟踪文件。

## 摘要

**先修，提交前格式检查一次，Codex 用普通 `git commit`，再过编测/工具闸门，然后 push。** 闸门失败不要 push。
