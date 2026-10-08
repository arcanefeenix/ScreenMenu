<#
.SYNOPSIS
  Drives the real operator window with Windows UI Automation: opens the Screens tab on a seeded data folder, checks that the
  video playlist editor is there, presses "Preview videos", then Next / Pause / Resume / Previous and reads back what the program says.
  Nothing is sent to any display (it uses the desk preview, not the LED output).

.USAGE
  & .\tools\video-ui-smoke.ps1 -Clips $arrayOfThreeMp4Paths
#>
param([Parameter(Mandatory)] [string[]]$Clips)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

dotnet build LedMenu.sln -c Release -warnaserror | Out-Null
dotnet build tools\SeedVideoData -c Release | Out-Null
$seed = Get-ChildItem tools\SeedVideoData\bin\Release -Recurse -Filter SeedVideoData.dll | Select-Object -First 1
$app = Get-ChildItem src\LedMenu.App\bin\Release -Recurse -Filter LedMenu.App.exe | Select-Object -First 1
$data = Join-Path ([IO.Path]::GetTempPath()) ('ledmenu-ui-' + [guid]::NewGuid().ToString('N'))
$p = $null
$AE = [Windows.Automation.AutomationElement]
function Find($root, $prop, $value) { $root.FindFirst('Descendants', (New-Object Windows.Automation.PropertyCondition($prop, $value))) }
function ById($root, $id) { Find $root $AE::AutomationIdProperty $id }
function ByIdAll($root, $id) { @($root.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))) }
function ByName($root, $name) { Find $root $AE::NameProperty $name }
function Press($el) {
    if ($null -eq $el) { throw 'element not found' }
    $pat = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); return }
    if ($el.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select(); return }
    if ($el.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { $pat.Toggle(); return }
    throw "element $($el.Current.Name) supports no way of being pressed"
}
function Texts($root) { $root.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name } }
$results = [ordered]@{}
try {
    dotnet $seed.FullName $data @Clips | Out-Null
    $env:LEDMENU_DATA_DIR = $data
    $p = Start-Process $app.FullName -PassThru
    Start-Sleep 6
    $win = $AE::FromHandle($p.MainWindowHandle)

    # open the Screens tab
    $tab = $win.FindFirst('Descendants', (New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($AE::NameProperty, 'Screens')),
        (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)))))
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep 1

    $results['Screens tab has the video editor ("Add videos…" button)'] = [bool](ById $win 'AddVideos')
    $results['Screens tab has Shows: Menu and Video choices'] = ([bool](ById $win 'ShowsMenu')) -and ([bool](ById $win 'ShowsVideo'))
    $allText = Texts $win
    $results['Three imported videos are listed with their sizes and true lengths'] = (($allText | Where-Object { $_ -match '168.672 . 0:39' }).Count -ge 1) -and (($allText | Where-Object { $_ -match '168.672 . 0:06' }).Count -ge 1) -and (($allText | Where-Object { $_ -match '168.672 . 0:13' }).Count -ge 1)
    $results['Nothing playing yet (output stopped): status says so'] = [bool]($allText | Where-Object { $_ -like 'Not playing*' })

    # preview videos at the desk
    $preview = ById $win 'PreviewVideos'
    $results['"Preview videos" button is offered'] = [bool]$preview
    if ($preview) {
        Press $preview
        Start-Sleep 3
        $t = Texts $win
        $results['After Preview videos: status shows "Playing 1 of 3"'] = [bool]($t | Where-Object { $_ -like 'Playing 1 of 3*' })
        Press (ById $win 'VideoNext'); Start-Sleep 2
        $results['Next goes to 2 of 3'] = [bool]((Texts $win) | Where-Object { $_ -like 'Playing 2 of 3*' })
        Press (ById $win 'VideoPlayPause'); Start-Sleep 1
        $results['Pause shows "Paused on 2 of 3"'] = [bool]((Texts $win) | Where-Object { $_ -like 'Paused on 2 of 3*' })
        Press (ById $win 'VideoPlayPause'); Start-Sleep 2
        $results['Resume plays again'] = [bool]((Texts $win) | Where-Object { $_ -like 'Playing 2 of 3*' })
        Press (ById $win 'VideoPrevious'); Start-Sleep 2
        $results['Previous goes back to 1 of 3'] = [bool]((Texts $win) | Where-Object { $_ -like 'Playing 1 of 3*' })
        Press (ByName $win 'Stop previewing videos'); Start-Sleep 2
        $results['Stopping the preview returns to "Not playing"'] = [bool]((Texts $win) | Where-Object { $_ -like 'Not playing*' })
    }

    # switch the screen to Menu and back: nothing is lost
    # the seeded layout has two cards: screen 1 shows a menu, screen 2 shows video; use the second card's choices
    Press ((ByIdAll $win 'ShowsMenu')[1]); Start-Sleep 1
    $results['Switching the screen to Menu hides the playlist editor'] = -not [bool](ById $win 'AddVideos')
    Press ((ByIdAll $win 'ShowsVideo')[1]); Start-Sleep 1
    $results['Switching back to Video brings the same playlist back'] = [bool](ById $win 'AddVideos') -and (((Texts $win) | Where-Object { $_ -match '168.672 . 0:39' }).Count -ge 1)
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    $env:LEDMENU_DATA_DIR = $null
    Start-Sleep 1
    $logText = ''
    try { $logText = Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName } | Where-Object { $_ -match '\[ERROR\]|Unhandled' } } catch { }
    $results['No errors in the program log'] = (-not $logText)
    try { [IO.Directory]::Delete($data, $true) } catch { Write-Host "Could not remove $data : $_" }
}
$failed = 0
foreach ($k in $results.Keys) { $ok = $results[$k]; if (-not $ok) { $failed++ }; '{0,-6} {1}' -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $k }
if ($failed) { "$failed check(s) failed"; exit 1 } else { 'ALL CHECKS PASSED' }
