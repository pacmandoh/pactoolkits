# 仓库脚本工具

`scripts/` 统一管理版本号、构建发布、资源审计以及 Windows 侧部署同步，保证 Desktop、Agents、DB 三端协同演进。

## 版本与发布

- `bump-version.sh` — 提升 product / desktop / Agents / DB schema 版本
- `check-version.sh` — 校验仓库内版本与导出产物一致
- `export-version.sh` — 将 `release-manifest.json` 导出为 `Version.g.props`、`ReleaseManifest.json`、`module.json` 版本等（**不再**写 `ReleaseVersion.json`）
- `release-desktop.sh` — 打包并发布 Desktop（Velopack）产物
- `release-agents.sh` — 打包并发布 Agents 产物（staging：`Agents.exe` + manifest 中全部 `Modules/<Id>/`）
- `resolve-release-plan.sh` — 本地解析发布计划（供 CI / 本地对齐）
- `manifest-v2.sh` — manifest 查询/校验公共库（被其它脚本 source；模块源码目录按 `module.json` id 解析）

## 仓库维护

- `audit-legacy-identity.sh` — 检测 pre-monorepo 路径（`pactoolkits-ui`、`pactoolkits-db` 等）是否意外回归
- `audit-unused-desktop-avalonia-resources.sh` — 扫描 Desktop 未引用样式 / 资源

## 本地开发

- `run-desktop-with-agents.sh` — 构建 Desktop 与 Host，并在 Desktop 输出目录模拟正式 `Agents/Modules/<Id>` 布局后启动；`--stage-only` 仅生成布局

## Windows 部署 / 同步

- `create_sync_task.ps1` — 双网环境下的静默更新同步计划任务
- `sync_pactoolkits_uu.ps1` — 远端更新源 → 本地目录（BITS，失败回退 `Invoke-WebRequest`）

## 运维补充

- Desktop 保存 Agents 配置时会补全 / 收敛必要字段
- Injector 按配置中的窗口类、完整 ClassNN、`AppWin` 门控执行（见 [Injector](../../runtime/agents/docs/injector.md)）
- Host / 模块进程模型见 [Agents 运行时架构](../../docs/architecture/agents.md)

## 相关文档

- [发布流程](../../docs/operations/release-flow.md)
- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Agents Injector](../../runtime/agents/docs/injector.md)
