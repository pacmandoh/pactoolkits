# Beta 发布规则

本文规定 PacToolkits Beta 构建、发布、更新源和数据库验证的强制边界。Beta 版本用于在隔离环境提前验证应用与数据库变更，不构成生产可用性承诺。

## 发布通道

- `release.channel` 必须为 `beta`
- `product.version` 与 Desktop 版本必须使用 `X.Y.Z-beta.N` 格式
- Git tag 必须使用 `vX.Y.Z-beta.N` 格式，并与 `product.version` 完全一致
- GitHub Release 必须标记为预发布版本
- `packId` 必须保持为 `PacToolkits`，不得通过更换 package ID 绕过兼容性检查
- Beta 产物只能发布到 `.../beta/` Feed，禁止写入 `.../stable/`
- Desktop 组件固定使用 `components.desktop.avalonia`

CI 通过 `validate-release.yml`、`validate-release-channel.sh` 和 `validate-database-policy.sh` 拒绝 tag、版本、通道、预发布标记、Feed 或数据库边界不一致的发布。

## 数据库限制

Beta 应用不得迁移共享生产数据库，只能连接处于其兼容范围内的数据库。CI 始终使用 `origin/main` 作为稳定数据库基线。普通拉取请求和分支 CI 仅检查 Manifest、目录迁移以及已存在 migration 的不可变性，不授予数据库发布权限。

Beta 发布默认沿用主分支的 `database.postgres.version`。数据库版本高于主分支或新增 SQL migration 时必须显式授权。所有通道中的既有 migration 均不得修改、删除或重命名。

Migration 差异按文件名（`V*__*.sql`）匹配。基线仍使用历史路径 `pactoolkits-db/sql/migrations/` 时，CI 会按同名文件与当前 `database/postgres/migrations/` 对齐；仅移动目录不视为 schema 变更。

需要验证新的 Beta 专用数据库 migration 时，必须同时满足：

1. 使用从 Stable 环境克隆或由受控备份创建的隔离测试数据库
2. `Database.Environment=isolated`
3. 发布流程显式设置 `allow_beta_db_change=true`

普通 Beta 发布不得超出主分支数据库基线或携带 migration 差异。需要发布数据库变更时，必须手动触发 `workflow_dispatch` 并启用 `allow_beta_db_change`。该授权仅对当前部署流程有效，不写入 release manifest。

隔离 Beta 数据库仅用于开发和测试，不得作为生产升级通道，也不得将生产数据库连接标记为 `isolated`。

## 升级与退出

- Stable 切换至 Beta 时，保存设置前必须确认风险；检查和下载具体版本时必须验证目标 Feed 与当前数据库的兼容范围
- Beta 切换至 Stable 时，检查、下载和重启前必须重新读取目标版本的兼容信息并检查数据库
- 当前 DB 高于 Stable `maxDbSchema` 时，不得切回该 Stable
- 通道切换本身不得触发数据库迁移、降级或备份恢复
- 通道验证不得作为永久授权保存；检查、下载与重启必须按当前 Feed 和数据库状态重新验证
- Beta 验证结束后，隔离库按测试数据管理规则保留或销毁，不得替换生产库

数据库回退规则见 [数据库兼容与回退规则](database-compatibility-policy.md)。
