$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
Set-Location $root
$log = Join-Path $root 'build_installers.log'
function W([string]$m) { Add-Content -Path $log -Value ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $m) -Encoding UTF8 }

Remove-Item $log -ErrorAction SilentlyContinue
W "=== kill stale build processes ==="
Get-Process dotnet, MSBuild, VBCSCompiler -ErrorAction SilentlyContinue |
    Where-Object { $_.Id -ne $PID } | ForEach-Object {
        try { W ("killing " + $_.Name + " PID=" + $_.Id); Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
Start-Sleep -Seconds 2

$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { $iscc = 'C:\Program Files\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { W "ISCC NOT FOUND - aborting"; exit 1 }
W ("ISCC = " + $iscc)

$ver = '1.1.0'
$targets = @(
    @{ Arch = 'x64';   Rid = 'win-x64';   Plat = 'x64' },
    @{ Arch = 'arm64'; Rid = 'win-arm64'; Plat = 'ARM64' }
)

foreach ($t in $targets) {
    $outDir = Join-Path $root ("publish\" + $t.Arch)
    W ("=== publish " + $t.Arch + " start ===")
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue }

    $platArg = '-p:Platform=' + $t.Plat
    $pubArg  = '-p:PublishReadyToRun=false'
    $outArg  = '-o'
    & dotnet publish (Join-Path $root 'FluentScrobbler.csproj') -c Release $platArg -r $t.Rid --self-contained true $pubArg $outArg $outDir --disable-build-servers -v:m --nologo 2>&1 |
        ForEach-Object { Add-Content -Path $log -Value ("  " + $_) -Encoding UTF8 }
    W ("publish " + $t.Arch + " exitcode=" + $LASTEXITCODE)

    if (-not (Test-Path (Join-Path $outDir 'FluentScrobbler.exe'))) {
        W ("publish " + $t.Arch + " FAILED - FluentScrobbler.exe missing, skip installer")
        continue
    }

    W ("=== ISCC " + $t.Arch + " start ===")
    & $iscc ("/DMyAppVersion=" + $ver) ("/DAppArch=" + $t.Arch) ("/DSourceDir=publish\" + $t.Arch) (Join-Path $root 'installer.iss') 2>&1 |
        ForEach-Object { Add-Content -Path $log -Value ("  " + $_) -Encoding UTF8 }
    W ("ISCC " + $t.Arch + " exitcode=" + $LASTEXITCODE)
}

W "=== done ==="
Get-ChildItem (Join-Path $root 'Output') -Filter '*.exe' -ErrorAction SilentlyContinue |
    ForEach-Object { W ("OUTPUT: " + $_.Name + "  " + $_.Length + " bytes") }
