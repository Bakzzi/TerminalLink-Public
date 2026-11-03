param()
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$python=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $python (Join-Path $root 'desktop\check-boundary.py')
if($LASTEXITCODE -ne 0){throw 'Desktop source boundary failed'}
$oldProcessors=$env:DOTNET_PROCESSOR_COUNT
try {
 Push-Location $PSScriptRoot
 $env:DOTNET_PROCESSOR_COUNT='2'
 $output=Join-Path $root 'dist\FileBoxBridge'
 dotnet publish (Join-Path $PSScriptRoot 'TerminalLink.FileBox.csproj') -c Release -r win-x64 --self-contained true -m:1 -nodeReuse:false -p:RestoreLockedMode=true -p:UseSharedCompilation=false -p:PublishSingleFile=true -p:DebugType=None -o $output
 if($LASTEXITCODE -ne 0){throw 'Filebox bridge build failed'}
 Copy-Item (Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD_PARTY_NOTICES.md') $output -Force
 $licenses=Join-Path $output 'licenses';New-Item -ItemType Directory -Path $licenses -Force|Out-Null
 Get-ChildItem (Join-Path $root 'docs\licenses') -File|Copy-Item -Destination $licenses -Force
 $exe=Join-Path $output 'TerminalLink-FileBox.exe'
 $p=Start-Process -FilePath $exe -ArgumentList '--check' -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(30000)){throw 'Filebox checks timed out'}
 if($p.ExitCode -ne 0){throw 'Filebox checks failed'}
 Get-Content (Join-Path $output 'filebox-checks.json') -Encoding UTF8
 Get-FileHash -LiteralPath $exe
}finally {Pop-Location;if($null -eq $oldProcessors){Remove-Item Env:DOTNET_PROCESSOR_COUNT -ErrorAction SilentlyContinue}else{$env:DOTNET_PROCESSOR_COUNT=$oldProcessors}}
