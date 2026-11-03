param([Parameter(Mandatory=$true)][string]$ExpectedHash,[switch]$EnableOnLogin,[switch]$Launch)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source=Join-Path $root 'dist\FileBoxBridge'
$exe=Join-Path $source 'TerminalLink-FileBox.exe'
if((Get-FileHash -LiteralPath $exe).Hash -ne $ExpectedHash){throw 'Bridge artifact hash mismatch'}
$version=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$install=Join-Path $env:LOCALAPPDATA ('Programs\TerminalLinkDesktopPreview\FileBoxBridge\'+$version)
$destination=Join-Path $install 'TerminalLink-FileBox.exe'
if(Get-Process TerminalLink-FileBox -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $destination}){throw 'This bridge version is already running; do not overwrite it'}
New-Item -ItemType Directory -Path $install -Force|Out-Null
Copy-Item -LiteralPath $exe -Destination $destination -Force
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $install -Force}
$licenses=Join-Path $install 'licenses';New-Item -ItemType Directory -Path $licenses -Force|Out-Null
Get-ChildItem (Join-Path $source 'licenses') -File|Copy-Item -Destination $licenses -Force
if((Get-FileHash -LiteralPath $destination).Hash -ne $ExpectedHash){throw 'Installed bridge hash mismatch'}
if($EnableOnLogin){
 $shortcutPath=Join-Path ([Environment]::GetFolderPath('Startup')) 'TerminalLink FileBox.lnk'
 $shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($shortcutPath)
 if((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -notlike "$env:LOCALAPPDATA\Programs\TerminalLinkDesktopPreview\FileBoxBridge\*\TerminalLink-FileBox.exe"){throw 'An unrelated startup shortcut exists'}
 $shortcut.TargetPath=$destination;$shortcut.Arguments='--run';$shortcut.WorkingDirectory=$install;$shortcut.Description='TerminalLink SSH file box receiver';$shortcut.Save()
}
if($Launch){Start-Process -FilePath $destination -ArgumentList '--run' -WindowStyle Hidden}
Write-Output "Installed TerminalLink filebox receiver $version. Desktop, connector and terminal sessions were not replaced."
