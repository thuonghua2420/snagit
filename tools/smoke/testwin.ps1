# A scrollable test window with 300 uniquely numbered lines (24 px each at 100% DPI). Used as a known capture target.
# Writes "left,top,width,height" of its text area to -InfoFile when shown; closes when -StopFile appears.
param([string]$InfoFile, [string]$StopFile)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace T -Name D -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][T.D]::SetProcessDPIAware()
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'SnapGit Scroll Test Window'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(120, 120)
$form.Size = New-Object System.Drawing.Size(760, 560)
$form.TopMost = $true

$tb = New-Object System.Windows.Forms.TextBox
$tb.Multiline = $true; $tb.ReadOnly = $true; $tb.ScrollBars = 'Vertical'; $tb.Dock = 'Fill'
$tb.Font = New-Object System.Drawing.Font('Consolas', 16)
$tb.BackColor = [System.Drawing.Color]::White
$tb.Text = ((1..300 | ForEach-Object { 'Line {0:D3}  unique-token-{1}' -f $_, (($_ * 7919) % 10007) }) -join "`r`n")
$tb.TabStop = $false
# A TextBox selects all of its text when it first gets focus and later drops that selection: the colour change of every row mid-scroll would make the capture unalignable. Keep the selection empty.
$tb.Add_GotFocus({ $tb.SelectionStart = 0; $tb.SelectionLength = 0 })
$form.Controls.Add($tb)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 300
$timer.Add_Tick({ if (Test-Path $StopFile) { $form.Close() } })
$timer.Start()

$form.Add_Shown({
    $tb.SelectionStart = 0; $tb.SelectionLength = 0; $form.ActiveControl = $null
    $r = $tb.RectangleToScreen($tb.ClientRectangle)
    "$($r.Left),$($r.Top),$($r.Width),$($r.Height)" | Set-Content $InfoFile
})
[void]$form.ShowDialog()
