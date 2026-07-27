param(
    [string]$TaskName = "pactoolkits-sync",
    [string]$ScriptPath = "F:\PacDocs\sync\sync_pactoolkits_uu.ps1",
    [int]$RepeatMinutes = 3
)

$ErrorActionPreference = "Stop"

# 计划任务注册需要管理员权限，非管理员会话必须重新提升
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
        "-ScriptPath", "`"$ScriptPath`"",
        "-RepeatMinutes", $RepeatMinutes
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
$userId = $currentIdentity.Name
$startAt = (Get-Date).AddMinutes(1)
$plainPassword = $null

$cred = Get-Credential -UserName $userId -Message "请输入用于计划任务后台运行的 Windows 账户密码"
if (-not $cred) {
    throw "Credential input was cancelled."
}
$userId = $cred.UserName
$plainPassword = $cred.GetNetworkCredential().Password
if ([string]::IsNullOrWhiteSpace($plainPassword)) {
    throw "Password cannot be empty for Password logon type."
}

# 注册前移除同名任务，避免保留旧触发器和凭据设置
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing task found. Removing..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

try {
    $action = New-ScheduledTaskAction `
        -Execute $psExe `
        -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ScriptPath`"" `
        -WorkingDirectory $workingDir
} catch {
    Write-Warning "New-ScheduledTaskAction -WorkingDirectory is not supported on this machine. Falling back without WorkingDirectory."
    $action = New-ScheduledTaskAction `
        -Execute $psExe `
        -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ScriptPath`""
}

# 周期触发器从注册时开始，按配置的分钟间隔重复执行
$trigger1 = New-ScheduledTaskTrigger `
    -Once `
    -At $startAt `
    -RepetitionInterval (New-TimeSpan -Minutes $RepeatMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)

# 登录触发器用于在用户会话开始后立即同步一次
try {
    $trigger2 = New-ScheduledTaskTrigger -AtLogOn -User $userId
} catch {
    Write-Warning "Per-user logon trigger is not supported on this machine. Falling back to a generic logon trigger."
    $trigger2 = New-ScheduledTaskTrigger -AtLogOn
}

# 允许计划任务错过触发时间后补执行，并限制并发实例
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -Hidden

# 使用当前用户和最高权限运行；Password 登录类型可在后台保留网络访问能力
$taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId $userId `
    -RunLevel Highest `
    -LogonType Password

$task = New-ScheduledTask `
    -Action $action `
    -Trigger @($trigger1, $trigger2) `
    -Settings $settings `
    -Principal $taskPrincipal

try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -InputObject $task `
        -User $userId `
        -Password $plainPassword `
        -Force | Out-Null
} finally {
    $plainPassword = $null
}

try {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Initial run started."
} catch {
    Write-Warning ("Task created, but the initial start request failed: " + $_.Exception.Message)
}

Write-Host ""
Write-Host "Task created successfully."
Write-Host ""

Get-ScheduledTask -TaskName $TaskName | Format-List TaskName,State
Get-ScheduledTaskInfo -TaskName $TaskName | Format-List LastRunTime,NextRunTime
