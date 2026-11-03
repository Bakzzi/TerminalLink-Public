$ErrorActionPreference='Stop'
$env:DOTNET_PROCESSOR_COUNT='2'
dotnet run --project (Join-Path $PSScriptRoot 'Fixture.csproj') -c Release -p:UseSharedCompilation=false -m:1 -nodeReuse:false
if($LASTEXITCODE -ne 0){throw 'Original request record checks failed'}
