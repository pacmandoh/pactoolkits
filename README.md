# PacToolkits

<div align="center">

[English](./README.md) | [简体中文](./README.zh-CN.md)

<br />

<table>
  <tr>
    <td align="center" width="260" valign="top">
      <img src="./apps/desktop-avalonia/src/Assets/icon-128.png" alt="PacToolkits Desktop Icon" width="72" />
      <br />
      <strong>PacToolkits Desktop</strong>
      <br />
      <sub>Avalonia Desktop Client</sub>
      <br />
      <sub>&nbsp;</sub>
      <br />
      <img src="https://img.shields.io/badge/Business-Desktop-0f766e?style=flat-square&logo=avaloniaui&logoColor=white" alt="Business Desktop" />
      <br />
      <img src="https://img.shields.io/badge/MVVM-Avalonia-475569?style=flat-square&logo=dotnet&logoColor=white" alt="MVVM Avalonia" />
    </td>
    <td align="center" width="260" valign="top">
      <img src="./runtime/agents/injector-ahk/assets/pactoolkits-agent-injector-ahk.ico" alt="PacToolkits Agent Icon" width="72" />
      <br />
      <strong>PacToolkits Agent</strong>
      <br />
      <sub>AutoHotkey Automation Runtime</sub>
      <br />
      <sub>&nbsp;</sub>
      <br />
      <img src="https://img.shields.io/badge/Automation-AHK%20v2-92400e?style=flat-square&logo=autohotkey&logoColor=white" alt="Automation AHK v2" />
      <br />
      <img src="https://img.shields.io/badge/Execution-Task%20Driven-475569?style=flat-square&logo=postgresql&logoColor=white" alt="Execution Task Driven" />
    </td>
  </tr>
</table>

<br />

<sub><strong>Desktop</strong> for business operations · <strong>Agent</strong> for automation execution · <strong>DB</strong> for task orchestration and persistence</sub>

<br />
<br />

**Drug Trace-Code Operations Suite for Desktop, Automation, and Database Workflows**

PacToolkits Desktop, AutoHotkey automation, and PostgreSQL orchestration for drug trace-code operations.

<br />

<table>
  <tr>
    <td align="center"><a href="./LICENSE"><img src="https://img.shields.io/badge/License-GPL--3.0--or--later-blue?style=for-the-badge&logo=gnu&logoColor=white" alt="License: GPL-3.0-or-later" /></a></td>
    <td align="center"><a href="https://www.jetbrains.com/opensource/"><img src="https://img.shields.io/badge/JetBrains-Supported-000000?style=for-the-badge&logo=jetbrains&logoColor=white" alt="JetBrains" /></a></td>
    <td align="center"><img src="https://img.shields.io/badge/Platform-Windows-334155?style=for-the-badge&logo=microsoft&logoColor=white" alt="Platform" /></td>
    <td align="center"><img src="https://img.shields.io/badge/.NET-net10.0-475569?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET" /></td>
  </tr>
  <tr>
    <td align="center"><a href="./apps/desktop-avalonia/src"><img src="https://img.shields.io/badge/Desktop-Avalonia%2011-0f766e?style=for-the-badge&logo=avaloniaui&logoColor=white" alt="Desktop" /></a></td>
    <td align="center"><a href="./runtime/agents/injector-ahk"><img src="https://img.shields.io/badge/Agent-AutoHotkey%20v2-92400e?style=for-the-badge&logo=autohotkey&logoColor=white" alt="Agent" /></a></td>
    <td align="center"><a href="./database/postgres"><img src="https://img.shields.io/badge/Database-PostgreSQL-1d4ed8?style=for-the-badge&logo=postgresql&logoColor=white" alt="Database" /></a></td>
    <td align="center"><img src="https://img.shields.io/badge/Channel-stable-334155?style=for-the-badge&logo=githubactions&logoColor=white" alt="Channel" /></td>
  </tr>
  <tr>
    <td align="center"><a href="./release-manifest.json"><img src="https://img.shields.io/badge/Suite-0.17.1-475569?style=for-the-badge&logo=git&logoColor=white" alt="Suite" /></a></td>
    <td align="center"><a href="./release-manifest.json"><img src="https://img.shields.io/badge/Desktop%20Version-0.16.1-475569?style=for-the-badge&logo=git&logoColor=white" alt="Desktop Version" /></a></td>
    <td align="center"><a href="./release-manifest.json"><img src="https://img.shields.io/badge/Agent%20Version-0.6.1-475569?style=for-the-badge&logo=git&logoColor=white" alt="Agent Version" /></a></td>
    <td align="center"><a href="./release-manifest.json"><img src="https://img.shields.io/badge/DB%20Schema-1.2.23-475569?style=for-the-badge&logo=postgresql&logoColor=white" alt="DB Schema" /></a></td>
  </tr>
