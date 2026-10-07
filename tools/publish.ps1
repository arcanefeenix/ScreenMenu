<#
.SYNOPSIS
  Builds the self-contained release folder (no .NET install needed on the target PC), adds the operator
  documents and sample files, zips it, and runs a clean-start smoke test on the published copy.

.USAGE
  powershell -ExecutionPolicy Bypass -File tools\publish.ps1

  Output:  dist\LedMenuControl\            the folder to copy to the event PC (run LedMenu.App.exe)
           dist\LedMenuControl-<version>.zip   the same folder, zipped
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$project = 'src\LedMenu.App\LedMenu.App.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist    = Join-Path $repo 'dist'
$out     = Join-Path $dist 'LedMenuControl'
$zip     = Join-Path $dist "LedMenuControl-$version.zip"

Write-Host "== Publishing LED Menu Control $version (self-contained, win-x64) =="
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Write-Host "== Adding documents and samples =="
Copy-Item 'docs\OPERATOR_GUIDE.html' $out
Copy-Item 'docs\READ_ME_FIRST.txt'   $out
if (Test-Path 'samples') { Copy-Item 'samples' (Join-Path $out 'samples') -Recurse }

# must-have files: refuse to produce a package that cannot work
$required = 'LedMenu.App.exe', 'Fonts\Lato-Regular.ttf', 'Fonts\Lato-Bold.ttf', 'OPERATOR_GUIDE.html', 'READ_ME_FIRST.txt'
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $out $r))) { throw "Package is missing $r" }
}
if (-not (Get-ChildItem $out -Filter 'hostfxr.dll' -ErrorAction SilentlyContinue) -and
    -not (Get-ChildItem $out -Filter 'coreclr.dll' -ErrorAction SilentlyContinue)) {
    throw 'Package does not contain the .NET runtime (not self-contained?)'
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $out -DestinationPath $zip
$sizeMb = [math]::Round((Get-ChildItem $out -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ("Folder: {0}  ({1} MB, {2} files)" -f $out, $sizeMb, (Get-ChildItem $out -Recurse -File).Count)
Write-Host ("Zip:    {0}  ({1} MB)" -f $zip, [math]::Round((Get-Item $zip).Length / 1MB, 1))

if ($SkipSmokeTest) { return }

Write-Host "== Clean-start smoke test of the published copy =="
$work = Join-Path ([IO.Path]::GetTempPath()) ('ledmenu-smoke-' + [guid]::NewGuid().ToString('N'))
$run  = Join-Path $work 'program folder with spaces'        # a different place, with spaces, like a desktop copy
$data = Join-Path $work 'data'
New-Item -ItemType Directory -Force $run | Out-Null
Copy-Item "$out\*" $run -Recurse

# Make the machine look like it has no .NET installed: no dotnet on PATH, no DOTNET_ROOT.
$savedPath = $env:PATH; $savedRoot = $env:DOTNET_ROOT; $savedRoot32 = ${env:DOTNET_ROOT(x86)}
$env:PATH = (($env:PATH -split ';') | Where-Object { $_ -and $_ -notmatch 'dotnet' }) -join ';'
$env:DOTNET_ROOT = $null; ${env:DOTNET_ROOT(x86)} = $null
$env:LEDMENU_DATA_DIR = $data
$ok = $true
try {
    $p = Start-Process (Join-Path $run 'LedMenu.App.exe') -PassThru
    Start-Sleep -Seconds 8
    $p.Refresh()
    $alive = -not $p.HasExited
    $responding = $alive -and $p.Responding
    $log = Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
    $text = if ($log) { Get-Content $log.FullName -Raw } else { '' }
    $fonts = $text -match 'families: Lato'
    $started = $text -match 'Application startup'
    $problems = ($text -split "`n") | Where-Object { $_ -match '\[(ERROR|WARN)\]' }

    "{0,-34} {1}" -f 'Process still running after 8 s:', $alive
    "{0,-34} {1}" -f 'Window responding:', $responding
    "{0,-34} {1}" -f 'Startup logged:', $started
    "{0,-34} {1}" -f 'Bundled Lato font found:', $fonts
    $dataMade = (Test-Path (Join-Path $data 'logs'))
    "{0,-34} {1}" -f 'Data created in override folder:', $dataMade
    "{0,-34} {1}" -f 'Errors/warnings in log:', @($problems).Count
    $problems | ForEach-Object { "    $_" }
    $ok = $alive -and $responding -and $started -and $fonts -and $dataMade -and (@($problems).Count -eq 0)
    if ($alive) { Stop-Process -Id $p.Id -Force }
}
finally {
    $env:PATH = $savedPath; $env:DOTNET_ROOT = $savedRoot; ${env:DOTNET_ROOT(x86)} = $savedRoot32
    $env:LEDMENU_DATA_DIR = $null
    Start-Sleep -Seconds 1
    try { [IO.Directory]::Delete($work, $true) } catch { Write-Host "Could not remove $work : $_" }
}
if ($ok) { Write-Host 'SMOKE TEST PASSED' -ForegroundColor Green } else { Write-Host 'SMOKE TEST FAILED' -ForegroundColor Red; exit 1 }
