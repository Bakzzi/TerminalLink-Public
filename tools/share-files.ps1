param([Parameter(Mandatory=$true)][string[]]$Files,[Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
$resolved=@(foreach($file in $Files){$item=Get-Item -LiteralPath $file;if($item.PSIsContainer){throw 'Folders must first be compressed as ZIP'};$item.FullName})
$temp=Join-Path $env:TEMP ('TerminalLink-publish-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$request=Join-Path $temp 'request.json';$result=Join-Path $temp 'result.json'
[IO.File]::WriteAllText($request,(@{files=$resolved}|ConvertTo-Json -Compress),(New-Object Text.UTF8Encoding($false)))
try {
 $process=Start-Process -FilePath $Executable -ArgumentList @('--publish-files',('"'+$request+'"'),('"'+$result+'"')) -Wait -PassThru -WindowStyle Hidden
 if(!(Test-Path -LiteralPath $result)){throw 'Publish result missing; check the PC executable version.'}
 $items=Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json
 $items | ConvertTo-Json -Depth 5
 if($process.ExitCode -ne 0 -or @($items | Where-Object {!$_.ok}).Count -gt 0){throw 'One or more files could not be published; successful items remain in the file box.'}
}finally{
 foreach($path in @($request,$result)){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}
 Remove-Item -LiteralPath $temp
}