</table>

</div>

---

## Project Overview

**PacToolkits** is a monorepo for drug trace-code operations, combining:

- PacToolkits Desktop for business workflows and diagnostics
- an AutoHotkey runtime for semi-automatic and warehouse injection flows
- a PostgreSQL schema and migration system for ingestion, mapping, tasking, and execution state

PacToolkits is designed for environments where **drug indexing, trace-code intake, inventory workflows, and automation-assisted injection** must stay aligned across Desktop, agent runtime, and database state.

This repository is a coordinated system with:

- business-facing interaction in `apps/desktop-avalonia` (Avalonia)
- shared use cases and abstractions in `packages/application`
- PostgreSQL implementations in `packages/infrastructure`
- execution and automation in `runtime/agents/injector-ahk`
- persistence, task orchestration, and schema evolution in `database/postgres`
- a reserved future preview shell in `apps/desktop-electron` (not in release)

---

## Highlights

- Unified desktop + automation + database architecture in one repository
- Avalonia-based business client with update and diagnostics capabilities
- Lucide-based icon system with lightweight custom status and busy indicators
- AutoHotkey v2 automation agent for parse, inject, and verify workflows
- Full ClassNN-based agent configuration generated by PacToolkits Desktop
- PostgreSQL migration-based schema lifecycle with compatibility gates
- Versioned release pipeline for Desktop, agent, and DB schema compatibility
- Operational visibility for inventory, mapping, MSFX linkage, and execution queues
- MSFX bill-watch recovery and manual task discard workflows for exception handling

---

## Architecture

```mermaid
flowchart LR
    DESKTOP["apps/desktop-avalonia\nAvalonia Desktop App"]
    PKG["packages/\napplication · infrastructure · core"]
    AGENT["runtime/agents/injector-ahk\nAutoHotkey v2 Runtime"]
    DB["database/postgres\nPostgreSQL Schema + Migrations"]
    SCRIPTS["scripts/\nRelease + Version Tooling"]
    CI[".github/workflows\nBuild + Release Automation"]

    DESKTOP --> PKG
    DESKTOP -->|config / runtime control| AGENT
    AGENT -->|task claim / state sync / event logs| DB
    PKG --> DB
    SCRIPTS --> DESKTOP
    SCRIPTS --> AGENT
    SCRIPTS --> DB
    CI --> SCRIPTS
```

---

## Repository Map

```text
pactoolkits/
  apps/desktop-avalonia/src/  Current production Desktop (Avalonia)
  apps/desktop-electron/      Future Nuxt + Electron preview (placeholder, not in release)
  packages/
    core/                     Pure domain helpers (no IO)
    application/              Use cases, DTOs, service abstractions
    infrastructure/           PostgreSQL repos and DB services
    agent-contracts/          Shared Desktop ↔ Agent protocol
  runtime/agents/injector-ahk/          AutoHotkey v2 automation runtime
  database/postgres/          PostgreSQL bootstrap, migration, verify, deploy scripts
  docs/                       Architecture and operations documentation
  scripts/                    Versioning, packaging, release helpers
  .github/workflows/          CI/CD and release workflows
  PacToolkits.sln             .NET solution entry point
  release-manifest.json       Unified version source of truth
```

## Documentation

- [Monorepo layout](./docs/architecture/monorepo-layout.md)
- [Layering and dependency rules](./docs/architecture/layering.md)
- [Avalonia extraction plan](./docs/migration/avalonia-extraction-plan.md)
- [Release flow](./docs/operations/release-flow.md)
- [Beta release policy](./docs/operations/beta-release-policy.md)
- [Database compatibility policy](./docs/operations/database-compatibility-policy.md)

