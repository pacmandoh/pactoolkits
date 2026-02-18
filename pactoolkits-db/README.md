# pactoolkits-db (Refactored)

This project is fully migrated to a unified deployment model:

- single command entry
- migration ledger (`schema_migrations`)
- schema version gate (`schema_version` vs manifest)
- read-only verification safe for non-empty databases

## Layout

```text
pactoolkits-db/
  sql/
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
cd /Users/tottidaq/RiderProjects/pactoolkits/pactoolkits-db
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
Set-Location /Users/tottidaq/RiderProjects/pactoolkits/pactoolkits-db
Copy-Item scripts/config.example.json scripts/config.json
# edit scripts/config.json

./scripts/deploy.ps1 doctor
./scripts/deploy.ps1 plan
./scripts/deploy.ps1 upgrade
./scripts/deploy.ps1 verify
./scripts/deploy.ps1 full
```

## Rules

1. Source of target DB version: `/Users/tottidaq/RiderProjects/pactoolkits/release-manifest.json` -> `dbSchemaVersion`.
2. Every schema change must be a new migration file: `Vx_y_z__description.sql`.
3. Applied migration files are immutable (checksum protected).
4. `verify` is read-only and production-safe on non-empty databases.
