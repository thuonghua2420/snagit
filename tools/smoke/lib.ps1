# Shared helpers for the live (real desktop) smoke tests. Dot-source it:  . "$PSScriptRoot\lib.ps1"
# Everything here talks to a REAL desktop: synthetic keys/mouse via user32, window enumeration by process id.

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, System.IO.Compression.FileSystem

Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class SnapWin {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  public static List<string> VisibleWindows(uint pid) {
    var res = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RECT r; GetWindowRect(h, out r); res.Add(r.L + "," + r.T + "," + (r.R - r.L) + "," + (r.B - r.T)); }
      return true; }, IntPtr.Zero);
    return res;
  }
}
"@
[void][SnapWin]::SetProcessDPIAware()

$script:Primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

function Get-SnapWindows { $p = Get-Process SnapGit -ErrorAction SilentlyContinue; if ($p) { [SnapWin]::VisibleWindows([uint32]$p.Id) } }

# ---- keyboard: Send-Hotkey @(0xA2,0xA4) 0x31  == Ctrl+Alt+1   (0xA2 LCtrl, 0xA4 LAlt, 0x10 Shift)
function Send-Hotkey([byte[]]$Modifiers, [byte]$Key) {
    foreach ($m in $Modifiers) { [SnapWin]::keybd_event($m, 0, 0, [UIntPtr]::Zero) }
    [SnapWin]::keybd_event($Key, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 120
    [SnapWin]::keybd_event($Key, 0, 2, [UIntPtr]::Zero)
    $rev = [byte[]]$Modifiers.Clone(); [array]::Reverse($rev)
    foreach ($m in $rev) { [SnapWin]::keybd_event($m, 0, 2, [UIntPtr]::Zero) }
}
$CtrlAlt = [byte[]](0xA2, 0xA4)

# ---- mouse (absolute coordinates on the primary monitor)
function Move-Mouse($x, $y) {
    $nx = [uint32](($x - $script:Primary.X) * 65535 / $script:Primary.Width)
    $ny = [uint32](($y - $script:Primary.Y) * 65535 / $script:Primary.Height)
    [SnapWin]::mouse_event(0x8001, $nx, $ny, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
}
function Mouse-Down { [SnapWin]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }
function Mouse-Up { [SnapWin]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }
function Click-At($x, $y) { Move-Mouse $x $y; Mouse-Down; Mouse-Up }
function Drag-Mouse($x1, $y1, $x2, $y2) {
    Move-Mouse $x1 $y1; Start-Sleep -Milliseconds 250   # let the overlay register the hover before pressing
    Mouse-Down
    for ($i = 1; $i -le 8; $i++) { Move-Mouse ([int]($x1 + ($x2 - $x1) * $i / 8)) ([int]($y1 + ($y2 - $y1) * $i / 8)) }
    Mouse-Up
}
function Scroll-Wheel($notches) {
    $d = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int](-120 * $notches)), 0)
    [SnapWin]::mouse_event(0x800, 0, 0, $d, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
}

# ---- results
$script:Results = New-Object System.Collections.Generic.List[object]
function Assert-Check([string]$Name, [bool]$Condition, [string]$Detail = '') {
    $script:Results.Add([pscustomobject]@{ Name = $Name; Pass = $Condition; Detail = $Detail })
    $mark = if ($Condition) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1}  {2}" -f $mark, $Name, $Detail)
}

# ---- library helpers
function Get-Projects($lib) { @(Get-ChildItem -LiteralPath $lib -Filter *.snapgit -ErrorAction SilentlyContinue) }
function Get-NewestProject($lib) { Get-Projects $lib | Sort-Object CreationTime -Descending | Select-Object -First 1 }
function Read-ProjectImage($file) {
    $zip = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try { $e = $zip.GetEntry('image.png'); $st = $e.Open(); $ms = New-Object IO.MemoryStream; $st.CopyTo($ms); $st.Dispose(); [System.Drawing.Bitmap]::new($ms) }
    finally { $zip.Dispose() }
}
function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds = 30) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 300 }
    return $false
}