---

## Module Guide

## 1. `apps/desktop-avalonia`

**Role**

The desktop application is the operational center of the suite. It provides business workflows for inventory, drug indexing, scan entry, MSFX linkage, runtime control, update handling, and diagnostics. Page ViewModels call into `packages/application` services; database access lives in `packages/infrastructure`.

**Primary responsibilities**

- business dashboards and overview pages
- drug index maintenance
- trace-code entry and scan workflows
- inventory overview and reassignment workflows
- MSFX pull/map/task audit views
- MSFX bill-watch recovery and manual task discard actions
- MSFX task remap, merge, split, and manual-review flows
- AHK runtime configuration and control
- settings, logging, and update management

**Key areas**

- `Views/` and `ViewModels/`
- `Services/Application/` and `Services/Infrastructure/` (desktop-specific adapters)
- `Styles/`, `Controls/`, `Behaviors/`, `Converters/`
- `Docs/`

**Representative pages**

- `DashboardViewModel.cs`
- `DrugIndexViewModel.cs`
- `InventoryOverviewViewModel.cs`
- `ScanCodeViewModel.cs`
- `MsfxLinkViewModel.cs`
- `ToolsCenterViewModel.cs`
- `SettingsViewModel.cs`

**Technology**

- Avalonia 11
- CommunityToolkit.Mvvm
- IconPacks.Avalonia.Lucide
- SukiUI
- Velopack
- Project references: `PacToolkits.Application`, `PacToolkits.Infrastructure`, `PacToolkits.Agent.Contracts`

---

## 2. `runtime/agents/injector-ahk`

**Role**

The automation agent is the execution layer. It drives target desktop windows, parses grid content, injects trace codes, verifies outcomes, and synchronizes execution state back to PostgreSQL.

**Primary responsibilities**

- parse clipboard/grid content from target windows
- drive UI injection into target desktop applications
- verify injection results
- claim and execute warehouse inject tasks
- synchronize execution state and events back to PostgreSQL
- read config generated by PacToolkits Desktop

**Key modules**

- `main.ahk`
- `src/main_semi_auto.ahk`
- `src/msfx_task.ahk`
- `src/parse_clipboard.ahk`
- `src/ui_txn.ahk`
- `src/db_txn.ahk`
- `src/pg_exec.ahk`
- `src/utils.ahk`

**Execution model**

- `ipt/opt` flows remain atomic in AHK runtime logic
- warehouse mode consumes DB-backed inject tasks
- parse, inject, verify, and finalize are modularized
- warehouse duplicate protection is DB-backed and execution-aware
- runtime window, parse grid, verify grid, and input targets are configured through explicit full `ClassNN` fields rather than inferred suffixes
- inpatient standard flows no longer stop on warehouse-style soft checks unless warehouse mode is actually enabled
- agent runtime metadata is initialized once and reused for version tags and client identity logging
- focus/copy handling for full `ClassNN` targets avoids unnecessary window activation and keeps grid targeting stable
- warehouse task execution now captures a click anchor and row fingerprint so duplicate-success protection can distinguish same bill rows more precisely

**Key agent config fields**

PacToolkits Desktop now writes the agent runtime targets explicitly. The current key fields are:

- `OptWindowClass`: top-level class for the outpatient window
- `IptWindowClass`: top-level class for the inpatient window
- `OptParseGridClassNN`: full `ClassNN` for the outpatient parse grid
- `OptVerifyGridClassNN`: full `ClassNN` for the outpatient verify grid
- `IptParseGridClassNN`: full `ClassNN` for the inpatient parse grid
- `IptVerifyGridClassNN`: full `ClassNN` for the inpatient verify grid
- `OptInputClassNN`: full `ClassNN` for the outpatient input target
- `IptInputClassNN`: full `ClassNN` for the inpatient / warehouse input target

Grid-style `ClassNN` values such as `TcxGridSite1` and `TcxGridSite2` are resolved by class name plus ordinal index, so the runtime can focus and copy from the intended grid instead of treating the suffix as part of a literal control name.

Related warehouse execution fields:

- `WarehouseEnabled`
- `WarehouseAnchorTexts`
- `CodePickPolicy`
- `WarehouseTaskIdentifier`

