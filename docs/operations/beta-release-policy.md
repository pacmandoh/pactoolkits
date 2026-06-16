# Beta 发布政策

本文定义 PacToolkits Beta 构建、发布、更新源和数据库验证的强制边界。
Beta 用于提前验证应用与数据库变更，不代表生产可用性承诺。

## 发布通道

- `release.channel` 必须为 `beta`
- `product.version` 与 Desktop 版本必须使用严格的 `X.Y.Z-beta.N`
- Git tag 必须为 `vX.Y.Z-beta.N`，并与 `product.version` 完全一致
- GitHub Release 必须标记为 prerelease
- `packId` 必须继续使用 `pactoolkits`，不得通过更换 package ID 绕过兼容检查
- Beta 产物只能发布到 `.../beta/` Feed，禁止写入 `.../stable/`
- 正式 Desktop 只能选择一个 `components.desktop.implementation`

CI 会通过 `validate-release.yml`、`validate-release-channel.sh` 和
`validate-database-policy.sh` 拒绝 tag、版本、通道、prerelease、Feed 或数据库策略不一致的发布。

## 数据库限制

Beta 应用默认不能迁移共享生产数据库。普通 Beta 应继续使用
`migrationPolicy=stable-only`，并只连接处于其兼容范围内的数据库。
CI 会始终使用 `origin/main` 作为稳定数据库基线：`stable-only` Beta 可以等于
main 的 `database-postgres.version`，但不能高于 main，也不能新增、修改、删除或重命名
SQL migration。
重构过渡期内，如果 main 仍使用 legacy manifest，CI 会用 legacy `dbSchemaVersion`
作为稳定基线；缺少可解析 DB 版本时才失败。Migration 对比会按 **文件名**
（`V*__*.sql`）跨 legacy 路径 `pactoolkits-db/sql/migrations/` 与新路径
`database/postgres/sql/migrations/` 匹配，monorepo 目录搬迁不计为 schema 变更。

需要验证新的 Beta 专用数据库 migration 时，必须同时满足：

1. `migrationPolicy=isolated-beta`
2. 使用从 Stable 环境克隆或由受控备份创建的隔离测试数据库
3. `Database.Environment=isolated`
4. `Database.AllowBetaMigrations=true`
5. 交互操作获得用户二次确认；**正式发布**（tag 推送或 `workflow_dispatch`）还需 CI 显式授权

`beta` 分支上的日常 push/PR 校验会自动允许 `isolated-beta` 相对 `origin/main`
的 migration diff，以便持续集成测试与构建；这与 release 流程的显式授权是分开的。
`stable-only` Beta 在任何 CI 场景下仍不得超出 main 基线或携带 migration diff。
tag 触发的自动 release 仅对 `stable-only` 放行；`isolated-beta` 必须走手动
`workflow_dispatch` 并勾选 `allow_beta_db_migration`。

`isolated-beta` 仅用于开发和测试，不是生产升级通道。不得把生产连接串标记为
`isolated`，也不得在共享生产数据库上开启 `AllowBetaMigrations`。
`manual` 不参与自动 Beta 发布，用于人工处理或特殊场景。

## 隔离数据库操作

使用以下脚本创建隔离库：

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --template pactoolkits_production \
  --config database/postgres/scripts/config.json
```

脚本不会覆盖同名数据库，并会写入隔离环境、迁移授权、克隆来源和 Beta 版本标记。
数据库凭据不得写入 release manifest、日志或发布产物。

## 升级与退出

- Stable → Beta 前必须确认目标 Feed 可用，且当前 DB 位于 Beta 的 min/max 范围
- Beta → Stable 前必须读取 Stable manifest 并重新检查数据库兼容范围
- 当前 DB 高于 Stable `maxDbSchema` 时，不得切回该 Stable
- 通道切换本身不得触发数据库迁移、降级或备份恢复
- Beta 验证结束后，隔离库按测试数据管理规则保留或销毁，不得替换生产库

数据库回退规则见 [数据库兼容与回退政策](database-compatibility-policy.md)。
