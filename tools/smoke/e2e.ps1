<#
.SYNOPSIS
  Live end-to-end acceptance test on the REAL desktop: starts SnapGit with an isolated data folder, drives it with
  synthetic hotkeys/mouse against a known scrollable window, and checks the files it produces.

.DESCRIPTION
  Scenarios:  A region capture · B scrolling capture · C Grab Text (OCR) · D GIF · E MP4 · F free-hand
              G multi-area · H repeat last · I PrintWindow
  Takes ~1.5 minutes for all; do not touch mouse/keyboard meanwhile. Never touches your real settings or
  library: it sets SNAPGIT_DATA to a temp folder. The test window opens at 120,120 on the primary monitor.

.EXAMPLE
  dotnet build SnapGit.sln
  powershell -NoProfile -File tools\smoke\e2e.ps1                 # everything
  powershell -NoProfile -File tools\smoke\e2e.ps1 -Only B,G -KeepFiles   # just two scenarios, keep the output + log
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\SnapGit.App\bin\Debug\net8.0-windows10.0.19041.0\SnapGit.exe'),
    [string[]]$Only,
    [switch]$KeepFiles
)

. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })   # "-File ... -Only B,G" arrives as one string
$Exe = (Resolve-Path $Exe).Path

$script:root = Join-Path ([IO.Path]::GetTempPath()) ("snapgit-e2e-" + [Guid]::NewGuid().ToString('N'))
$script:data = Join-Path $script:root 'data'
$script:lib = Join-Path $script:root 'lib'
New-Item -ItemType Directory -Force $script:data, $script:lib | Out-Null
$info = Join-Path $script:root 'info.txt'
$stop = Join-Path $script:root 'stop.txt'

# isolated profile: nothing is shared with the user's real SnapGit
@{ LibraryFolder = $script:lib; OpenEditorAfterCapture = $true; CaptureDelaySeconds = 0; Language = 'en'; Theme = 'Light'; UsePrintWindowForWindows = $true } |
    ConvertTo-Json | Set-Content (Join-Path $script:data 'settings.json') -Encoding UTF8
$env:SNAPGIT_DATA = $script:data

function Read-AppLog { Get-ChildItem (Join-Path $script:data 'logs') -Filter *.log -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName } }

# ------------------------------------------------------------------ scenarios (each gets the text-area rectangle $r and its centre)

function Test-A-Region {
    $n = (Get-Projects $script:lib).Count
    Send-Hotkey $CtrlAlt 0x31; Start-Sleep -Seconds 2
    Drag-Mouse ($r[0] + 40) ($r[1] + 40) ($r[0] + 340) ($r[1] + 240); Start-Sleep -Seconds 3
    $ok = (Get-Projects $script:lib).Count -eq $n + 1
    Assert-Check 'A region capture creates a project' $ok
    if ($ok) { $img = Read-ProjectImage (Get-NewestProject $script:lib); Assert-Check 'A region capture size ~300x200' (([Math]::Abs($img.Width - 300) -le 2) -and ([Math]::Abs($img.Height - 200) -le 2)) "$($img.Width)x$($img.Height)" }
}

function Test-B-Scrolling {
    $n = (Get-Projects $script:lib).Count
    Send-Hotkey $CtrlAlt 0x34; Start-Sleep -Seconds 2
    Click-At $cx $cy
    $done = Wait-Until { (Get-Projects $script:lib).Count -gt $n } 90
    Assert-Check 'B scrolling capture finishes' $done
    if ($done) {
        $img = Read-ProjectImage (Get-NewestProject $script:lib)
        Assert-Check 'B scrolling capture covers all 300 lines (24 px each)' (($img.Height -ge 7100) -and ($img.Height -le 7400)) "$($img.Width)x$($img.Height)"
        Assert-Check 'B scrolling capture is as wide as the control' (($img.Width -ge $r[2]) -and ($img.Width -le $r[2] + 40)) "$($img.Width) vs text area $($r[2])"
    }
}

function Test-C-Ocr {
    [System.Windows.Forms.Clipboard]::Clear()
    Send-Hotkey $CtrlAlt 0x35; Start-Sleep -Seconds 2
    Click-At $cx $cy; Start-Sleep -Seconds 5
    $text = [System.Windows.Forms.Clipboard]::GetText()
    $lines = @($text -split "`r?`n" | Where-Object { $_.Trim() })
    Assert-Check 'C OCR text reaches the clipboard' ($text -match 'token') "$($lines.Count) lines"
    Assert-Check 'C OCR keeps a row together ("Line" and "unique" on one line)' ((@($lines | Where-Object { $_ -match 'Line.*unique' }).Count) -ge 5) (($lines | Select-Object -First 2) -join ' | ')
}

