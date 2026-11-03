param([string]$Output='', [switch]$TestHooks)
. (Join-Path $PSScriptRoot 'common.ps1')
$root=Split-Path $PSScriptRoot -Parent
if(!$Output){$Output=Join-Path $root 'dist\Windows'}
$hooks=if($TestHooks){'true'}else{'false'}
$env:DOTNET_PROCESSOR_COUNT='2'
Invoke-Native 'dotnet' @('publish',(Join-Path $root 'host\TerminalLink.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-m:1','-nodeReuse:false','-p:UseSharedCompilation=false','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None',"-p:TestHooks=$hooks",'-o',$Output)
Copy-Item (Join-Path $root 'LICENSE'),(Join-Path $root 'THIRD_PARTY_NOTICES.md') $Output -Force
$licenses=Join-Path $Output 'licenses';New-Item -ItemType Directory $licenses -Force|Out-Null
Get-ChildItem (Join-Path $root 'docs\licenses') -File | Copy-Item -Destination $licenses -Force
Get-FileHash (Join-Path $Output 'TerminalLink-PC.exe') -Algorithm SHA256
