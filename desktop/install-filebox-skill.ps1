param()
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'filebox\local-skill\SKILL.md'
$root=if($env:CODEX_HOME){$env:CODEX_HOME}else{Join-Path $env:USERPROFILE '.codex'}
$folder=Join-Path $root 'skills\terminallink-local-filebox'
$target=Join-Path $folder 'SKILL.md'
if(Test-Path -LiteralPath $target){
 $current=Get-Content -Raw -Encoding UTF8 -LiteralPath $target
 if(!$current.Contains('<!-- TerminalLink local filebox managed v1 -->')){throw 'An unrelated skill already uses this name; it was preserved'}
}
New-Item -ItemType Directory -Path $folder -Force|Out-Null
Copy-Item -LiteralPath $source -Destination $target -Force
if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw 'Installed skill hash mismatch'}
Write-Output 'Installed TerminalLink local file box guidance. Existing terminal sessions were not changed.'
