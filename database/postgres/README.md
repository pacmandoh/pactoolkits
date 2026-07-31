# PostgreSQL（database/postgres）

数据库部署采用统一入口和可审计迁移模型：

- 单一命令入口
- migration 执行记录（`schema_migrations`）
- schema 版本兼容校验（`schema_version` 与发布清单）
- 可安全用于非空数据库的只读验证

## 布局

```text
database/postgres/
  bootstrap/
    000_init_meta.sql
  migrations/
    V1_2_0__baseline.sql
  verify/
    01_structure.sql
    02_constraints.sql
    03_schema_version.sql
  scripts/
    deploy.sh
    deploy.ps1
    config.example.json
    lib/
      common.sh
      migrate.sh
      verify.sh
```

## 命令

### macOS / Linux

```bash
cd database/postgres
cp scripts/config.example.json scripts/config.json
# 编辑 scripts/config.json

./scripts/deploy.sh doctor
./scripts/deploy.sh plan
./scripts/deploy.sh upgrade
./scripts/deploy.sh verify
./scripts/deploy.sh full
./scripts/deploy.sh realign-checksums
```

### Windows PowerShell

```powershell
Set-Location database/postgres
Copy-Item scripts/config.example.json scripts/config.json
# 编辑 scripts/config.json

./scripts/deploy.ps1 doctor
./scripts/deploy.ps1 plan
./scripts/deploy.ps1 upgrade
./scripts/deploy.ps1 verify
./scripts/deploy.ps1 full
```

## 规则

1. 目标数据库版本由 `../../release-manifest.json` 的 `components.database.postgres.version` 定义
2. 每次 schema 变更必须新增 `Vx_y_z__description.sql` migration 文件
3. 已应用的 migration 文件受校验和保护，不得修改
4. `verify` 仅执行只读检查，可用于非空生产数据库
5. `realign-checksums`：校正已应用行的 `checksum` 与/或短 `name` 至当前工作区 `*.sql`（应为 LF），**每次变更都追加 `note`**，不重跑 SQL。checksum 已对齐但 `name` 仍是旧全文件名时也会改名并记 note。生产使用前应确认 checksum 差异为换行问题；脚本会对「既非 LF 也非 CRLF」的 mismatch 打 warn 仍校正
6. `upgrade` 一次拉取已应用 checksum 后本地校验；已应用的 migration 不再逐条 `psql` / 逐条打 skip 日志

## 文档

- [业务主题与职责](./docs/overview.md)
- [数据库兼容与回退规则](../../docs/operations/database-compatibility-policy.md)
- [Beta 发布规则](../../docs/operations/beta-release-policy.md)
- [发布流程](../../docs/operations/release-flow.md)
