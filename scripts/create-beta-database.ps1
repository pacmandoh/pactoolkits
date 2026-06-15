param(
  [Parameter(Mandatory = $true)]
  [string]$Version,
  [string]$Backup,
  [string]$TemplateDatabase,
  [string]$ConfigPath,
  [string]$AdminDatabase = 'postgres',
  [switch]$NameOnly,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$MarkerSql = Join-Path $PSScriptRoot 'create-beta-database.sql'
$BetaSemVerPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-beta\.(0|[1-9][0-9]*)(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'

function Require-Command([string]$Name) {
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "[beta-db][error] command not found: $Name"
  }
}

function Get-ConfigValue([object]$Config, [string]$Name, [string]$DefaultValue) {
  $property = $Config.PSObject.Properties[$Name]
  if ($null -eq $property -or [string]::IsNullOrWhiteSpace("$($property.Value)")) {
    return $DefaultValue
  }
  "$($property.Value)"
}

function Get-EnvironmentValue([string]$Name, [string]$DefaultValue) {
  $value = [Environment]::GetEnvironmentVariable($Name)
  if ([string]::IsNullOrWhiteSpace($value)) {
    return $DefaultValue
  }
  $value
}

function Escape-SqlLiteral([string]$Value) {
  $Value.Replace("'", "''")
}

function Format-Command([string]$Name, [string[]]$Arguments) {
  $formatted = $Arguments | ForEach-Object {
    if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
  }
  "[dry-run] $Name $($formatted -join ' ')"
}

function Invoke-Native([string]$Name, [string[]]$Arguments) {
  if ($DryRun) {
    Write-Host (Format-Command $Name $Arguments)
    return
  }
  & $Name @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "[beta-db][error] command failed ($LASTEXITCODE): $Name"
  }
}

function Invoke-PsqlScalar([string]$Database, [string]$VariableValue) {
  $escapedValue = Escape-SqlLiteral $VariableValue
  $arguments = @(
    '-v', 'ON_ERROR_STOP=1',
    '-X', '-q', '-t', '-A',
    "--dbname=$Database",
    '-c', "select exists(select 1 from pg_database where datname = '$escapedValue')"
  )
  $output = & psql @arguments
  if ($LASTEXITCODE -ne 0) {
    throw '[beta-db][error] PostgreSQL database lookup failed'
  }
  ($output | Out-String).Trim()
}

function Get-TemplateConnectionCount([string]$Database, [string]$TemplateName) {
  $escapedTemplateName = Escape-SqlLiteral $TemplateName
  $arguments = @(
    '-v', 'ON_ERROR_STOP=1',
    '-X', '-q', '-t', '-A',
    "--dbname=$Database",
    '-c', "select count(*) from pg_stat_activity where datname = '$escapedTemplateName'"
  )
  $output = & psql @arguments
  if ($LASTEXITCODE -ne 0) {
    throw '[beta-db][error] PostgreSQL template connection lookup failed'
  }
  [int](($output | Out-String).Trim())
}

function Test-PlainSqlBackup([string]$Path) {
  [string]::Equals([IO.Path]::GetExtension($Path), '.sql', [StringComparison]::OrdinalIgnoreCase)
}

if ($Version -notmatch $BetaSemVerPattern) {
  throw '[beta-db][error] -Version must match strict Beta SemVer: X.Y.Z-beta.N'
}

$TargetDatabase = 'pactoolkits_beta_' + ($Version -replace '[.\-+]', '_')
if ([Text.Encoding]::UTF8.GetByteCount($TargetDatabase) -gt 63) {
  throw "[beta-db][error] generated database name exceeds PostgreSQL's 63-byte identifier limit: $TargetDatabase"
}

if ($NameOnly) {
  Write-Output $TargetDatabase
  exit 0
}

if ([string]::IsNullOrWhiteSpace($Backup) -eq [string]::IsNullOrWhiteSpace($TemplateDatabase)) {
  throw '[beta-db][error] choose exactly one source: -Backup or -TemplateDatabase'
}

