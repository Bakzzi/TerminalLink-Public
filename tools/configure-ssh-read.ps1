param([Parameter(Mandatory=$true)][string]$PcExe,[Parameter(Mandatory=$true)][string]$Address,[int]$Port=22,[Parameter(Mandatory=$true)][string]$UserName,[Parameter(Mandatory=$true)][string]$HostKeyType,[Parameter(Mandatory=$true)][string]$HostKeySha256)
$ErrorActionPreference='Stop'
$parsed=$null
if(![Net.IPAddress]::TryParse($Address,[ref]$parsed) -or $Port -lt 1 -or $Port -gt 65535){throw 'Use a valid server IP and port.'}
$fingerprint=$HostKeySha256 -replace '^SHA256:','';$fingerprint=$fingerprint.TrimEnd('=')
if($fingerprint -notmatch '^[A-Za-z0-9+/]{43}$'){throw 'Use a verified SHA256 host fingerprint.'}
$secure=Read-Host 'SSH password (hidden)' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try{
 $profile=@{host=$Address;port=$Port;user=$UserName;password=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer);hostKey=$fingerprint;hostKeyType=$HostKeyType}
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=(Resolve-Path -LiteralPath $PcExe).Path;$start.Arguments='--register-ssh-read';$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardError=$true;$start.StandardInputEncoding=New-Object Text.UTF8Encoding($false)
 $process=[Diagnostics.Process]::Start($start)
 $process.StandardInput.Write(($profile|ConvertTo-Json -Compress));$process.StandardInput.Close()
 $err=$process.StandardError.ReadToEnd();$process.WaitForExit()
 if($process.ExitCode -ne 0){throw 'SSH registration failed. Check credentials, connectivity and trusted host fingerprint; no success is assumed.'}
 Write-Output 'SSH read connection verified and registered for this Windows user.'
}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$profile=$null;$secure.Dispose()}
