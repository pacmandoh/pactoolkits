param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('doctor','bootstrap','upgrade','plan','status','verify','full')]
  [string]$Command,
  [string]$ConfigPath = $(Join-Path $PSScriptRoot 'config.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$DbRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$SqlRoot = Join-Path $DbRoot 'sql'
$RepoRoot = Resolve-Path (Join-Path $DbRoot '..\..')
$ManifestPath = Join-Path $RepoRoot.Path 'release-manifest.json'
$LockOwner = "{0}@{1}:{2}" -f $env:USERNAME, $PID, ([guid]::NewGuid().ToString('N'))
$LockLeaseMinutes = 60

function Require-Command([string]$name) {
  if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
    throw "[ERROR] command not found: $name"
  }
}

function Read-Json([string]$path) {
  if (!(Test-Path $path)) { throw "[ERROR] missing file: $path" }
  Get-Content $path -Raw | ConvertFrom-Json
}

function Load-DbEnv() {
  $cfg = Read-Json $ConfigPath
  $env:PGHOST = if ($cfg.PGHOST) { "$($cfg.PGHOST)" } else { '127.0.0.1' }
  $env:PGPORT = if ($cfg.PGPORT) { "$($cfg.PGPORT)" } else { '5432' }
  $env:PGDATABASE = if ($cfg.PGDATABASE) { "$($cfg.PGDATABASE)" } else { 'codepool_dev' }
  $env:PGUSER = if ($cfg.PGUSER) { "$($cfg.PGUSER)" } else { 'postgres' }
  $env:PGPASSWORD = if ($cfg.PGPASSWORD) { "$($cfg.PGPASSWORD)" } else { '' }
}

function Psql-Scalar([string]$sql) {
  $env:PGCLIENTENCODING = 'UTF8'
  $out = & psql -v ON_ERROR_STOP=1 -X -q -t -A -c $sql
  if ($LASTEXITCODE -ne 0) { throw "[ERROR] psql failed" }
  ($out | Out-String).Trim()
}

function Psql-File([string]$filePath) {
  $env:PGCLIENTENCODING = 'UTF8'
  & psql -v ON_ERROR_STOP=1 -X -f $filePath
  if ($LASTEXITCODE -ne 0) { throw "[ERROR] psql failed: $filePath" }
}

function Ensure-MetaTables() {
  Psql-File (Join-Path $SqlRoot 'bootstrap\000_init_meta.sql')
}

function Escape-SqlLiteral([string]$value) {
  if ($null -eq $value) { return '' }
  $value.Replace("'", "''")
}

function Ensure-DeployLockTable() {
  $sql = @"
create table if not exists schema_deploy_lock (
  singleton       boolean primary key default true check (singleton),
  lock_owner      text not null,
  lock_acquired_at timestamptz not null default clock_timestamp(),
  lock_expires_at  timestamptz not null
);
"@
  [void](Psql-Scalar $sql)
}

function Read-ManifestDbVersion() {
  $m = Read-Json $ManifestPath
  if ([string]::IsNullOrWhiteSpace("$($m.dbSchemaVersion)")) {
    throw "[ERROR] manifest.dbSchemaVersion is empty: $ManifestPath"
  }
  "$($m.dbSchemaVersion)"
}

function Migration-Version([string]$name) {
  $m = [regex]::Match($name, '^V([0-9_]+)__')
  if (-not $m.Success) { throw "[ERROR] invalid migration name: $name" }
  $m.Groups[1].Value.Replace('_','.')
}

function Migration-Title([string]$name) {
  $m = [regex]::Match($name, '^V[0-9_]+__(.+)\.sql$')
  if (-not $m.Success) { throw "[ERROR] invalid migration name: $name" }
  $m.Groups[1].Value
}

function Migration-SortKey([System.IO.FileInfo]$file) {
  $parts = (Migration-Version $file.Name).Split('.') | ForEach-Object { [int]$_ }
  if ($parts.Count -ne 3) { throw "[ERROR] invalid migration version: $($file.Name)" }
  "{0:D10}.{1:D10}.{2:D10}" -f $parts[0], $parts[1], $parts[2]
}

function Get-MigrationFiles() {
  Get-ChildItem (Join-Path $SqlRoot 'migrations') -Filter 'V*__*.sql' |
    Sort-Object @{ Expression = { Migration-SortKey $_ } }
}

function File-Checksum([string]$path) {
  if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
    return (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
  }
  throw '[ERROR] Get-FileHash unavailable in this PowerShell version'
}

function Is-MigrationApplied([string]$version) {
  $v = Escape-SqlLiteral $version
  (Psql-Scalar "select exists(select 1 from schema_migrations where version='${v}' and success=true)") -eq 't'
}

function Record-Migration([string]$version,[string]$name,[string]$checksum) {
  $v = Escape-SqlLiteral $version
  $n = Escape-SqlLiteral $name
  $c = Escape-SqlLiteral $checksum
  $sql = @"
insert into schema_migrations(version, name, checksum, success, note)
values ('${v}', '${n}', '${c}', true, 'applied by scripts/deploy.ps1')
on conflict (version) do update
  set name = excluded.name,
      checksum = excluded.checksum,
      success = excluded.success,
      installed_at = clock_timestamp(),
      note = excluded.note;
"@
  [void](Psql-Scalar $sql)
}

