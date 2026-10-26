param(
  [Parameter(Mandatory=$true)][ValidateSet("Click","Move","Drag","Down","Up","Text","Key")][string]$Action,
  [int]$X = 0,
  [int]$Y = 0,
  [int]$ToX = 0,
  [int]$ToY = 0,
  [int]$DurationMs = 350,
  [string]$Value = ""
)
$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UiInputNative {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
$shell = New-Object -ComObject WScript.Shell
$process = Get-Process CardGame -ErrorAction Stop | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
$shell.AppActivate($process.Id) | Out-Null
Start-Sleep -Milliseconds 250
if ($Action -eq "Click") {
  [UiInputNative]::SetCursorPos($X,$Y) | Out-Null
  [UiInputNative]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
  [UiInputNative]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
} elseif ($Action -eq "Move") {
  [UiInputNative]::SetCursorPos($X,$Y) | Out-Null
  [UiInputNative]::mouse_event(0x0001,1,0,0,[UIntPtr]::Zero)
} elseif ($Action -eq "Down") {
  [UiInputNative]::SetCursorPos($X,$Y) | Out-Null
  [UiInputNative]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
} elseif ($Action -eq "Up") {
  [UiInputNative]::SetCursorPos($X,$Y) | Out-Null
  [UiInputNative]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
} elseif ($Action -eq "Drag") {
  [UiInputNative]::SetCursorPos($X,$Y) | Out-Null
  [UiInputNative]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
  $steps = [Math]::Max(8,[Math]::Ceiling([Math]::Max(80,$DurationMs)/16))
  for ($step=1; $step -le $steps; $step++) {
    $t = $step / $steps
    $eased = $t * $t * (3 - 2 * $t)
    [UiInputNative]::SetCursorPos([Math]::Round($X + ($ToX-$X)*$eased),[Math]::Round($Y + ($ToY-$Y)*$eased)) | Out-Null
    # Emit a real movement delta so Unity's Input System receives each OS move.
    [UiInputNative]::mouse_event(0x0001,1,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds ([Math]::Max(1,[Math]::Floor($DurationMs/$steps)))
  }
  [UiInputNative]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
} elseif ($Action -eq "Text") {
  $shell.SendKeys("^a"); Start-Sleep -Milliseconds 80
  $escaped = $Value.Replace("{","{{}").Replace("}","{}}").Replace("+","{+}").Replace("^","{^}").Replace("%","{%}").Replace("~","{~}").Replace("(","{(}").Replace(")","{)}")
  $shell.SendKeys($escaped)
} else { $shell.SendKeys($Value) }
Start-Sleep -Milliseconds 350