function Test-D-Gif {
    Send-Hotkey $CtrlAlt 0x38; Start-Sleep -Seconds 2
    Click-At $cx $cy; Start-Sleep -Seconds 4
    Move-Mouse $cx $cy; 1..8 | ForEach-Object { Scroll-Wheel 2; Start-Sleep -Milliseconds 350 }
    Send-Hotkey $CtrlAlt 0x38
    $ok = Wait-Until { @(Get-ChildItem -LiteralPath $script:lib -Filter *.gif).Count -ge 1 } 30
    Assert-Check 'D GIF recording saved' $ok
    if ($ok) {
        $gif = Get-ChildItem -LiteralPath $script:lib -Filter *.gif | Select-Object -First 1
        $g = [System.Drawing.Image]::FromFile($gif.FullName); $dim = New-Object System.Drawing.Imaging.FrameDimension($g.FrameDimensionsList[0])
        $frames = $g.GetFrameCount($dim); $g.Dispose()
        Assert-Check 'D GIF has many frames' ($frames -ge 15) "$frames frames, $([int]($gif.Length / 1024)) KB"
    }
}

function Test-E-Video {
    Send-Hotkey $CtrlAlt 0x39; Start-Sleep -Seconds 2
    Click-At $cx $cy; Start-Sleep -Seconds 4.5
    Move-Mouse $cx $cy; 1..8 | ForEach-Object { Scroll-Wheel 2; Start-Sleep -Milliseconds 300 }
    Send-Hotkey $CtrlAlt 0x39
    $ok = Wait-Until { @(Get-ChildItem -LiteralPath $script:lib -Filter *.mp4 | Where-Object { $_.Length -gt 50000 }).Count -ge 1 } 40
    Assert-Check 'E MP4 recording saved (and not deleted afterwards)' $ok
    if ($ok) {
        $mp4 = Get-ChildItem -LiteralPath $script:lib -Filter *.mp4 | Select-Object -First 1
        $shell = New-Object -ComObject Shell.Application; $folder = $shell.Namespace($mp4.DirectoryName); $item = $folder.ParseName($mp4.Name)
        $len = $folder.GetDetailsOf($item, 27)
        Assert-Check 'E MP4 has a duration of a few seconds' ($len -match '0:00:0[3-9]|0:00:1\d') "duration $len, $([int]($mp4.Length / 1024)) KB"
    }
}

function Test-F-Freehand {
    $n = (Get-Projects $script:lib).Count
    Send-Hotkey $CtrlAlt 0x36; Start-Sleep -Seconds 2
    $x = $r[0] + 100; $y = $r[1] + 60
    Move-Mouse $x $y; Mouse-Down
    Move-Mouse ($x + 400) $y; Move-Mouse ($x + 400) ($y + 300); Move-Mouse ($x + 200) ($y + 150); Move-Mouse $x ($y + 300); Move-Mouse $x ($y + 20)
    Mouse-Up; Start-Sleep -Seconds 3
    if ((Get-Projects $script:lib).Count -eq $n + 1) {
        $img = Read-ProjectImage (Get-NewestProject $script:lib)
        Assert-Check 'F free-hand: outside the shape is transparent, inside is not' (($img.GetPixel(200, 285).A -eq 0) -and ($img.GetPixel(60, 100).A -eq 255)) "$($img.Width)x$($img.Height)"
    } else { Assert-Check 'F free-hand capture creates a project' $false }
}

function Test-G-MultiArea {
    $n = (Get-Projects $script:lib).Count
    Send-Hotkey $CtrlAlt 0x37; Start-Sleep -Seconds 2
    Drag-Mouse ($r[0] + 30) ($r[1] + 30) ($r[0] + 230) ($r[1] + 130)
    Drag-Mouse ($r[0] + 330) ($r[1] + 250) ($r[0] + 560) ($r[1] + 340)
    Start-Sleep -Milliseconds 300; Send-Hotkey ([byte[]]@()) 0x0D; Start-Sleep -Seconds 3
    if ((Get-Projects $script:lib).Count -eq $n + 1) {
        $img = Read-ProjectImage (Get-NewestProject $script:lib)
        Assert-Check 'G multi-area: bounding box ~530x310 with transparent gap' (([Math]::Abs($img.Width - 530) -le 3) -and ([Math]::Abs($img.Height - 310) -le 3) -and ($img.GetPixel(300, 60).A -eq 0)) "$($img.Width)x$($img.Height)"
    } else { Assert-Check 'G multi-area capture creates a project' $false }
}

function Test-H-Repeat {
    $n = (Get-Projects $script:lib).Count
    Send-Hotkey $CtrlAlt 0x52; Start-Sleep -Seconds 3
    Assert-Check 'H repeat last capture' ((Get-Projects $script:lib).Count -eq $n + 1)
}

