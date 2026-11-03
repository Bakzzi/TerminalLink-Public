param([Parameter(Mandatory=$true)][string]$Apk,[Parameter(Mandatory=$true)][string]$PcDirectory,[Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSignerSha256,[string]$Notes='',[string]$AndroidSdk='',[string]$JavaHome='')
. (Join-Path $PSScriptRoot 'common.ps1')
$env:JAVA_HOME=Find-Java $JavaHome
$bt=Join-Path (Find-AndroidSdk $AndroidSdk) 'build-tools\36.1.0'
$apkPath=(Resolve-Path -LiteralPath $Apk).Path
$cert=(& (Join-Path $bt 'apksigner.bat') verify --print-certs $apkPath 2>&1|Out-String)
if($LASTEXITCODE -ne 0 -or $cert -notmatch 'Signer #1 certificate SHA-256 digest: ([A-Fa-f0-9]{64})' -or $Matches[1] -ine $ExpectedSignerSha256){throw 'APK signature does not match the intended publisher.'}
$badging=(& (Join-Path $bt 'aapt2.exe') dump badging $apkPath|Out-String)
if($LASTEXITCODE -ne 0 -or $badging -notmatch "package: name='com.terminallink.remote' versionCode='(\d+)' versionName='(\d+\.\d+\.\d+)'" ){throw 'Wrong package or invalid version.'}
$code=[int]$Matches[1];$version=$Matches[2];$size=(Get-Item -LiteralPath $apkPath).Length
if($size -gt 32MB -or $Notes.Length -gt 1500){throw 'APK or release notes exceed protocol limits.'}
$pc=(Resolve-Path -LiteralPath $PcDirectory).Path
if(!(Test-Path -LiteralPath (Join-Path $pc 'TerminalLink-PC.exe'))){throw 'PcDirectory must contain TerminalLink-PC.exe.'}
$updates=Join-Path $pc 'updates';New-Item -ItemType Directory $updates -Force|Out-Null
$catalog=Join-Path $updates 'release.json'
if(Test-Path $catalog){$old=[IO.File]::ReadAllText($catalog)|ConvertFrom-Json;if($old.versionCode -ge $code){throw 'New versionCode must increase.'}}
$hash=(Get-FileHash $apkPath -Algorithm SHA256).Hash
Copy-Item -LiteralPath $apkPath -Destination (Join-Path $updates ($hash+'.apk')) -Force
$json=@{packageName='com.terminallink.remote';versionCode=$code;versionName=$version;size=$size;sha256=$hash;notes=$Notes}|ConvertTo-Json
[IO.File]::WriteAllText(($catalog+'.tmp'),$json,(New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath ($catalog+'.tmp') -Destination $catalog -Force
Write-Output "Published $version. Auto-update-capable phones fetch it over their paired connection and install when idle; Android may require confirmation."
