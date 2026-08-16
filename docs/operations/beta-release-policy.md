# Beta 发布规则

本文规定 PacToolkits Beta 构建、发布、更新源和数据库验证的强制边界。Beta 版本用于在隔离环境提前验证应用与数据库变更，不构成生产可用性承诺。

## 发布通道

- `release.channel` 必须为 `beta`
- `product.version` 与 Desktop 版本必须使用 `X.Y.Z-beta.N` 格式
- Git tag 必须使用 `vX.Y.Z-beta.N` 格式，并与 `product.version` 完全一致
- GitHub Release 必须标记为预发布版本
- `packId` 必须保持为 `PacToolkits`，不得通过更换 package ID 绕过兼容性检查
- Beta 产物只更新组件目录的 `beta` 指针，不得更新 Stable 使用的 `current`
- Desktop 组件固定使用 `components.desktop.avalonia`

CI 通过 `validate-release.yml`、`validate-release-channel.sh` 和 `validate-database-policy.sh` 拒绝 tag、版本、通道、预发布标记或数据库边界不一致的发布。

## 数据库限制

PacAPI 只能连接处于其兼容范围内的数据库，不得在共享生产库上执行 schema 变更。库变更在服务器或受控运维节点通过 `database/postgres/scripts/deploy.sh` 部署。

CI 的 `validate-database-policy.sh` 相对 `origin/main` 只检查 **已存在 migration 的不可变性**（禁止修改、删除或重命名）。允许追加新 migration 或提高 manifest 中的 `database.postgres.version`；实际是否升级生产或共享库由运维流程决定，不由 Desktop 或 CI 授权开关代管。

Migration 差异按文件名（`V*__*.sql`）匹配。基线仍使用历史路径 `pactoolkits-db/sql/migrations/` 时，CI 会按同名文件与当前 `database/postgres/migrations/` 对齐；仅移动目录不视为 schema 变更。

Beta 环境应使用与生产隔离的测试库验证 schema 变更；该隔离要求属于运维与测试流程，不由应用内配置或 CI 布尔开关强制执行。

## 升级与退出

- Stable 切换至 Beta 时，保存设置前必须确认风险；检查和下载具体版本时确认目标通道 Feed 与清单产品版本
- Beta 切换至 Stable 时，检查、下载和重启前再读目标通道清单并核对产品版本
- 当前库高于 PacAPI `maxDbSchema` 时，该 PacAPI 停止写入
- 通道切换只换更新源
- Beta 验证结束后，隔离库按测试数据管理规则保留或销毁，不得替换生产库

数据库回退规则见 [数据库兼容与回退规则](database-compatibility-policy.md)。
