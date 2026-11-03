# Dedicated file-box publisher. No command line arguments or arbitrary commands.
$ErrorActionPreference = 'Stop'
$inbox = Join-Path $env:TEMP 'TerminalLink-filebox-skill'
$request = Join-Path $inbox 'request.json'
$broker = Join-Path $env:LOCALAPPDATA 'TerminalLink\filebox-skill'
$publisher = Join-Path $env:LOCALAPPDATA 'Programs\TerminalLink\TerminalLink-PC.exe'
if (-not (Test-Path -LiteralPath $request -PathType Leaf)) { throw 'File-box request not found.' }
if ((Get-Item -LiteralPath $request -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked request rejected.' }
if ((Get-Item -LiteralPath $request).Length -gt 65536) { throw 'File-box request is too large.' }
if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) { throw 'TerminalLink publisher not found.' }
New-Item -ItemType Directory -Path $broker -Force | Out-Null
$item = Get-Item -LiteralPath $broker -Force
while ($item) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked broker path rejected.' }
    $item = $item.Parent
}
$lock = [IO.File]::Open((Join-Path $broker 'request.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $data = [IO.File]::ReadAllText($request, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $id = [string]$data.requestId
    if ($id -notmatch '^[0-9a-f]{32}$') { throw 'Invalid request ID.' }
    $files = @($data.files)
    if ($files.Count -lt 1 -or $files.Count -gt 32) { throw 'Invalid file count.' }
    foreach ($file in $files) {
        if ($file -isnot [string] -or $file -notmatch '^[A-Za-z]:\\' -or -not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'File path must name an existing local file.' }
    }
    $result = Join-Path $broker ("result-$id.json")
    if (Test-Path -LiteralPath $result) { throw 'Result already exists. Inspect it before retrying.' }
    $manifest = Join-Path $broker ("request-$id.json")
    if (Test-Path -LiteralPath $manifest) { throw 'Previous request may still be in progress. Inspect it before retrying.' }
    $json = @{files = $files} | ConvertTo-Json -Compress
    $out = [IO.File]::Open($manifest, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json); $out.Write($bytes,0,$bytes.Length); $out.Flush($true) } finally { $out.Dispose() }
    # WinExe programs may return from PowerShell's call operator before the
    # process exits. Wait for this exact child, never infer failure from timing.
    $args = @('--publish-files', ('"' + $manifest + '"'), ('"' + $result + '"'))
    $child = Start-Process -FilePath $publisher -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
    if ($child.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result -PathType Leaf)) { throw 'Publisher did not produce a result. Inspect before retrying.' }
    $items = @(Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json)
    if ($items.Count -ne $files.Count -or @($items | Where-Object { -not $_.ok }).Count -gt 0) { throw 'File-box registration failed. Inspect the result before retrying.' }
    Remove-Item -LiteralPath $manifest
    Write-Output "Published $($items.Count) file(s). Result: $result"
} finally { $lock.Dispose() }
