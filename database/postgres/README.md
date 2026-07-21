# PostgreSQL（database/postgres）

统一部署模型：

- 单一命令入口
- migration 账本（`schema_migrations`）
- schema 版本门禁（`schema_version` vs manifest）
- 对非空库安全的只读 verify

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

1. 目标 DB 版本来源：`../../release-manifest.json` → `components.database.postgres.version`。
2. 每次 schema 变更必须新增 migration 文件：`Vx_y_z__description.sql`。
3. 已应用的 migration 文件不可变（校验和保护）。
4. `verify` 只读，对非空生产库安全。

## 隔离 Beta 数据库

Beta 库仅由运维显式命令创建；Desktop 应用不会执行这些脚本。

在 macOS/Linux 上从 Stable 库克隆：

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --template pactoolkits_production \
  --config database/postgres/scripts/config.json
```

模板库不得有活跃连接。脚本会在克隆前检查；请先断开应用与管理会话。

从 Stable 备份恢复：

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --backup /secure/backups/pactoolkits.dump \
  --config database/postgres/scripts/config.json
```

Windows PowerShell 策略相同：

```powershell
./scripts/create-beta-database.ps1 `
  -Version 0.18.0-beta.1 `
  -TemplateDatabase pactoolkits_production `
  -ConfigPath database/postgres/scripts/config.json
```

生成库名形如 `pactoolkits_beta_0_18_0_beta_1`。已存在同名库时不会覆盖或删除。克隆成功后写入：

- `Database.Environment=isolated`
- `Database.AllowBetaMigrations=true`
- `Database.Source=production-clone`
- `Database.BetaVersion=<version>`

完成时打印的连接串故意不含密码。

## 文档

- [业务主题与职责](./docs/overview.md)
- [数据库兼容与回退政策](../../docs/operations/database-compatibility-policy.md)
- [Beta 发布政策](../../docs/operations/beta-release-policy.md)
- [发布流程](../../docs/operations/release-flow.md)
