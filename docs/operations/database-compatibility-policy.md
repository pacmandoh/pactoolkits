# 数据库兼容与回退规则

PacToolkits 用 `release-manifest.json` 里 Desktop 的 `minDbSchema` 与 `maxDbSchema` 定义 **Desktop 业务访问** 的库 schema 闭区间。Agents 包在安装树旁 `ReleaseManifest.json` 声明可配套的 Desktop 版本（`components.agents.minDesktop`、`maxDesktop`）；运行时读 Agents 树，方便 Agents 与 Desktop 分开发版或自更新。

依赖库的模块：schema 闭区间写在清单 `components.agents.modules.<Id>` 的 minDbSchema 与 maxDbSchema（须成对齐全，或两项皆缺表示不依赖库；只写一半非法），由 `export-version.sh` 写入对应 `module.json`。Desktop 在写入 desired 前读安装树 `module.json`，用当前库版本与该区间做 `SemVerRange.Classify`（纯 X.Y.Z）；Host 不连库、不判 schema。

## 兼容性决策

| 当前数据库状态           | Desktop 业务                    | 库依赖模块 mount                           |
| ------------------------ | ------------------------------- | ------------------------------------------ |
| 低于模块 min             | （另见 Desktop 区间）           | 拒绝该模块                                 |
| 高于模块 max             | （另见 Desktop 区间）           | 拒绝该模块                                 |
| 位于模块 min/max         | 若亦在 Desktop 区间内则允许业务 | 允许 mount（且库已连接）                   |
| 低于 / 高于 Desktop 区间 | 阻止业务访问                    | 仍按 **模块自身** 区间判定（可更严或更宽） |
| 无法读取 Schema 版本     | 失败关闭，阻止业务访问          | 拒绝库依赖模块 mount                       |

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

1. 校验 Manifest 的 DB 版本与 Desktop min/max schema 范围，Agents 包 minDesktop/maxDesktop 与 Desktop 版本一致，以及各库依赖模块的 min/maxDbSchema
2. 确认 Stable 与 Beta Feed 相互隔离，且目标 manifest 来自正确通道
3. 确认没有修改或删除已执行 migration
4. 验证当前 Schema 位于 Desktop 声明的 schema 闭区间内
5. 验证 Agents 安装树 `ReleaseManifest` 可解析，且 minDesktop、maxDesktop 与当前 Desktop 版本配套（清单缺失、区间不全或 Desktop 版本未知则失败）；校验依赖库的模块在当前 Schema 下是否可 mount（区间来自 `module.json`，须与清单模块条目经 export 后一致）；失败时阻止或不挂载对应模块

Beta 的具体发布要求见 [Beta 发布规则](beta-release-policy.md)。
