# 将 PacToolkits Stable 与 Beta Feed 同步到本地目录

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$FeedRoot = 'https://updates.pacdocs.com/feed/pactoolkits'
$FeedDestRoot = 'F:\PacDocs\feed\pactoolkits'
$SyncRoot = 'F:\PacDocs\sync'
$LogPath = Join-Path $SyncRoot 'logs\sync_uu.log'
$Channels = @('stable', 'beta')

New-Item -Force -ItemType Directory $FeedDestRoot, (Split-Path $LogPath -Parent) | Out-Null

function Write-SyncLog([string]$Message) {
    Add-Content -LiteralPath $LogPath -Value ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message)
}

function Get-RemoteTag([string]$ManifestUrl) {
    $head = Invoke-WebRequest -Method Head -UseBasicParsing -Uri $ManifestUrl -TimeoutSec 15
    $tag = $head.Headers['ETag']
    if (-not $tag) { $tag = $head.Headers['Last-Modified'] }
    if (-not $tag) { $tag = $head.Headers['Content-Length'] }
    if (-not $tag) { $tag = 'no-tag' }
    "$tag"
}

function Receive-SyncFile([string]$Url, [string]$Destination, [int]$TimeoutSeconds) {
    $temporaryPath = "$Destination.tmp"
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue

    $usedBits = $false
    if (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue) {
        try {
            Start-BitsTransfer -Source $Url -Destination $temporaryPath -TransferType Download -ErrorAction Stop | Out-Null
            $usedBits = $true
        } catch {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not $usedBits) {
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri $Url `
            -OutFile $temporaryPath `
            -TimeoutSec $TimeoutSeconds `
            -ErrorAction Stop
    }

    Move-Item -LiteralPath $temporaryPath -Destination $Destination -Force
}

function Sync-Channel([string]$Channel) {
    $baseUrl = "$FeedRoot/$Channel"
    $destination = Join-Path $FeedDestRoot $Channel
    $manifestName = "releases.$Channel.json"
    $releasesName = "RELEASES-$Channel"
    $assetsName = "assets.$Channel.json"
    $setupName = "pactoolkits-$Channel-Setup.exe"
    $tagName = "tag_manifest.$Channel.txt"
    if ($Channel -eq 'stable') {
        $tagName = 'tag_manifest.txt'
    }
    $tagPath = Join-Path $SyncRoot $tagName
    $manifestUrl = "$baseUrl/$manifestName"

    New-Item -Force -ItemType Directory $destination | Out-Null

    try {
        $remoteTag = Get-RemoteTag $manifestUrl
        if (Test-Path -LiteralPath $tagPath) {
            $savedTag = Get-Content -LiteralPath $tagPath | Select-Object -First 1
            if ($savedTag -eq $remoteTag) {
                Write-SyncLog "$Channel skip: no change ($remoteTag)"
                return $true
            }
        } else {
            Write-SyncLog "$Channel first run ($remoteTag)"
        }

        $manifestJson = (Invoke-WebRequest -UseBasicParsing -Uri $manifestUrl -TimeoutSec 30).Content
        $packagePattern = '[A-Za-z0-9\.\-_]+\.nupkg(?:\.(?:full|delta))?'
        $packages = @(
            [regex]::Matches($manifestJson, $packagePattern) |
                ForEach-Object { $_.Value } |
                Sort-Object -Unique
        )
        if ($packages.Count -eq 0) {
            Write-SyncLog "$Channel error: manifest contains no packages"
            return $false
        }

        foreach ($package in $packages) {
            try {
                Invoke-WebRequest -Method Head -UseBasicParsing -Uri "$baseUrl/$package" -TimeoutSec 15 | Out-Null
            } catch {
                Write-SyncLog "$Channel skip: package not ready $package"
                return $true
            }
        }

        $payload = @($setupName, $assetsName) + $packages | Sort-Object -Unique
        Write-SyncLog ("$Channel ready: downloading {0} files" -f $payload.Count)

        $jobs = foreach ($name in $payload) {
            $url = "$baseUrl/$name"
            $output = Join-Path $destination $name
            Start-Job -ArgumentList $url, $output -ScriptBlock {
                param($Url, $Output)

                $temporaryPath = "$Output.tmp"
                try {
                    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
                    $usedBits = $false
                    if (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue) {
                        try {
                            Start-BitsTransfer -Source $Url -Destination $temporaryPath -TransferType Download -ErrorAction Stop | Out-Null
                            $usedBits = $true
                        } catch {
                            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
                        }
                    }
                    if (-not $usedBits) {
                        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $temporaryPath -TimeoutSec 300 -ErrorAction Stop
                    }
                    Move-Item -LiteralPath $temporaryPath -Destination $Output -Force
                    "OK $Output"
                } catch {
                    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
                    "ERR $Url :: $($_.Exception.Message)"
                }
            }
        }

        $results = Receive-Job -Job $jobs -Wait -AutoRemoveJob
        $errors = @($results | Where-Object { $_ -like 'ERR *' })
        if ($errors.Count -gt 0) {
            $errors | ForEach-Object { Write-SyncLog "$Channel $_" }
            return $false
        }

        foreach ($pointerName in @($manifestName, $releasesName)) {
            Receive-SyncFile `
                -Url "$baseUrl/$pointerName" `
                -Destination (Join-Path $destination $pointerName) `
                -TimeoutSeconds 120
            Write-SyncLog "$Channel pointer ready: $pointerName"
        }

        Get-ChildItem -LiteralPath $destination -Filter '*.nupkg*' -File |
            Sort-Object LastWriteTime -Descending |
            Select-Object -Skip 6 |
            Remove-Item -Force -ErrorAction SilentlyContinue

        Set-Content -LiteralPath $tagPath -Value $remoteTag -Encoding ascii
        Write-SyncLog "$Channel done ($remoteTag)"
        return $true
    } catch {
        Write-SyncLog "$Channel error: $($_.Exception.Message)"
        return $false
    }
}

$mutex = New-Object Threading.Mutex($false, 'Global\PacDocsSyncUU')
$lockTaken = $false
try {
    $lockTaken = $mutex.WaitOne(0)
    if (-not $lockTaken) {
        Write-SyncLog 'Skip: another sync is running'
        exit 0
    }

    $failedChannels = @()
    foreach ($channel in $Channels) {
        if (-not (Sync-Channel $channel)) {
            $failedChannels += $channel
        }
    }

    if ($failedChannels.Count -gt 0) {
        Write-SyncLog ("Sync failed: {0}" -f ($failedChannels -join ', '))
        exit 20
    }

    Write-SyncLog 'All channels synchronized'
} finally {
    if ($lockTaken) {
        $mutex.ReleaseMutex() | Out-Null
    }
    $mutex.Dispose()
}
