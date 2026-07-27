# 仓库脚本工具

`scripts/` 提供版本管理、构建发布、仓库审计和 Windows 部署同步工具。脚本与 CI 共用 `release-manifest.json` 和 Manifest V2 校验逻辑。

## 版本与发布

- `bump-version.sh` — 更新 product、Desktop、Agents 和数据库 schema 版本
- `check-version.sh` — 校验发布清单、生成文件和模块版本一致性
- `export-version.sh` — 从 `release-manifest.json` 生成 `Version.g.props`、各组件 `ReleaseManifest.json`，并同步 `module.json` 版本
- `release-desktop.sh` — 打包并发布 Desktop（Velopack）产物
- `release-agents.sh` — 校验并打包 Agents staging，包括 Host 和发布清单中的全部模块
- `resolve-release-plan.sh` — 解析本地或 CI 使用的发布计划
- `manifest-v2.sh` — Manifest V2 查询与校验函数库，模块源码目录按 `module.json` 的 ID 解析

## 仓库维护

- `audit-unused-desktop-avalonia-resources.sh` — 扫描 Desktop 中未引用的样式和资源

## 本地开发

- `run-desktop-with-agents.sh` — 构建 Desktop 与 Host，在 Desktop 输出目录生成 Agents 安装布局；`--stage-only` 仅生成布局

## Windows 部署与同步

- `create_sync_task.ps1` — 双网环境下的静默更新同步计划任务
- `sync_pactoolkits_uu.ps1` — 使用 BITS 将 Stable 与 Beta 更新源同步到本地目录；失败时改用 `Invoke-WebRequest`

同步任务直接执行 `ScriptPath` 指向的文件，不复制脚本。同名任务重复创建时会先注销旧任务再注册新任务；脚本路径不变时，只需替换 `sync_pactoolkits_uu.ps1`，无需重新创建计划任务。Stable 与 Beta 分别同步到 `feed\pactoolkits\stable` 和 `feed\pactoolkits\beta`。

## Agents 运行约束

- Desktop 保存全局 Agents 配置时规范化 Host 路径和模块启用状态
- 模块业务配置独立存放，不写入 Desktop 全局配置文件
- Injector 根据 `AppWin`、窗口类和 ClassNN 限制自动化目标

## 相关文档

- [发布流程](../../docs/operations/release-flow.md)
- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Agents Injector](../../runtime/agents/docs/injector.md)
