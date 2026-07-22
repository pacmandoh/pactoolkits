# Beta 发布政策

本文定义 PacToolkits Beta 构建、发布、更新源和数据库验证的强制边界。
Beta 用于提前验证应用与数据库变更，不代表生产可用性承诺。

## 发布通道

- `release.channel` 必须为 `beta`
- `product.version` 与 Desktop 版本必须使用严格的 `X.Y.Z-beta.N`
- Git tag 必须为 `vX.Y.Z-beta.N`，并与 `product.version` 完全一致
- GitHub Release 必须标记为 prerelease
- `packId` 必须继续使用 `PacToolkits`，不得通过更换 package ID 绕过兼容检查
- Beta 产物只能发布到 `.../beta/` Feed，禁止写入 `.../stable/`
- 正式 Desktop 固定使用 `components.desktop.avalonia`

CI 会通过 `validate-release.yml`、`validate-release-channel.sh` 和
`validate-database-policy.sh` 拒绝 tag、版本、通道、prerelease、Feed 或数据库边界不一致的发布。

## 数据库限制

Beta 应用不能迁移共享生产数据库，并且只能连接处于其兼容范围内的数据库。
CI 会始终使用 `origin/main` 作为稳定数据库基线。普通 PR / 分支 CI 负责检查 Manifest、
路径搬迁与既有 migration 不可变性，不授予数据库发布权限。正式 Beta 发布默认沿用 Main 的
`database.postgres.version`；高于 Main 或新增 SQL migration 时必须显式授权。
已有 migration 在所有通道下均不得修改、删除或重命名。
Migration 对比按 **文件名**（`V*__*.sql`）匹配；若基线仍在历史路径
Main 中的 `pactoolkits-db/sql/migrations/`，会与当前 `database/postgres/migrations/`
按同名对齐（目录搬迁本身不计为 schema 变更）。

需要验证新的 Beta 专用数据库 migration 时，必须同时满足：

1. 使用从 Stable 环境克隆或由受控备份创建的隔离测试数据库
2. `Database.Environment=isolated`
3. 发布流程显式设置 `allow_beta_db_change=true`

普通 Beta 发布不得超出 main 数据库基线或携带 migration diff。需要数据库变化时必须走手动
`workflow_dispatch` 并勾选 `allow_beta_db_change`；授权由部署流程持有，不写入 release manifest。

隔离 Beta 数据库仅用于开发和测试，不是生产升级通道。不得把生产连接串标记为
`isolated`。

## 隔离数据库操作

使用以下脚本创建隔离库：

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --template pactoolkits_production \
  --config database/postgres/scripts/config.json
```

脚本不会覆盖同名数据库，并会写入隔离环境、克隆来源和 Beta 版本标记。
数据库凭据不得写入 release manifest、日志或发布产物。

## 升级与退出

- Stable → Beta 保存设置前必须确认风险；检查与下载具体版本时验证目标 Feed 和当前 DB 的 min/max 范围
- Beta → Stable 检查、下载和重启前必须重新读取目标版本的兼容依据并检查数据库
- 当前 DB 高于 Stable `maxDbSchema` 时，不得切回该 Stable
- 通道切换本身不得触发数据库迁移、降级或备份恢复
- 通道验证不得作为永久授权保存；检查、下载与重启必须按当前 Feed 和数据库状态重新验证
- Beta 验证结束后，隔离库按测试数据管理规则保留或销毁，不得替换生产库

数据库回退规则见 [数据库兼容与回退政策](database-compatibility-policy.md)。
