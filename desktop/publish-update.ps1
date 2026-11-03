param([Parameter(Mandatory=$true)][ValidateSet('desktop')][string]$Product,[Parameter(Mandatory=$true)][string]$BuildDirectory,[Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedExeHash,[Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{40}$')][string]$SourceCommit)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if((& git -C $repo rev-parse HEAD).Trim() -ne $SourceCommit){throw 'Source commit is not current HEAD'}
if((& git -C $repo status --porcelain --untracked-files=normal)){throw 'Commit the verified source before publishing'}
$exe=if($Product -eq 'connector'){'TerminalLink-PC.exe'}else{'TerminalLink-Desktop.exe'}
$dir=if($Product -eq 'connector'){'TerminalLink'}else{'TerminalLinkDesktopPreview'}
$build=(Resolve-Path -LiteralPath $BuildDirectory).Path
$source=Join-Path $build $exe
if((Get-FileHash -LiteralPath $source).Hash -ine $ExpectedExeHash){throw 'Built executable hash mismatch'}
$version=[version](Get-Item -LiteralPath $source).VersionInfo.FileVersion
$install=Join-Path $env:LOCALAPPDATA ('Programs\'+$dir)
$destination=Join-Path $install $exe
if(!(Test-Path -LiteralPath $destination) -or $version -le [version](Get-Item -LiteralPath $destination).VersionInfo.FileVersion){throw 'Publish requires an installed older version'}
function Assert-NoLinks([string]$path){$item=Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue;if($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked update path rejected'};$parent=[IO.DirectoryInfo](Split-Path $path -Parent);while($parent){if($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked parent rejected'};$parent=$parent.Parent}}
$updates=Join-Path $install 'automatic-updates';Assert-NoLinks (Join-Path $updates 'release.json');New-Item -ItemType Directory -Path $updates -Force|Out-Null
$zip=Join-Path $updates ([guid]::NewGuid().ToString('N')+'.partial')
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try{
 $paths=@($exe,'LICENSE','THIRD_PARTY_NOTICES.md')+@(Get-ChildItem -LiteralPath (Join-Path $build 'licenses') -File|ForEach-Object {'licenses/'+$_.Name})
 foreach($relative in $paths){$path=Join-Path $build $relative;Assert-NoLinks $path;[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$path,$relative,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
}finally{$archive.Dispose()}
$verify=[IO.Compression.ZipFile]::OpenRead($zip)
try{$stream=$verify.GetEntry($exe).Open();$digest=[Security.Cryptography.SHA256]::Create();try{$packagedHash=[BitConverter]::ToString($digest.ComputeHash($stream)).Replace('-','');if($packagedHash -ine $ExpectedExeHash){throw 'Packaged executable differs from the verified artifact'}}finally{$stream.Dispose();$digest.Dispose()}}finally{$verify.Dispose()}
$hash=(Get-FileHash -LiteralPath $zip).Hash;$size=(Get-Item -LiteralPath $zip).Length
$bundle=Join-Path $updates ($hash+'.zip');Assert-NoLinks $bundle
Move-Item -LiteralPath $zip -Destination $bundle -Force
$manifest=@{product=$Product;version=$version.ToString();sha256=$hash;size=$size;commit=$SourceCommit}|ConvertTo-Json
$catalog=Join-Path $updates 'release.json';$pending=$catalog+'.tmp';Assert-NoLinks $pending
[IO.File]::WriteAllText($pending,$manifest,(New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath $pending -Destination $catalog -Force
Write-Output "Published $Product $version. The running app applies it automatically when idle. SHA256 $hash"
