# `sync_pactoolkits_uu.ps1` by pacmandoh, 2026-03-05
# 从 updates.pacdocs.com 同步 pactoolkits feed 到本地目录（变更检测 + 并行下载）

$Base="https://updates.pacdocs.com/feed/pactoolkits/stable"; $Dest="F:\PacDocs\feed\pactoolkits\stable"; $Sync="F:\PacDocs\sync"
$Man="releases.stable.json"; $Rel="RELEASES-stable"; $Ast="assets.stable.json"; $Setup="pactoolkits-stable-Setup.exe"
$Tag="$Sync\tag_manifest.txt"; $Log="$Sync\logs\sync_uu.log"; New-Item -Force -ItemType Directory $Dest,("$Sync\logs")|Out-Null

function L($m){Add-Content -LiteralPath $Log -Value ("[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"),$m)}
$mx=New-Object Threading.Mutex($false,"Global\PacDocsSyncUU"); $lockTaken=$false; if(-not ($lockTaken=$mx.WaitOne(0))){L "Skip: running"; exit 0}

try{
  $mu="$Base/$Man"; $h=Invoke-WebRequest -Method Head -UseBasicParsing -Uri $mu -TimeoutSec 15
  $t=$h.Headers.ETag; if(!$t){$t=$h.Headers.'Last-Modified'}; if(!$t){$t=$h.Headers.'Content-Length'}; if(!$t){$t="no-tag"}
  if(Test-Path $Tag){if((Get-Content $Tag|Select -First 1) -eq $t){L "Skip: no change ($t)"; exit 0}} else {L "First run ($t)"}

  $j=(Invoke-WebRequest -UseBasicParsing -Uri $mu -TimeoutSec 30).Content
  $rx='[A-Za-z0-9\.\-_]+\.nupkg(?:\.(?:full|delta))?'; $arts=[regex]::Matches($j,$rx)|%{$_.Value}|sort -Unique
  if(!$arts -or $arts.Count -eq 0){L "Skip: no artifacts"; exit 0}

  foreach($f in $arts){try{Invoke-WebRequest -Method Head -UseBasicParsing -Uri "$Base/$f" -TimeoutSec 15|Out-Null}catch{L "Skip: not ready $f"; exit 0}}
  $payload=@($Setup,$Ast)+$arts | sort -Unique
  L ("Ready. Download {0} files..." -f $payload.Count)

  $jobs=@()
  foreach($n in $payload){
    $u="$Base/$n"; $o=Join-Path $Dest $n; $tmp="$o.tmp"
    $jobs += Start-Job -ArgumentList $u,$tmp,$o -ScriptBlock {
      param($u,$tmp,$o)
      try{
        $usedBits = $false
        if(Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue){
          try{
            Start-BitsTransfer -Source $u -Destination $tmp -TransferType Download -ErrorAction Stop
            $usedBits = $true
          } catch {
            if(Test-Path $tmp){ Remove-Item -Force $tmp -ErrorAction SilentlyContinue }
          }
        }
        if(-not $usedBits){
          Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $tmp -TimeoutSec 300 -ErrorAction Stop
        }
        Move-Item -Force $tmp $o
        "OK $o"
      } catch {
        "ERR $u :: $($_.Exception.Message)"
      }
    }
  }
  $res=Receive-Job -Job $jobs -Wait -AutoRemoveJob
  $err=$res | ?{$_ -like "ERR *"}; if($err){$err | % { L $_ }; L "ERROR: download failed"; exit 20}

  foreach($n in @($Man,$Rel)){
    $u="$Base/$n"; $o=Join-Path $Dest $n; $tmp="$o.tmp"
    try{ Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $tmp -TimeoutSec 120 -ErrorAction Stop; Move-Item -Force $tmp $o; L "Ptr OK $n" }
    catch{ L "ERROR: pointer failed $n :: $($_.Exception.Message)"; exit 21 }
  }

  # 可选清理：只保留最新 6 个 nupkg*
  Get-ChildItem $Dest -Filter "*.nupkg*" -File | Sort-Object LastWriteTime -Descending | Select-Object -Skip 6 | Remove-Item -Force -ErrorAction SilentlyContinue

  Set-Content -LiteralPath $Tag -Value $t -Encoding ascii; L "OK: done ($t)"
} finally { if($lockTaken){ $mx.ReleaseMutex()|Out-Null } }
