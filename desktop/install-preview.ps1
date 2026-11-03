param([Parameter(Mandatory=$true)][string]$ExpectedHash,[switch]$Launch)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Join-Path $root 'dist\Desktop'
$exe=Join-Path $source 'TerminalLink-Desktop.exe'
if((Get-FileHash -LiteralPath $exe).Hash -ne $ExpectedHash){throw 'Desktop artifact hash mismatch'}
$install=Join-Path $env:LOCALAPPDATA 'Programs\TerminalLinkDesktopPreview'
$destination=Join-Path $install 'TerminalLink-Desktop.exe'
if(Get-Process TerminalLink-Desktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $destination}){throw 'Close TerminalLink Desktop before replacing its preview executable'}
New-Item -ItemType Directory -Path $install -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination $destination -Force
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $install -Force}
$licenses=Join-Path $install 'licenses';New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Get-ChildItem (Join-Path $source 'licenses') -File | Copy-Item -Destination $licenses -Force
if((Get-FileHash -LiteralPath $destination).Hash -ne $ExpectedHash){throw 'Installed desktop hash mismatch'}
$shortcutPath=Join-Path ([Environment]::GetFolderPath('Desktop')) 'TerminalLink Desktop.lnk'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($shortcutPath)
if((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -ne $destination){throw 'An unrelated shortcut already uses this name'}
$shortcut.TargetPath=$destination;$shortcut.WorkingDirectory=$install;$shortcut.Description='TerminalLink Desktop preview';$shortcut.IconLocation="$destination,0";$shortcut.Save()
# Refresh only the changed executable and shortcut; do not clear the global icon cache or restart Explorer.
if(-not ('TerminalLinkDesktopShell' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TerminalLinkDesktopShell {
 [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
 public static extern void SHChangeNotify(uint action, uint flags, string item, IntPtr unused);
}
'@
}
[TerminalLinkDesktopShell]::SHChangeNotify(0x2000,0x1005,$destination,[IntPtr]::Zero)
[TerminalLinkDesktopShell]::SHChangeNotify(0x2000,0x1005,$shortcutPath,[IntPtr]::Zero)
if($Launch){Start-Process -FilePath $destination}
Write-Output "Installed isolated desktop preview. Existing PC connector was not modified."
