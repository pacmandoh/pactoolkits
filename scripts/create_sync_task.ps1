param(
    [string]$TaskName = "pactoolkits-sync",
    [string]$ScriptPath = "F:\PacDocs\sync\sync_pactoolkits_uu.ps1",
    [int]$RepeatMinutes = 3
)

$ErrorActionPreference = "Stop"

# -----------------------------
# Auto elevate to Administrator
# -----------------------------
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Re-launching as Administrator..."
    $argList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-TaskName", "`"$TaskName`"",
        "-ScriptPath", "`"$ScriptPath`""
    ) -join ' '

    Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -ArgumentList $argList
    exit
}

Write-Host "==== PacDocs Sync Task Setup ===="

if (!(Test-Path $ScriptPath)) {
    throw "Script not found: $ScriptPath"
}

$psExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$workingDir = Split-Path $ScriptPath -Parent
if ($RepeatMinutes -lt 1) {
    throw "RepeatMinutes must be >= 1"
}
$userId = "$env:USERDOMAIN\$env:USERNAME"

# -----------------------------
# Remove existing task if exists
# -----------------------------
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing task found. Removing..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# -----------------------------
# Build action
# -----------------------------
$action = New-ScheduledTaskAction `
    -Execute $psExe `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ScriptPath`"" `
    -WorkingDirectory $workingDir

# -----------------------------
# Trigger 1: every N minutes after first registration
# -----------------------------
$trigger1 = New-ScheduledTaskTrigger `
    -Once `
    -At (Get-Date) `
    -RepetitionInterval (New-TimeSpan -Minutes $RepeatMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)

# -----------------------------
# Trigger 2: at logon
# -----------------------------
$trigger2 = New-ScheduledTaskTrigger -AtLogOn -User $userId

# -----------------------------
# Settings
# -----------------------------
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -Hidden

# -----------------------------
# Run as current user, highest
# InteractiveToken keeps the task in the logged-on user session,
# which avoids S4U startup runs losing network credentials.
# -----------------------------
$taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId $userId `
    -RunLevel Highest `
    -LogonType InteractiveToken

# -----------------------------
# Register task
# -----------------------------
Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger @($trigger1, $trigger2) `
    -Settings $settings `
    -Principal $taskPrincipal `
    -Force | Out-Null

Write-Host ""
Write-Host "Task created successfully."
Write-Host ""

Get-ScheduledTask -TaskName $TaskName | Format-List TaskName,State
Get-ScheduledTaskInfo -TaskName $TaskName | Format-List LastRunTime,NextRunTime
