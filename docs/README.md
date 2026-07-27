# 文档索引

`docs/` 保存跨组件架构与运维规范；各子项目的 README 和 `docs/` 目录保存组件专属说明。

## 架构

| 文档                                                    | 内容                                 |
| ------------------------------------------------------- | ------------------------------------ |
| [monorepo-layout.md](./architecture/monorepo-layout.md) | 目录职责与解决方案成员               |
| [layering.md](./architecture/layering.md)               | 依赖方向与各层职责                   |
| [agents.md](./architecture/agents.md)                   | Agents Host、模块、进程与控制文件    |
| [desktop-state.md](./architecture/desktop-state.md)     | Desktop 连接、页面可用性与区块空状态 |

## 运维

| 文档                                                                              | 内容                                      |
| --------------------------------------------------------------------------------- | ----------------------------------------- |
| [release-flow.md](./operations/release-flow.md)                                   | Manifest、CI、打包、更新、Agents 路径解析 |
| [beta-release-policy.md](./operations/beta-release-policy.md)                     | Beta 通道与隔离库                         |
| [database-compatibility-policy.md](./operations/database-compatibility-policy.md) | Schema 兼容与回退                         |

## 模块入口

- [Desktop](../apps/desktop-avalonia/README.md)
- [Agents](../runtime/agents/README.md)
- [PostgreSQL](../database/postgres/README.md)
- [脚本工具](../scripts/docs/tooling.md)
