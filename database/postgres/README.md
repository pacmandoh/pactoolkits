# pactoolkits-db (Refactored)

This project is fully migrated to a unified deployment model:

- single command entry
- migration ledger (`schema_migrations`)
- schema version gate (`schema_version` vs manifest)
- read-only verification safe for non-empty databases

## Layout

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

## Commands

### macOS / Linux

```bash
cd database/postgres
cp scripts/config.example.json scripts/config.json
# edit scripts/config.json

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
# edit scripts/config.json

./scripts/deploy.ps1 doctor
./scripts/deploy.ps1 plan
./scripts/deploy.ps1 upgrade
./scripts/deploy.ps1 verify
./scripts/deploy.ps1 full
```

## Rules

1. Source of target DB version: `../../release-manifest.json` -> `components.database.postgres.version`.
2. Every schema change must be a new migration file: `Vx_y_z__description.sql`.
3. Applied migration files are immutable (checksum protected).
4. `verify` is read-only and production-safe on non-empty databases.

## Isolated Beta database

Beta databases are created only by an explicit operator command. The Desktop
application does not run these scripts.

Clone a Stable database on macOS/Linux:

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --template pactoolkits_production \
  --config database/postgres/scripts/config.json
```

The template database must have no active connections. The scripts check this
before cloning and stop with an explicit error; disconnect application and
administrative sessions from the template database first.

Restore a Stable backup:

```bash
./scripts/create-beta-database.sh \
  --version 0.18.0-beta.1 \
  --backup /secure/backups/pactoolkits.dump \
  --config database/postgres/scripts/config.json
```

Windows PowerShell uses the same policy:

```powershell
./scripts/create-beta-database.ps1 `
  -Version 0.18.0-beta.1 `
  -TemplateDatabase pactoolkits_production `
  -ConfigPath database/postgres/scripts/config.json
```

The generated name is `pactoolkits_beta_0_18_0_beta_1`. Existing databases are
never overwritten or deleted. After cloning, the script writes:

- `Database.Environment=isolated`
- `Database.AllowBetaMigrations=true`
- `Database.Source=production-clone`
- `Database.BetaVersion=<version>`

The connection string printed at completion intentionally omits the password.