if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
  if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw "[beta-db][error] config not found: $ConfigPath"
  }
  $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
  $env:PGHOST = Get-ConfigValue $config 'PGHOST' '127.0.0.1'
  $env:PGPORT = Get-ConfigValue $config 'PGPORT' '5432'
  $env:PGUSER = Get-ConfigValue $config 'PGUSER' 'postgres'
  $env:PGPASSWORD = Get-ConfigValue $config 'PGPASSWORD' ''
  $sslMode = Get-ConfigValue $config 'PGSSLMODE' ''
  if (-not [string]::IsNullOrWhiteSpace($sslMode)) {
    $env:PGSSLMODE = $sslMode
  }
}

$env:PGHOST = Get-EnvironmentValue 'PGHOST' '127.0.0.1'
$env:PGPORT = Get-EnvironmentValue 'PGPORT' '5432'
$env:PGUSER = Get-EnvironmentValue 'PGUSER' 'postgres'

if (-not (Test-Path -LiteralPath $MarkerSql -PathType Leaf)) {
  throw "[beta-db][error] marker SQL not found: $MarkerSql"
}
if (-not [string]::IsNullOrWhiteSpace($Backup) -and
    -not (Test-Path -LiteralPath $Backup -PathType Leaf)) {
  throw "[beta-db][error] backup not found: $Backup"
}

Write-Host "[beta-db] target: $($env:PGUSER)@$($env:PGHOST):$($env:PGPORT)/$TargetDatabase"
if (-not [string]::IsNullOrWhiteSpace($TemplateDatabase)) {
  Write-Host "[beta-db] source: template database $TemplateDatabase"
} else {
  Write-Host "[beta-db] source: backup $Backup"
}

if (-not $DryRun) {
  Require-Command psql
  Require-Command createdb
  if (-not [string]::IsNullOrWhiteSpace($Backup) -and
      -not (Test-PlainSqlBackup $Backup)) {
    Require-Command pg_restore
  }

  $exists = Invoke-PsqlScalar $AdminDatabase $TargetDatabase
  if ($exists -ne 'f') {
    throw "[beta-db][error] database already exists; refusing to overwrite: $TargetDatabase"
  }
}

if (-not [string]::IsNullOrWhiteSpace($TemplateDatabase)) {
  if (-not $DryRun) {
    $templateExists = Invoke-PsqlScalar $AdminDatabase $TemplateDatabase
    if ($templateExists -ne 't') {
      throw "[beta-db][error] template database not found: $TemplateDatabase"
    }
    $activeConnections = Get-TemplateConnectionCount $AdminDatabase $TemplateDatabase
    if ($activeConnections -ne 0) {
      throw "[beta-db][error] template database has active connections ($activeConnections); disconnect them before cloning: $TemplateDatabase"
    }
  }
  Invoke-Native createdb @("--maintenance-db=$AdminDatabase", "--template=$TemplateDatabase", $TargetDatabase)
} else {
  Invoke-Native createdb @("--maintenance-db=$AdminDatabase", $TargetDatabase)
  if (Test-PlainSqlBackup $Backup) {
    Invoke-Native psql @('-v', 'ON_ERROR_STOP=1', '-X', "--dbname=$TargetDatabase", '-f', $Backup)
  } else {
    Invoke-Native pg_restore @(
      '--exit-on-error',
      '--no-owner',
      '--no-privileges',
      "--dbname=$TargetDatabase",
      $Backup
    )
  }
}

Invoke-Native psql @(
  '-v', 'ON_ERROR_STOP=1',
  '-X',
  "--dbname=$TargetDatabase",
  '-v', "beta_version=$Version",
  '-v', 'database_source=production-clone',
  '-f', $MarkerSql
)

if (-not $DryRun) {
  Write-Host "[beta-db] created isolated Beta database: $TargetDatabase"
  Write-Host "[beta-db] connection string: Host=$($env:PGHOST);Port=$($env:PGPORT);Database=$TargetDatabase;Username=$($env:PGUSER)"
  $password = [Environment]::GetEnvironmentVariable('PGPASSWORD')
  if (-not [string]::IsNullOrWhiteSpace($password)) {
    Write-Host '[beta-db] password omitted from output; reuse the configured PGPASSWORD securely'
  }
}
