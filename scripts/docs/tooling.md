# 仓库脚本工具

`scripts/` 提供版本管理、构建发布和仓库审计。脚本与 CI 共用 `release-manifest.json` 和 Manifest V2 校验逻辑。

## 版本与发布

- `bump-version.sh` — 更新 product、Desktop、Agents、API 和数据库 schema 版本
- `check-version.sh` — 校验发布清单、生成文件和模块版本一致性
- `export-version.sh` — 按仓根清单写出各产物 `ReleaseManifest.json`、`Version.g.props`、`module.json` 字段与 API 生成代码
- `release-desktop.sh` — 打包 Desktop（Velopack）产物
- `release-agents.sh` — 校验并打包 Agents staging，包括 Host 和发布清单中的全部模块
- `resolve-release-plan.sh` — 解析本地或 CI 使用的发布计划
- `manifest-v2.sh` — Manifest V2 查询与校验函数库，模块源码目录按 `module.json` 的 ID 解析
- `prepare-server-release.sh` — 生成组件发布元数据与 SHA-256 校验清单
- `publish-server-releases.sh` — 在服务器提交三类不可变版本快照并更新通道指针

## 仓库维护

- `audit-unused-desktop-avalonia-resources.sh` — 扫描 Desktop 中未引用的样式和资源

## 本地开发

- `run-desktop-with-agents.sh` — 构建 Desktop 与 Host，在 Desktop 输出目录生成 Agents 安装布局；`--stage-only` 仅生成布局。Windows 上优先用本机 Ahk2Exe 编译模块（见 `scripts/lib/compile-ahk-modules-win.sh`），否则回退 artifacts

## Agents 运行约束

- Desktop 保存全局 Agents 配置时规范化 Host 路径和模块启用状态
- 模块业务配置独立存放，不写入 Desktop 全局配置文件
- Injector 根据 `AppWin`、窗口类和 ClassNN 限制自动化目标

## 相关文档

- [发布流程](../../docs/operations/release-flow.md)
- [Agents 运行时架构](../../docs/architecture/agents.md)
- [Agents Injector](../../runtime/agents/docs/injector.md)
