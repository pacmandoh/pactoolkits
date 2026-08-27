# 审计流程（阶段、优先级、PR 范围）

## 何时加载配套 skill

| 顺序 | Skill | 用于 |
|------|-------|------|
| 1 | **pac-dead-code-hygiene**（本 skill） | 扫描、分类、安全删除 |
| 2 | **pac-architecture** | 漂移发现（§9）— 重构，不要未经确认就删 |
| 3 | **pac-desktop-ui** | 删 AXAML class/converter 前 — 确认 Shad 替代 |
| 4 | **pac-naming** | 改名 vs 删除的边界；碰到的符号做 debit + peer |
| 5 | **pac-dotnet-build** | 删完编测闸门 |
| 6 | **pac-git** | 只含清理的提交/PR；push 前核对 |

## 三阶段

### Phase A — 机械扫描

快、量大。跑脚本和 ripgrep；收集候选。

```bash
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-dead-di.sh
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-orphan-vm-props.sh
bash .agents/skills/pac-dead-code-hygiene/scripts/scan-unused-styles.sh
rg "TODO|FIXME|HACK" apps packages tests --glob "*.cs"
dotnet build PacToolkits.sln -c Release --no-restore -v minimal
```

按需读参考：

- Desktop UI：[desktop-scan-patterns.md](desktop-scan-patterns.md)
- DI/反射：[di-reflection-wiring.md](di-reflection-wiring.md)
- Application/Infrastructure：[package-layer-patterns.md](package-layer-patterns.md)

### Phase B — 假阳性确认

Phase A 每个候选在 **删除** 前过 [false-positive-guards.md](false-positive-guards.md) 清单。

重点：反射注册、`GetNotifiableCommands`、code-behind、测试、动态 `Classes.Add`。

### Phase C — 架构漂移分拣

**重复但没重设计就删不了** 的项：

- VM 注入 `*Repo` / `PgDb`
- 并行 `IsBusy` / reload / toast 系统
- 跨层重复业务规则

标 **类型: drift**，**动作: defer** 或 **merge into `<canonical>`** — 读 **pac-architecture**。

## 优先级矩阵

| 严重度 | 例子 | 动作 | PR 风格 |
|--------|------|------|---------|
| **P0** | 零引用文件、`#if false`、注释块 >5 行、空 catch 不打日志 | 立即删除 | 小清理 PR |
| **P1** | 死 DI 注册、孤儿 VM 属性、未用 converter/样式键、死 `[RelayCommand]` | Phase B 后再删 | 一页或一层一个 PR |
| **P2** | 重复 SQL、重复空态逻辑、成对分类器 | 并进规范位置 | 专门重构 PR |
| **P3** | 分层违规、并行的 reload 管线、desktop 服务本该在 Application | 推迟或重构 | 不要和 P0/P1 清理混 |

**规则：** 同一 PR 里不要把 P2/P3 行为变化和 P0/P1 删除混在一起。

## 范围模式

| 模式 | 何时 | 范围 |
|------|------|------|
| **只动到的文件** | 功能重构之后 | 只限分支 diff 里的文件 + 它们的直接孤儿 |
| **Page** | 清理一个页面 | 该功能的 VM + View + Styles + Service + Repo |
| **Layer** | 发布前清理 | 整个 `packages/application` 或 Desktop `Views/` |
| **全库** | 大审计 | Phase A 全库；P0/P1 按目录分批 |

## 建议的全库顺序

1. P0 机械可删项（注释、不可达分支）
2. P1 孤儿资源（控件、样式、converter、脚本）
3. P1 死 DI / 未用包引用
4. P1 VM 属性和命令（脚本辅助）
5. P2 重复逻辑（需要设计）
6. P3 漂移积压（单独跟踪）

## 核对闸门（每个 PR）

见 **pac-dotnet-build**：

```bash
dotnet build PacToolkits.sln -c Release --no-restore -v minimal
dotnet test tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj -c Release --no-restore --no-build -v minimal --filter "Category!=PostgresIntegration"
```

## 报告输出

用 SKILL.md §12 的格式（位置、类型、严重度、依据、假阳性核对、一并删除、如何核对、动作）。

报告按严重度分段（P0 在前），再按目录。
