param([Parameter(Mandatory=$true)][string]$ServiceAccount)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
$raw=[IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ServiceAccount).Path)
try{
 $config=[Text.Encoding]::UTF8.GetString($raw)|ConvertFrom-Json
 if($config.type -ne 'service_account' -or !$config.project_id -or !$config.client_email -or !$config.private_key){throw 'Invalid service account JSON.'}
 $folder=Join-Path $env:LOCALAPPDATA 'TerminalLink';New-Item -ItemType Directory $folder -Force|Out-Null
 $file=Join-Path $folder 'push-service-account.dpapi'
 if(Test-Path $file){Copy-Item -LiteralPath $file -Destination ($file+'.backup-'+(Get-Date -Format 'yyyyMMddHHmmss'))}
 [IO.File]::WriteAllBytes($file,[Security.Cryptography.ProtectedData]::Protect($raw,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
 Write-Output 'Saved encrypted Firebase credentials for the current Windows user. Source JSON was not deleted.'
}finally{[Array]::Clear($raw,0,$raw.Length);$config=$null}