function Set-SchemaVersion([string]$version,[string]$note) {
  $v = Escape-SqlLiteral $version
  $n = Escape-SqlLiteral $note
  $sql = @"
insert into schema_version(singleton, schema_version, applied_at, note)
values (true, '${v}', clock_timestamp(), '${n}')
on conflict (singleton) do update
  set schema_version = excluded.schema_version,
      applied_at = excluded.applied_at,
      note = excluded.note;
"@
  [void](Psql-Scalar $sql)
}

function Acquire-Lock() {
  Ensure-MetaTables
  Ensure-DeployLockTable

  $owner = Escape-SqlLiteral $LockOwner
  $sql = @"
with got as (
  insert into schema_deploy_lock(singleton, lock_owner, lock_acquired_at, lock_expires_at)
  values (true, '${owner}', clock_timestamp(), clock_timestamp() + interval '${LockLeaseMinutes} minutes')
  on conflict (singleton) do update
    set lock_owner = excluded.lock_owner,
        lock_acquired_at = excluded.lock_acquired_at,
        lock_expires_at = excluded.lock_expires_at
  where schema_deploy_lock.lock_expires_at < clock_timestamp()
  returning lock_owner
)
select lock_owner from got;
"@
  $acquired = Psql-Scalar $sql
  if ([string]::IsNullOrWhiteSpace($acquired)) {
    $who = Psql-Scalar "select lock_owner from schema_deploy_lock where singleton = true"
    throw "[ERROR] another deploy process is running (owner=$who)"
  }
}

function Renew-Lock() {
  $owner = Escape-SqlLiteral $LockOwner
  [void](Psql-Scalar "update schema_deploy_lock set lock_expires_at = clock_timestamp() + interval '${LockLeaseMinutes} minutes' where singleton = true and lock_owner = '${owner}'")
}

function Release-Lock() {
  try {
    $owner = Escape-SqlLiteral $LockOwner
    [void](Psql-Scalar "delete from schema_deploy_lock where singleton = true and lock_owner = '${owner}'")
  } catch {}
}

function Run-Upgrade() {
  Ensure-MetaTables
  $files = Get-MigrationFiles
  foreach ($f in $files) {
    $version = Migration-Version $f.Name
    $title = Migration-Title $f.Name
    $checksum = File-Checksum $f.FullName

    if (Is-MigrationApplied $version) {
      $v = Escape-SqlLiteral $version
      $existing = Psql-Scalar "select checksum from schema_migrations where version='${v}'"
      if ($existing.ToLowerInvariant() -ne $checksum) {
        throw "[ERROR] migration checksum changed after applied: V${version}"
      }
      Write-Host "[db] skip applied migration: V$version ($title)"
      continue
    }

    Write-Host "[db] apply migration: V$version ($title)"
    Renew-Lock
    Psql-File $f.FullName
    Record-Migration $version $title $checksum
    Set-SchemaVersion $version "migration $title"
    Write-Host "[db] done migration: V$version"
  }
}

function Run-Verify() {
  $expected = Read-ManifestDbVersion
  Psql-File (Join-Path $SqlRoot 'verify\01_structure.sql')
  Psql-File (Join-Path $SqlRoot 'verify\02_constraints.sql')
  & psql -v ON_ERROR_STOP=1 -X -v "expected_schema_version=$expected" -f (Join-Path $SqlRoot 'verify\03_schema_version.sql')
  if ($LASTEXITCODE -ne 0) { throw '[ERROR] verify failed' }
  Write-Host "[db] verify passed (expected schema_version=$expected)"
}

Load-DbEnv

switch ($Command) {
  'doctor' {
    Require-Command psql
    [void](Read-ManifestDbVersion)
    [void](Psql-Scalar 'select 1')
    Write-Host "[db] connectivity OK"
  }
  'status' {
    Ensure-MetaTables
    $expected = Read-ManifestDbVersion
    $current = Psql-Scalar 'select schema_version from schema_version where singleton=true'
    Write-Host "manifest.dbSchemaVersion: $expected"
    Write-Host "db.schema_version:      $current"
  }
  'plan' {
    Ensure-MetaTables
    $files = Get-MigrationFiles
    foreach ($f in $files) {
      $v = Migration-Version $f.Name
      $n = Migration-Title $f.Name
      if (Is-MigrationApplied $v) { Write-Host "APPLIED  V$v  $n" } else { Write-Host "PENDING  V$v  $n" }
    }
  }
  'bootstrap' {
    Acquire-Lock
    try { Run-Upgrade } finally { Release-Lock }
  }
  'upgrade' {
    Acquire-Lock
    try { Run-Upgrade } finally { Release-Lock }
  }
  'verify' {
    Run-Verify
  }
  'full' {
    Acquire-Lock
    try { Run-Upgrade } finally { Release-Lock }
    Run-Verify
  }
}
