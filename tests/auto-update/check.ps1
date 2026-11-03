$ErrorActionPreference='Stop'
$root=Resolve-Path (Join-Path $PSScriptRoot '../..')
$work=Join-Path $root 'build/auto-update'
$env:DOTNET_PROCESSOR_COUNT='2'
foreach($version in @('0.0.1','0.0.2')){
 $out=Join-Path $work $(if($version -eq '0.0.1'){'old'}else{'new'})
 dotnet publish (Join-Path $PSScriptRoot 'Fixture.csproj') -c Release -r win-x64 --self-contained true -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:PublishSingleFile=true "-p:Version=$version" -o $out
 if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}
}
$run=Join-Path $work ('check-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $run|Out-Null
$env:TERMINALLINK_UPDATE_FIXTURE=Join-Path $run 'unit'
$unit=Start-Process -FilePath (Join-Path $work 'old/Fixture.exe') -ArgumentList '--unit' -WindowStyle Hidden -PassThru
if(!$unit.WaitForExit(20000) -or $unit.ExitCode -ne 0){throw 'Unit fixture failed'}
Get-Content (Join-Path $env:TERMINALLINK_UPDATE_FIXTURE 'unit-result.txt')
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
foreach($scenario in @('success','rollback','busy')){
 $install=Join-Path $run $scenario;$queue=Join-Path $install 'automatic-updates';New-Item -ItemType Directory $queue -Force|Out-Null
 $target=Join-Path $install 'Fixture.exe';Copy-Item (Join-Path $work 'old/Fixture.exe') $target
 $zip=Join-Path $queue 'bundle.zip';$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
 try{[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $work 'new/Fixture.exe'),'Fixture.exe')|Out-Null;foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $root $name),$name)|Out-Null}}finally{$archive.Dispose()}
 $hash=(Get-FileHash $zip).Hash;$size=(Get-Item $zip).Length;Move-Item $zip (Join-Path $queue ($hash+'.zip'))
 [IO.File]::WriteAllText((Join-Path $queue 'release.json'),(@{product='fixture';version='0.0.2.0';sha256=$hash;size=$size;commit=('b'*40)}|ConvertTo-Json))
 if($scenario -eq 'rollback'){[IO.File]::WriteAllText((Join-Path $install 'fail-start'),'yes')}
 if($scenario -eq 'busy'){[IO.File]::WriteAllText((Join-Path $install 'busy'),'yes')}
 $env:TERMINALLINK_UPDATE_FIXTURE=$install;$process=Start-Process -FilePath $target -WindowStyle Hidden -PassThru
 if($scenario -eq 'busy'){
  Start-Sleep -Seconds 2
  if($process.HasExited -or (Test-Path (Join-Path $queue 'plan.json'))){throw 'Busy application was interrupted'}
  Remove-Item -LiteralPath (Join-Path $install 'busy')
 }
 $deadline=[DateTime]::UtcNow.AddSeconds(55);$result=Join-Path $queue 'last-result.json'
 while(!(Test-Path $result) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 250}
 if(!(Test-Path $result)){throw "Timed out $scenario"}
 $resultData=Get-Content $result -Raw|ConvertFrom-Json
 if($scenario -eq 'rollback'){
  if($resultData.ok -or (Get-FileHash $target).Hash -ne (Get-FileHash (Join-Path $work 'old/Fixture.exe')).Hash){throw 'Rollback failed'}
 }elseif(!$resultData.ok -or (Get-FileHash $target).Hash -ne (Get-FileHash (Join-Path $work 'new/Fixture.exe')).Hash){throw "Update failed: $($resultData.error)"}
 Write-Output "PASS isolated $scenario replacement and executable hash"
}
Write-Output "Evidence: $run"
