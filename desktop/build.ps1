param([switch]$Checks)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$python=Get-Command python -ErrorAction SilentlyContinue
if($python){$pythonExe=$python.Source}else{$pythonExe=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'}
& $pythonExe (Join-Path $PSScriptRoot 'check-boundary.py')
if($LASTEXITCODE -ne 0){throw 'Desktop source boundary failed'}
if($Checks){& (Join-Path $PSScriptRoot 'tests/request-records/check.ps1')}
$output=Join-Path $root 'dist\Desktop'
$oldProcessors=$env:DOTNET_PROCESSOR_COUNT
try{
 $env:DOTNET_PROCESSOR_COUNT='2'
 dotnet publish (Join-Path $PSScriptRoot 'TerminalLink.Desktop.csproj') -c Release -r win-x64 --self-contained true -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:PublishSingleFile=true -p:DebugType=None -o $output
 if($LASTEXITCODE -ne 0){throw 'Desktop build failed'}
 Copy-Item (Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD_PARTY_NOTICES.md') $output -Force
 $notices=Join-Path $output 'licenses';New-Item -ItemType Directory -Path $notices -Force|Out-Null
 Get-ChildItem (Join-Path $root 'docs\licenses') -File|Copy-Item -Destination $notices -Force
 if($Checks){
  $check=Start-Process -FilePath (Join-Path $output 'TerminalLink-Desktop.exe') -ArgumentList '--self-check' -WindowStyle Hidden -PassThru
  if(!$check.WaitForExit(45000)){$check.Kill();throw 'Synthetic check timed out; its own fixture process was stopped'}
  $check.Refresh()
  if($check.ExitCode -ne 0){throw 'Desktop self-check failed; see desktop-checks.txt'}
  Get-Content (Join-Path $output 'desktop-checks.txt')
  $ui=Start-Process -FilePath (Join-Path $output 'TerminalLink-Desktop.exe') -ArgumentList '--ui-check' -WindowStyle Hidden -PassThru
  if(!$ui.WaitForExit(20000)){$ui.Kill();throw 'Synthetic desktop UI check timed out'}
  $ui.Refresh()
  if($ui.ExitCode -ne 0){throw 'Desktop UI check failed; see desktop-ui-checks.txt'}
  Get-Content (Join-Path $output 'desktop-ui-checks.txt')
 }
 Get-FileHash (Join-Path $output 'TerminalLink-Desktop.exe') -Algorithm SHA256
}finally{if($null -eq $oldProcessors){Remove-Item Env:DOTNET_PROCESSOR_COUNT -ErrorAction SilentlyContinue}else{$env:DOTNET_PROCESSOR_COUNT=$oldProcessors}}
