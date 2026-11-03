$ErrorActionPreference='Stop'
$env:DOTNET_PROCESSOR_COUNT='2'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
dotnet run --project (Join-Path $PSScriptRoot 'Fixture.csproj') -c Release -p:UseSharedCompilation=false -m:1 -nodeReuse:false
if($LASTEXITCODE -ne 0){throw 'Request lifecycle checks failed'}