function Test-I-PrintWindow {
    # The app's own window steps aside during a capture, so the target is the test window (another process).
    $tw = @([SnapWin]::VisibleWindows([uint32]$testWin.Id) | ForEach-Object { $q = $_.Split(',') | ForEach-Object { [int]$_ }; [pscustomobject]@{ X = $q[0]; Y = $q[1]; W = $q[2]; H = $q[3] } } |
        Sort-Object { $_.W * $_.H } -Descending) | Select-Object -First 1
    if (-not $tw) { Assert-Check 'I test window found for the PrintWindow test' $false; return }

    $n = (Get-Projects $script:lib).Count
    Send-Hotkey $CtrlAlt 0x32; Start-Sleep -Seconds 2
    Click-At ($tw.X + $tw.W / 2) ($tw.Y + $tw.H / 2); Start-Sleep -Seconds 3
    if ((Get-Projects $script:lib).Count -eq $n + 1) {
        $img = Read-ProjectImage (Get-NewestProject $script:lib)
        # PrintWindow gives the visible frame: a few pixels (the invisible resize border) smaller than the window rectangle.
        Assert-Check 'I PrintWindow captured the whole window without its invisible border' (($img.Width -lt $tw.W) -and ($img.Width -ge $tw.W - 30) -and ($img.Height -ge $tw.H - 30)) "$($img.Width)x$($img.Height) of window $($tw.W)x$($tw.H)"
    } else { Assert-Check 'I window capture creates a project' $false }
}

function Test-J-WindowStepsAside {
    # The main window is one big window (1240x800 client + frame); it must be gone while the capture overlay is up and back afterwards.
    function Main-Visible { @(Get-SnapWindows | Where-Object { $_ -match '^-?\d+,-?\d+,1[1-3]\d\d,(7|8|9)\d\d$' }).Count -gt 0 }
    Send-Hotkey $CtrlAlt 0x4C; Start-Sleep -Seconds 2
    $before = Main-Visible
    Send-Hotkey $CtrlAlt 0x32; Start-Sleep -Seconds 2
    $during = Main-Visible
    Send-Hotkey @() 0x1B; Start-Sleep -Milliseconds 800
    Send-Hotkey @() 0x1B; Start-Sleep -Seconds 2        # Esc cancels the capture
    $after = Main-Visible
    Assert-Check 'J main window is visible, steps aside during a capture and returns after it' ($before -and -not $during -and $after) "before=$before during=$during after=$after"
}

# ------------------------------------------------------------------ run

Stop-Process -Name SnapGit -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

$psi = New-Object System.Diagnostics.ProcessStartInfo 'powershell.exe', "-NoProfile -STA -File `"$PSScriptRoot\testwin.ps1`" -InfoFile `"$info`" -StopFile `"$stop`""
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$testWin = [System.Diagnostics.Process]::Start($psi)
if (-not (Wait-Until { Test-Path $info } 20)) { throw 'the test window did not appear' }
$r = (Get-Content $info).Split(',') | ForEach-Object { [int]$_ }      # left, top, width, height of the text area
$cx = $r[0] + 300; $cy = $r[1] + 250

$app = Start-Process $Exe -ArgumentList '--background' -PassThru
Start-Sleep -Seconds 4

try {
    $startup = @(Read-AppLog)
    Assert-Check '0 startup: all 11 hotkeys registered' (-not ($startup -match 'RegisterHotKey failed')) (($startup -match 'RegisterHotKey failed') -join '; ')

    $scenarios = [ordered]@{
        A = { Test-A-Region }; B = { Test-B-Scrolling }; C = { Test-C-Ocr }; D = { Test-D-Gif }; E = { Test-E-Video }
        F = { Test-F-Freehand }; G = { Test-G-MultiArea }; H = { Test-H-Repeat }; I = { Test-I-PrintWindow }; J = { Test-J-WindowStepsAside }
    }
    foreach ($key in $scenarios.Keys) {
        if ($Only -and ($Only -notcontains $key)) { continue }

        # Synthetic input on a live desktop can be disturbed by anything else touching the mouse/keyboard:
        # a failing scenario gets exactly ONE more attempt, and the retry is reported (never hidden).
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $mark = $script:Results.Count
            & $scenarios[$key]
            $failedNow = @($script:Results | Select-Object -Skip $mark | Where-Object { -not $_.Pass })
            if ($failedNow.Count -eq 0 -or $attempt -eq 2) { break }
            Write-Host "[RETRY] scenario $key failed ($($failedNow.Count) check(s)); trying once more" -ForegroundColor Yellow
            $script:Results.RemoveRange($mark, $script:Results.Count - $mark)
            Start-Sleep -Seconds 2
        }
    }

    $errors = @(Read-AppLog | Where-Object { $_ -match ' ERROR ' })
    Assert-Check 'Z no ERROR entries in the app log' ($errors.Count -eq 0) (($errors | Select-Object -First 2) -join ' | ')
}
finally {
    New-Item -ItemType File -Force $stop | Out-Null
    $logCopy = @(Read-AppLog)
    Stop-Process -Name SnapGit -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    if (-not $testWin.HasExited) { $testWin.Kill() }
    Remove-Item Env:\SNAPGIT_DATA -ErrorAction SilentlyContinue
    if ($KeepFiles) {
        Write-Host "Files kept in $($script:root)"
        Write-Host "---- app log ----"; $logCopy | Select-Object -Last 60 | ForEach-Object { Write-Host $_ }
    } else { Remove-Item $script:root -Recurse -Force -ErrorAction SilentlyContinue }
}

$failed = @($script:Results | Where-Object { -not $_.Pass })
Write-Host ""
Write-Host ("{0} checks, {1} passed, {2} failed" -f $script:Results.Count, ($script:Results.Count - $failed.Count), $failed.Count)
exit $failed.Count
