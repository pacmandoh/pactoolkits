# PacToolkits

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](./LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows-purple)
![Stack](https://img.shields.io/badge/stack-.NET%20%7C%20AHK%20%7C%20PostgreSQL-blue)

PacToolkits is a **drug trace-code toolkit** monorepo, including a desktop UI, an AutoHotkey automation agent, and PostgreSQL schema/deployment scripts for inventory and drug trace-code workflows.  
PacToolkits 是一个**药品追溯码工具套件**单仓库，包含桌面端 UI、AutoHotkey 自动化 Agent，以及 PostgreSQL 数据库建模与部署脚本，用于库存与药品追溯码业务流程。

## Table Of Contents

- [Overview](#overview)
- [Screenshots](#screenshots)
- [Repository Structure](#repository-structure)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Version Management](#version-management)
- [Release Workflow](#release-workflow)
- [Commit Convention](#commit-convention)
- [License](#license)

## Overview

This repository manages three core parts in one place:

- `pactoolkits-ui`: desktop business interface and online update entry.
- `pactoolkits-agent`: AutoHotkey automation runtime for trace-code operations.
- `pactoolkits-db`: migration/deploy/verify scripts for PostgreSQL schema lifecycle.

## Repository Structure

```text
pactoolkits/
  pactoolkits-ui/      # Avalonia desktop app (update client + business UI)
  pactoolkits-agent/   # AutoHotkey automation agent
  pactoolkits-db/      # PostgreSQL schema, migration, verify, deploy scripts
  scripts/             # Versioning and release scripts
  release-manifest.json
```

## Features

- UI supports online update checks (Velopack feed/channel).
- Agent integrates automation workflow and reads shared version metadata.
- DB deployment uses migration ledger + schema gate verification.
- Unified release manifest controls `suite/ui/agent/db` versions.

## Prerequisites

- macOS/Linux shell (`bash`, `jq`)
- .NET SDK 8.x (for UI build/publish)
- `vpk` (for UI update package)
- `zip` (for agent package script)
- `rsync` (optional, for upload)
- PostgreSQL client (`psql`, for DB deployment scripts)

## Quick Start

### 1) UI

```bash
cd pactoolkits-ui
dotnet build -c Release
```

### 2) Agent

```bash
cd pactoolkits-agent
# macOS mode: package source runtime bundle (no AHK compile)
../scripts/release-agent.sh --skip-upload --dry-run
```

### 3) Database

```bash
cd pactoolkits-db
cp scripts/config.example.json scripts/config.json
# edit scripts/config.json
./scripts/deploy.sh doctor
./scripts/deploy.sh plan
```

## Version Management

Single source of truth: `release-manifest.json`

- `suiteVersion`: overall release version
- `uiVersion`: desktop UI app version (Velopack update target)
- `agentVersion`: automation agent version
- `dbSchemaVersion`: database schema target version
- `compat`: minimum compatibility between UI and Agent

Common commands:

```bash
# from repo root
# bump ui -> suite auto major/minor/patch by component changes
./scripts/bump-version.sh --ui 0.4.3
# or force suite explicitly
./scripts/bump-version.sh --suite 0.4.4 --ui 0.4.3
./scripts/check-version.sh
```

## Release Workflow

### UI release (publish + vpk + optional upload)

```bash
# from repo root
./scripts/release-ui.sh \
  --runtime win-arm64 \
  --vpk-directive win \
  --upload-target user@host:/var/www/updates/pactoolkits-ui/
```

### Agent release (mac-friendly packaging, no AHK compile)

```bash
# from repo root
./scripts/release-agent.sh \
  --upload-target user@host:/var/www/updates/pactoolkits-agent/
```

## Commit Convention

- `chore(ui): scaffold pactoolkits-ui`
- `chore(agent): scaffold pactoolkits-agent`
- `chore(db): add schema and deploy scripts`
- `chore(scripts): add version and release scripts`

## License

Released under the [MIT License](./LICENSE).  
本项目基于 [MIT License](./LICENSE) 开源。
