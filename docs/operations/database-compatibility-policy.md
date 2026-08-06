# 数据库兼容与回退规则

PacToolkits 使用 `release-manifest.json` 中 Desktop 的 `minDbSchema` 和 `maxDbSchema` 定义数据库闭区间兼容范围。Agents 组件不再声明 schema 范围，而通过 `minDesktop` / `maxDesktop` 声明可配套的 Desktop 版本区间。

## 兼容性决策

| 当前数据库状态       | Desktop 行为                         |
| -------------------- | ------------------------------------ |
| 低于 `minDbSchema`   | 阻止业务访问，等待外部数据库部署完成 |
| 位于 min/max 范围内  | 允许正常运行                         |
| 高于 `maxDbSchema`   | 阻止业务访问                         |
| 无法读取 Schema 版本 | 失败关闭，阻止业务访问               |

Stable 版本连接到高于其 `maxDbSchema` 的数据库时必须停止写入。安装更早的 Stable 版本不能恢复业务访问；应升级到兼容版本，或按照生产事故流程处理。

## 迁移职责

Desktop 不包含数据库迁移执行器，安装包也不分发 migration SQL。数据库初始化、变更计划、升级和验证必须通过 `database/postgres/scripts/deploy.sh` 或 `deploy.ps1` 在服务器或受控运维节点执行。发布 manifest 只描述目标 schema 版本与组件兼容范围；数据库变更授权由服务器部署流程独立管理。
已执行的 SQL migration 不得修改、删除、重命名或覆盖；数据库演进只能追加新 migration。

## 回退原则

应用程序可以回退，数据库默认只允许前向演进。回退应用前必须确认当前数据库仍位于目标版本的兼容范围内。数据库版本高于目标 Stable 的 `maxDbSchema` 时，禁止切换到该 Stable 版本。

数据库备份恢复不是普通版本回退机制。禁止为了安装旧应用而直接覆盖当前数据库：

- 恢复操作可能丢失备份时间点之后的业务数据
- 外部系统状态、任务执行结果和审计记录可能无法同步回退
- 新 Schema 写入的数据可能无法由旧 Schema 或旧应用正确解释

备份恢复仅用于经过审批的灾难恢复，并应在隔离环境完成恢复演练、数据差异评估和停机计划。

## 发布前检查

1. 校验 Manifest 的 DB 版本与 Desktop min/max schema 范围，以及 Agents minDesktop/maxDesktop 与 Desktop 版本闭环
2. 确认 Stable 与 Beta Feed 相互隔离，且目标 manifest 来自正确通道
3. 确认没有修改或删除已执行 migration
4. 验证当前 Schema 位于 Desktop 声明的 schema 闭区间内
5. 验证 Agents 与当前 Desktop 版本配套；Schema 或配套校验失败时保持业务只读或阻止 Host 启动

Beta 的具体发布要求见 [Beta 发布规则](beta-release-policy.md)。
