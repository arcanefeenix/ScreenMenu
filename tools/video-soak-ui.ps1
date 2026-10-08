<#
.SYNOPSIS
  Long soak of the whole video pipeline in the real program, without using any display: starts the program on a seeded data folder
  (a menu screen and a video screen looping the clips), presses "Preview videos", then samples the process every 30 seconds
  (CPU, memory, handles, threads) and reads the status line, for the given number of minutes. Reports trends and any errors.

.USAGE
  & .\tools\video-soak-ui.ps1 -Minutes 30 -Clips $paths
#>
param(
    [Parameter(Mandatory)] [string[]]$Clips,
    [int]$Minutes = 30,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]

dotnet build LedMenu.sln -c Release -warnaserror | Out-Null
dotnet build tools\SeedVideoData -c Release | Out-Null
$seed = Get-ChildItem tools\SeedVideoData\bin\Release -Recurse -Filter SeedVideoData.dll | Select-Object -First 1
$app = Get-ChildItem src\LedMenu.App\bin\Release -Recurse -Filter LedMenu.App.exe | Select-Object -First 1
$data = Join-Path ([IO.Path]::GetTempPath()) ('ledmenu-soak-' + [guid]::NewGuid().ToString('N'))
$lines = New-Object System.Collections.Generic.List[string]
function Say($t) { $lines.Add($t); Write-Host $t }
function ById($root, $id) { $root.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id))) }
function Texts($root) { $root.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name } }

$p = $null
try {
    dotnet $seed.FullName $data @Clips | Out-Null
    $env:LEDMENU_DATA_DIR = $data
    $p = Start-Process $app.FullName -PassThru
    Start-Sleep 6
    $win = $AE::FromHandle($p.MainWindowHandle)
    $tab = $win.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, 'Screens')),
        (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)))))
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep 1
    $btn = ById $win 'PreviewVideos'
    $btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep 5

    Say "Soak started $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'): $Minutes minutes, process $($p.Id), 3 clips looping (about 57 s per round)."
    Say 'minute | CPU % (of one core, last 30 s) | working set MB | private MB | handles | threads | status'
    $started = Get-Date
    $samples = @()
    $last = $p.TotalProcessorTime
    $lastAt = Get-Date
    $notPlaying = 0
    while (((Get-Date) - $started).TotalMinutes -lt $Minutes) {
        Start-Sleep 30
        $p.Refresh()
        if ($p.HasExited) { Say 'THE PROGRAM EXITED during the soak.'; break }
        $now = Get-Date
        $cpu = ($p.TotalProcessorTime - $last).TotalSeconds / ($now - $lastAt).TotalSeconds * 100
        $last = $p.TotalProcessorTime; $lastAt = $now
        $status = (Texts $win | Where-Object { $_ -like 'Playing *' -or $_ -like 'Paused *' -or $_ -like 'Not playing*' -or $_ -like 'Finished*' } | Select-Object -First 1)
        if ($status -notlike 'Playing *') { $notPlaying++ }
        $row = [pscustomobject]@{ Min = [math]::Round(($now - $started).TotalMinutes, 1); Cpu = [math]::Round($cpu, 1); Ws = [int]($p.WorkingSet64 / 1MB); Priv = [int]($p.PrivateMemorySize64 / 1MB); Handles = $p.HandleCount; Threads = $p.Threads.Count }
        $samples += $row
        Say ('{0,6} | {1,6} | {2,6} | {3,6} | {4,6} | {5,5} | {6}' -f $row.Min, $row.Cpu, $row.Ws, $row.Priv, $row.Handles, $row.Threads, $status)
    }

    Say ''
    if ($samples.Count -ge 6) {
        $first = $samples[2..5]; $lastFew = $samples[-4..-1]
        $avg = { param($set, $prop) ($set | Measure-Object -Property $prop -Average).Average }
        Say ('Working set: {0:0} MB early (minutes 1-3) -> {1:0} MB at the end; private memory {2:0} -> {3:0} MB; handles {4:0} -> {5:0}; threads {6:0} -> {7:0}' -f (& $avg $first 'Ws'), (& $avg $lastFew 'Ws'), (& $avg $first 'Priv'), (& $avg $lastFew 'Priv'), (& $avg $first 'Handles'), (& $avg $lastFew 'Handles'), (& $avg $first 'Threads'), (& $avg $lastFew 'Threads'))
        Say ('CPU: average {0:0.0}% of one core, highest sample {1:0.0}%' -f (& $avg $samples 'Cpu'), ($samples | Measure-Object -Property Cpu -Maximum).Maximum)
        Say ('Peak working set {0} MB' -f ($samples | Measure-Object -Property Ws -Maximum).Maximum)
    }
    Say "Samples where the status line did not say Playing: $notPlaying of $($samples.Count)"
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    $env:LEDMENU_DATA_DIR = $null
    Start-Sleep 1
    try {
        $log = Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName }
        $bad = @($log | Where-Object { $_ -match '\[(ERROR)\]|Unhandled' })
        $warn = @($log | Where-Object { $_ -match '\[WARN\]' })
        Say "Log: $($bad.Count) error line(s), $($warn.Count) warning line(s); log size $($log.Count) lines"
        $bad | Select-Object -First 8 | ForEach-Object { Say "  $_" }
        $warn | Select-Object -First 5 | ForEach-Object { Say "  $_" }
    } catch { }
    try { [IO.Directory]::Delete($data, $true) } catch { Write-Host "Could not remove $data : $_" }
    if ($ReportPath) { $lines | Set-Content $ReportPath -Encoding utf8 }
}
