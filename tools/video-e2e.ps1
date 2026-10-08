<#
.SYNOPSIS
  End-to-end check of video in the real output window, without needing to photograph the screen.

.USAGE
  powershell -ExecutionPolicy Bypass -File tools\video-e2e.ps1 -Clips 'a.mp4','b.mp4' [-Seconds 20]

  It seeds an isolated data folder (a 168x672 menu screen and a 168x672 video screen playing the clips), starts the real
  program against it with the second display as LED output, lets it run the video self-test (the program compares what its
  output window really holds with the picture it believes it is showing, twice a second), prints the report and cleans up.
  The LED display flashes the test picture for the duration. The program is the Release build.
#>
param(
    [Parameter(Mandatory)] [string[]]$Clips,
    [int]$Seconds = 20,
    [string]$OutputDeviceName = ''        # optional: GDI name of the display to use as LED output, such as \\.\DISPLAY2
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

dotnet build LedMenu.sln -c Release -warnaserror | Out-Null
dotnet build tools\SeedVideoData -c Release | Out-Null
$seed = Get-ChildItem tools\SeedVideoData\bin\Release -Recurse -Filter SeedVideoData.dll | Select-Object -First 1
$app = Get-ChildItem src\LedMenu.App\bin\Release -Recurse -Filter LedMenu.App.exe | Select-Object -First 1

$data = Join-Path ([IO.Path]::GetTempPath()) ('ledmenu-e2e-' + [guid]::NewGuid().ToString('N'))
$report = Join-Path $data 'report.txt'
try {
    dotnet $seed.FullName $data @Clips
    if ($LASTEXITCODE -ne 0) { throw 'seeding failed' }

    # which display is the LED output: any display that is not the primary one
    Add-Type -AssemblyName System.Windows.Forms
    $screens = [System.Windows.Forms.Screen]::AllScreens
    $target = if ($OutputDeviceName) { $screens | Where-Object { $_.DeviceName -eq $OutputDeviceName } } else { $screens | Where-Object { -not $_.Primary } | Select-Object -First 1 }
    if (-not $target) { throw 'No second display found to use as the LED output.' }
    $primary = $screens | Where-Object { $_.Primary } | Select-Object -First 1

    # the program identifies displays by device path; read them with the program's own listing in its log after one quick start
    $env:LEDMENU_DATA_DIR = $data
    $probe = Start-Process $app.FullName -PassThru
    Start-Sleep 5
    Stop-Process -Id $probe.Id -Force
    Start-Sleep 1
    $log = Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' | Select-Object -First 1
    $lines = Get-Content $log.FullName | Where-Object { $_ -match 'Display \d+:' }
    function IdentityFor($gdiName) {
        $line = $lines | Where-Object { $_ -match [regex]::Escape("[$gdiName]") } | Select-Object -First 1
        if (-not $line) { throw "display $gdiName not found in the program's own display list" }
        $path = ($line -split 'id=', 2)[1].Trim()
        $m = [regex]::Match($line, 'Display \d+: (.*?) (\d+)\u00D7(\d+) at \((-?\d+),(-?\d+)\)')
        @{ DevicePath = $path; FriendlyName = $m.Groups[1].Value; DeviceName = $gdiName; X = [int]$m.Groups[4].Value; Y = [int]$m.Groups[5].Value; Width = [int]$m.Groups[2].Value; Height = [int]$m.Groups[3].Value }
    }
    $settings = @{ SchemaVersion = 1; OperatorDisplay = (IdentityFor $primary.DeviceName); OutputDisplay = (IdentityFor $target.DeviceName) }
    $settings | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $data 'settings.json') -Encoding utf8

    Write-Host "LED output: $($target.DeviceName) $($target.Bounds.Width)x$($target.Bounds.Height); operator: $($primary.DeviceName). Running $Seconds s..."
    $p = Start-Process $app.FullName -ArgumentList '--selftest-video', "`"$report`"" -PassThru
    if (-not $p.WaitForExit(($Seconds + 60) * 1000)) { Stop-Process -Id $p.Id -Force; throw 'the self-test did not finish' }
    if (Test-Path $report) { Get-Content $report } else { Write-Host 'No report was written.' }
    Write-Host '--- warnings and errors in the program log:'
    Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' | ForEach-Object { Get-Content $_.FullName } | Where-Object { $_ -match '\[(WARN|ERROR)\]' } | Select-Object -First 15
}
finally {
    $env:LEDMENU_DATA_DIR = $null
    Start-Sleep 1
    try { [IO.Directory]::Delete($data, $true) } catch { Write-Host "Could not remove $data : $_" }
}
