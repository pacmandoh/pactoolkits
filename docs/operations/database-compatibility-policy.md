# 数据库兼容与回退政策

PacToolkits 使用 `release-manifest.json` 中 Desktop 与 Agent 的
`minDbSchema` / `maxDbSchema` 定义封闭兼容区间。实际允许范围是所有启用组件范围的交集。

## 兼容性决策

| 当前数据库状态       | 应用行为                                                                |
| -------------------- | ----------------------------------------------------------------------- |
| 低于 `minDbSchema`   | 仅在当前通道与 `migrationPolicy` 明确允许时执行前向 migration，否则只读 |
| 位于 min/max 范围内  | 允许正常运行，不执行 migration                                          |
| 高于 `maxDbSchema`   | 禁止 migration 与写入；Agent 不得启动                                   |
| 无法读取 Schema 版本 | 失败关闭，禁止 migration 与写入                                         |

Stable 遇到高于其 `maxDbSchema` 的数据库时，必须停止写入。此时不能通过安装旧版 Stable
恢复业务写入，应升级到兼容版本或按正式事故流程处理。

## 迁移策略

- `stable-only`：Stable 可按应用入口执行前向迁移；Beta 只能跟随 main 已有 DB 版本，禁止迁移
- `manual`：应用内入口全部禁止，仅允许外部受控部署
- `isolated-beta`：只允许 Beta 对显式授权的隔离数据库执行迁移

启动、重连、设置页手动操作和外部部署都必须经过相同策略服务判断，任何入口不得绕过。
已执行的 SQL migration 不得修改、删除、重命名或覆盖；数据库演进只能追加新 migration。

## 回退原则

应用程序可以回退，数据库默认只前向演进。应用回退必须先确认当前数据库仍位于目标版本的
min/max 范围内。数据库高于目标 Stable `maxDbSchema` 时，禁止切回该 Stable。

数据库备份恢复不是普通版本回退机制。禁止为了安装旧应用而直接覆盖当前数据库：

- 恢复操作可能丢失备份时间点之后的业务数据
- 外部系统状态、任务执行结果和审计记录可能无法同步回退
- 新 Schema 写入的数据可能无法由旧 Schema 或旧应用正确解释

备份恢复仅用于经过审批的灾难恢复，并应在隔离环境完成恢复演练、数据差异评估和停机计划。

## 发布前检查

1. 校验 Manifest 的 DB 版本及所有组件 min/max 范围
2. 确认 Stable/Beta Feed 隔离且目标 manifest 来自正确通道
3. 确认没有修改或删除已执行 migration
4. Beta `stable-only` 必须以 main 为 DB 基线且不得包含 migration diff
5. Beta DB 变更必须使用 `isolated-beta` 和隔离数据库授权
6. 验证 Desktop 与所有启用 Agent 对当前 Schema 均兼容
7. 验证目标版本失败时保持只读或阻止 Agent 启动

Beta 的具体发布要求见 [Beta 发布政策](beta-release-policy.md)。