---

## 3. `database/postgres`

**Role**

The database module defines the persistence and orchestration model behind the suite. It holds schema bootstrap, migrations, verification scripts, and deployment tooling.

**Primary responsibilities**

- schema bootstrap
- incremental migrations
- verification and schema gating
- deployment planning and execution
- support for staging, mapping, task queueing, execution state, and audit records

**Structure**

```text
database/postgres/
  sql/bootstrap/
  sql/migrations/
  sql/verify/
  scripts/
```

**Operational themes**

- inbound bill and detail ingestion
- trace-code staging
- drug/spec mapping
- inject task creation and queue ordering
- warehouse duplicate protection
- task reopen / retry / finalize flows
- task remap, merge, and split orchestration
- warehouse row-fingerprint dedupe for successful tasks

---

## 4. `scripts`

**Role**

This folder standardizes versioning, packaging, publishing, and operational deployment tasks so Desktop, agent, and database changes stay aligned.

**Version and release scripts**

- `bump-version.sh`
  - bumps product / desktop / agent / DB schema versions in the manifest and generated files
- `check-version.sh`
  - validates version consistency across the repository
- `export-version.sh`
  - exports manifest values into generated project files
- `release-desktop.sh`
  - packages and publishes desktop (Velopack) artifacts
- `release-agent-injector-ahk.sh`
  - packages and publishes agent-injector-ahk artifacts

**Repository maintenance**

- `audit-unused-ui-resources.sh`
  - scans the desktop project for unreferenced styles, resources, and related leftovers

**Windows deployment / sync automation**

- `create_sync_task.ps1`
  - creates a silent scheduled task for feed synchronization
  - intended for dual-network Windows deployment environments
  - prompts for Windows credentials and registers the task with password logon for more reliable background execution
- `sync_pactoolkits_uu.ps1`
  - synchronizes update feed payloads to a local folder
  - performs change detection before download
  - prefers BITS and falls back to `Invoke-WebRequest` when BITS fails
  - only releases the global mutex when the lock is actually acquired

**Operational notes**

- Desktop writes the agent runtime config and normalizes required fields on save
- agent runtime consumes explicit window / parse / verify / input targets from config
- Windows sync tasks are designed for silent execution in operational environments

---

## 5. `.github/workflows`

**Role**

CI/CD workflows provide release automation for packaging, release-note generation, and publish coordination.

**Current workflows**

- `release.yml` — orchestrates tag / manual release
- `build-agent-injector-ahk.yml` — compile AHK agent
- `build-desktop-avalonia.yml` — publish Avalonia desktop (no bundled agents)
- `build-desktop-electron-preview.yml` — Electron preview marker (not published to feed)
- `package-desktop.yml` — bundle agents per `desktop.bundles`, Velopack pack
- `generate-release-notes.yml` — release notes (tag only)
- `publish-release.yml` — GitHub Release + update feed (tag only)

## Release and Update Flow

The current release and update chain supports only `stable` and `beta`. Versioning is driven by **manifest schema v2** in [release-manifest.json](./release-manifest.json).

> Beta is a test channel, not a production database upgrade path. A Beta application
> cannot migrate a shared production database by default. Beta database work requires
> an explicitly authorized isolated database.

1. Release manifest
- Everything is driven from [release-manifest.json](./release-manifest.json) (`schemaVersion: 2`)
- `product.version` is used as the Velopack `packVersion`
- `release.channel` is used as the release channel
- `components.desktop.bundles` lists agent component IDs bundled into the desktop installer

2. Desktop packaging
- [build-desktop-avalonia.yml](./.github/workflows/build-desktop-avalonia.yml) publishes the desktop app
- [package-desktop.yml](./.github/workflows/package-desktop.yml) downloads each bundled agent artifact and runs Velopack
- `packId` is fixed to `pactoolkits`
- `packVersion` uses `product.version`
- `channel` uses `release.channel`
- Main executable: `pactoolkits-desktop.exe`

3. Asset publishing
- [publish-release.yml](./.github/workflows/publish-release.yml) uploads the release assets
- Feed payloads are synced into channel-specific subdirectories:
  - `.../stable/`
  - `.../beta/`
