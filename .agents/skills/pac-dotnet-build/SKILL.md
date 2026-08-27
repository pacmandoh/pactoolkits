---
name: pac-dotnet-build
description: >-
  PacToolkits 的 .NET 编测流程。跑 dotnet build / dotnet test、核对 C# 改动、消除
  warning、或按 pac-git 准备 push 时加载。除非用户明确要求，或 pac-git push 前
  第 0 步需要，否则不要 dotnet restore。build/test 一律 --no-restore；test 再加
  --no-build。
---

# PacToolkits .NET 编测

跨 skill：**pac-git** push 前第 0 步是日常唯一允许 `dotnet restore` 的例外。

## 必带参数

| 命令 | 必带 |
|------|------|
| `dotnet build` | `--no-restore` |
| `dotnet test` | `--no-restore --no-build` |

build 和 test 都加 **`-v minimal`**。

## Shell 权限

**强制：** 每次 `dotnet build`、`dotnet test`、push 前闸门脚本，Shell 都要 **`required_permissions: ["all"]`**。`.cursor/rules/pac-dotnet-build-gate.mdc`（`alwaysApply`）同样要求这一条。默认沙箱经常几分钟没有任何输出；不要等待无输出后再申请权限，第一次就开满权限。

## 默认流程

按改动风险比例编测。全库格式是 **pac-git** 的提交闸门，不要绑在每次编测上。日常按改动层选测试项目（`Desktop.Tests` / `Api.Tests` / `Agents.Contracts.Tests` / `UiTests`）；push 前仍走 **pac-git** 的全解决方案闸门。

```bash
# 编一次（用户或 CI 已经 restore 过）
dotnet build PacToolkits.sln -c Release --no-restore -v minimal

# 默认核对（示例：Desktop 单测；按改动层换成 Api / Agents.Contracts / UiTests）
dotnet test tests/PacToolkits.Desktop.Tests/PacToolkits.Desktop.Tests.csproj -c Release --no-restore --no-build -v minimal --filter "Category!=PostgresIntegration"
```

单项目：

```bash
dotnet build path/to/Tests.csproj -c Release --no-restore -v minimal
dotnet test path/to/Tests.csproj -c Release --no-restore --no-build -v minimal
```

只改了代码时：先编受影响的项目/测试项目一次，再用 `--no-build` 跑测试：

```bash
dotnet build path/to/Tests.csproj -c Release --no-restore -v minimal
dotnet test path/to/Tests.csproj -c Release --no-restore --no-build -v minimal --filter "FullyQualifiedName~SomeTests"
```

## 不要跑

- **`dotnet restore`** — agent 不跑；缺包由用户或 CI restore（例外：pac-git push 前第 0 步）
- **不带 `--no-build` 的 `dotnet test`** — 先单独 build，再 `--no-build` 测
- **不带 `--no-restore` 的 `dotnet build` / `dotnet test`**

`--no-restore` 失败（缺 assets、NU1100/NU1202、Assets file not found）时 **停下，让用户本机 restore** — 除非用户明确要求或适用 pac-git 第 0 步，否则不要自己 restore。

`--no-build` 测试因缺 testhost/程序集失败时：**用 `--no-restore` 再编一次**，然后继续 `--no-build` 测。

## 编译 warning

**零 warning：** 碰到的项目 `dotnet build` 必须是 **0 Warning(s)**。改动里不要留 IDE/analyzer warning（IDE*、CS*）。

- 每次编完看 warning 列表；与本次功能或修复一并消除，不要留到「稍后」
- 常见：删未用 `using`、简化空检查（`?.`、`??`）、修可空引用对不上
- 有意保留、又无法干净消除时，就地 `#pragma` 或 `[SuppressMessage]` 加一行说明 — 不要项目级关 analyzer
- push 前全解决方案闸门（见 **pac-git**）也必须 **0 warnings**

## 其它

- 能限定到具体测试项目/filter 就不要整解决方案再编一遍
- 仅当需要更少输出时用 `-v q`，默认仍是 `-v minimal`
- 解决方案入口：仓库根的 `PacToolkits.sln`
