$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture = Join-Path $root 'build\filebox-skill-test'
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
$run = Join-Path $fixture ([guid]::NewGuid().ToString('N'))
$temp = Join-Path $run 'temp'; $local = Join-Path $run 'local'
$inbox = Join-Path $temp 'TerminalLink-filebox-skill'
$program = Join-Path $local 'Programs\TerminalLink'
New-Item -ItemType Directory -Force -Path $inbox,$program | Out-Null
$publisher = Join-Path $program 'TerminalLink-PC.exe'
Add-Type -OutputAssembly $publisher -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
public static class FakePublisher {
 public static int Main(string[] args) {
  if(args.Length!=3 || args[0]!="--publish-files") return 2;
  if(!File.ReadAllText(args[1]).Contains("fixture.txt")) return 3;
  System.Threading.Thread.Sleep(600);
  File.WriteAllText(args[2],"[{\"ok\":true,\"file\":{\"name\":\"fixture.txt\"}}]");
  return 0;
 }
}
'@
$source = Join-Path $run 'fixture.txt'; [IO.File]::WriteAllText($source,'fixture')
$script = Join-Path $root 'tools\filebox-skill-publish.ps1'
$env:TEMP=$temp; $env:LOCALAPPDATA=$local
$id = [guid]::NewGuid().ToString('N')
$request = Join-Path $inbox 'request.json'
[IO.File]::WriteAllText($request,(@{requestId=$id;files=@($source)} | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $local "TerminalLink\filebox-skill\result-$id.json"))) { throw 'Synthetic publisher failed.' }
Write-Output 'PASS waits for delayed publisher and its result path'
$ErrorActionPreference='Continue'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script 2>&1 | Out-Null
$ErrorActionPreference='Stop'
if ($LASTEXITCODE -eq 0) { throw 'Duplicate request was not rejected.' }
Write-Output 'PASS retry of a completed request does not publish a duplicate'
$id = [guid]::NewGuid().ToString('N')
[IO.File]::WriteAllText($request,(@{requestId=$id;files=@('C:\not-a-real-file-for-filebox-test')} | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
$ErrorActionPreference='Continue'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script 2>&1 | Out-Null
$ErrorActionPreference='Stop'
if ($LASTEXITCODE -eq 0) { throw 'Missing file was accepted.' }
Write-Output 'PASS invalid file is rejected before publication'
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$installed = Join-Path $env:USERPROFILE '.codex\skills\terminallink-local-filebox\scripts\publish.ps1'
$command = "& '$ps' -NoProfile -ExecutionPolicy Bypass -File '$installed'"
$pattern = @($ps,'-Command',$command) | ConvertTo-Json -Compress
$rule = Join-Path $run 'candidate.rules'
[IO.File]::WriteAllText($rule,('prefix_rule(pattern='+$pattern+', decision="allow")'),[Text.UTF8Encoding]::new($false))
$decision = (& codex.cmd execpolicy check --rules $rule -- $ps '-Command' $command | Out-String) | ConvertFrom-Json
$other = (& codex.cmd execpolicy check --rules $rule -- $ps '-Command' ($command+'; Get-Process') | Out-String) | ConvertFrom-Json
if ($decision.decision -ne 'allow' -or @($other.matchedRules).Count -ne 0) { throw 'Rule matches unintended PowerShell command.' }
Write-Output 'PASS exception matches only the fixed file-box command, not additional commands'