- Different channels are not mixed in one shared feed directory
- Electron preview artifacts are **not** included in the formal feed

4. Client update checks
- `AppUpdateService` resolves the feed to:
  - `FeedUrl/stable`
  - `FeedUrl/beta`
- The current version used for update decisions comes only from the Velopack installed version
- `version.generated.json` is no longer used to determine the current update version

5. Channel switching policy
- If the installed program channel matches the selected channel:
  - normal update checks run for that channel
- If they do not match:
  - the app does not perform automatic cross-channel switching
  - it explicitly tells the user to install the latest installer for the target channel
- This keeps database and config rollback risks out of the normal update flow

6. Database safety policy
- Application packages may be rolled back, but database schemas evolve forward by default
- A database above the target Stable `maxDbSchema` blocks switching back to Stable
- Restoring a database backup is a disaster-recovery operation, not a routine version rollback
- `isolated-beta` is limited to development and testing; it is not a production upgrade channel
- See [Beta release policy](./docs/operations/beta-release-policy.md) and
  [database compatibility policy](./docs/operations/database-compatibility-policy.md)

---

## Domain Coverage

PacToolkits currently spans these major business areas:

- drug trace-code intake
- trace-code entry and verification
- inventory overview and low-stock workflows
- drug information maintenance
- client alias management
- MSFX linkage, bill-watch recovery, and audit
- warehouse task injection, reopen, and discard handling
- runtime and agent configuration management

---

## Versioning and Compatibility

Single source of truth:

- `release-manifest.json` (schema v2)

Current manifest:

- `product.version`: `0.17.1`
- `components.desktop.version`: `0.16.1`
- `components.agent-injector-ahk.version`: `0.6.1`
- `components.database-postgres.version`: `1.2.23`
- `components.desktop.minDbSchema`: `1.2.23`
- `components.desktop.maxDbSchema`: `1.2.23`
- `components.agent-injector-ahk.minDbSchema`: `1.2.23`
- `components.agent-injector-ahk.maxDbSchema`: `1.2.23`

Common commands:

```bash
./scripts/bump-version.sh --desktop 0.12.1
./scripts/check-version.sh
./scripts/export-version.sh
```

## Getting Started

## Prerequisites

- .NET SDK 10.x
- PostgreSQL client tools such as `psql`
- shell environment with `bash`, `jq`, `zip`
- `vpk` for Velopack packaging
- `rsync` if publishing to a remote target

## Build solution

```bash
dotnet build PacToolkits.sln
```

## Build Desktop only

```bash
cd apps/desktop-avalonia/src
dotnet build -c Release
```

## Package Agent

```bash
cd pactoolkits
./scripts/release-agent-injector-ahk.sh --skip-upload --dry-run
```

## Database Deployment

```bash
cd database/postgres
cp scripts/config.example.json scripts/config.json
# edit scripts/config.json
./scripts/deploy.sh doctor
./scripts/deploy.sh plan
```

---

## Release Workflow

## Desktop Release

```bash
./scripts/release-desktop.sh \
  --runtime win-arm64 \
  --vpk-directive win \
  --upload-target user@host:/var/www/updates/pactoolkits
```

`release-desktop.sh` appends the selected channel under that root path and only supports `stable` / `beta`.

## Agent Release

```bash
./scripts/release-agent-injector-ahk.sh \
  --upload-target user@host:/var/www/updates/pactoolkits-agent/
```

---

## Design Principles

- One repository, one version source of truth
- Desktop, agent, and DB evolve together
- Business-facing flows stay observable
- Automation remains configurable, not page-hardcoded
- Database owns task state and execution truth
- Runtime, Desktop, and persistence boundaries stay explicit

---

## JetBrains Support

This project is developed with support from the **JetBrains Open Source Support Program**.

JetBrains tooling helps maintain productivity across:

- Avalonia and .NET desktop development
- PostgreSQL and SQL authoring
- repository-wide navigation and refactoring
- multi-module monorepo workflows

Thanks to JetBrains for supporting the project:

- [JetBrains Open Source Support](https://www.jetbrains.com/opensource/)

---

## License

Released under the [GNU General Public License version 3 or later](./LICENSE) (`GPL-3.0-or-later`).
This project is free software; see the license file for copying conditions.
