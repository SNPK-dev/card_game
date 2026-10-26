param([string]$Title = "CardGame.Unity", [Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowCaptureNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
"@
Add-Type -AssemblyName System.Drawing
$handle = [WindowCaptureNative]::FindWindow($null, $Title)
if ($handle -eq [IntPtr]::Zero) { $handle = (Get-Process CardGame -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1).MainWindowHandle }
if (-not $handle -or $handle -eq [IntPtr]::Zero) { throw "Window '$Title' was not found." }
$rect = New-Object WindowCaptureNative+RECT
if (-not [WindowCaptureNative]::GetWindowRect($handle, [ref]$rect)) { throw "GetWindowRect failed." }
[WindowCaptureNative]::SetForegroundWindow($handle) | Out-Null
$process = Get-Process CardGame -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -eq $handle | Select-Object -First 1
if ($process) { (New-Object -ComObject WScript.Shell).AppActivate($process.Id) | Out-Null; Start-Sleep -Milliseconds 500 }
$width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try { $hdc=$graphics.GetHdc(); try { [WindowCaptureNative]::PrintWindow($handle,$hdc,2) | Out-Null } finally { $graphics.ReleaseHdc($hdc) }; $bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png) }
finally { $graphics.Dispose(); $bitmap.Dispose() }
Write-Output "HANDLE=$handle RECT=$($rect.Left),$($rect.Top),$width,$height"
