param([string]$JavaHome='',[string]$Python='python')
. (Join-Path $PSScriptRoot 'common.ps1')
$root=Split-Path $PSScriptRoot -Parent
$env:PYTHONUTF8='1'
& (Join-Path $root 'tests/request-lifecycle/check.ps1')
& (Join-Path $root 'tests/request-records/check.ps1')
Invoke-Native $Python @((Join-Path $root 'tests/attachment-transfer/check_script.py'))
Invoke-Native $Python @((Join-Path $root 'tests/release-fixtures/remote-record/remote_record_test.py'))
$work=Join-Path $root 'build\tests';New-Item -ItemType Directory $work -Force|Out-Null
$pc=Join-Path $work 'pc'
& (Join-Path $PSScriptRoot 'build-pc.ps1') -Output $pc -TestHooks
foreach($arg in @('--public-checks','--self-test')){
 $p=Start-Process -FilePath (Join-Path $pc 'TerminalLink-PC.exe') -ArgumentList $arg -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(45000)){throw "Test timeout: $arg (isolated process $($p.Id)); no user process was stopped."}
 if($p.ExitCode -ne 0){throw "Test failed: $arg. Inspect build/tests/pc output."}
}
Get-Content (Join-Path $pc 'public-checks.txt'),(Join-Path $pc 'self-test.txt')
$java=Join-Path (Find-Java $JavaHome) 'bin'
$attachmentClasses=Join-Path $work 'attachments';New-Item -ItemType Directory $attachmentClasses -Force|Out-Null
Invoke-Native (Join-Path $java 'javac.exe') @('-J-Xmx128m','-encoding','UTF-8','-d',$attachmentClasses,(Join-Path $root 'android/src/com/terminallink/remote/AttachmentAccess.java'),(Join-Path $root 'tests/attachment-transfer/AttachmentAccessJvm.java'))
Invoke-Native (Join-Path $java 'java.exe') @('-Xmx128m','-cp',$attachmentClasses,'com.terminallink.remote.AttachmentAccessJvm')
$jar=Join-Path $work 'json-20250517.jar'
if(!(Test-Path $jar)){Invoke-WebRequest 'https://repo.maven.apache.org/maven2/org/json/json/20250517/json-20250517.jar' -OutFile $jar -UseBasicParsing}
if((Get-FileHash $jar -Algorithm SHA256).Hash -ne '3EA61B2A06E31EDF1C91134FE9106B0EBB16628BE169F3DB75BC7A2B06B45796'){throw 'JSON-java checksum mismatch'}
foreach($fixture in @(@('record-request-popup','PopupJvm'),@('notification-reply','NotificationReplyJvm'),@('attachment-send','AttachmentSendJvm'),@('filebox-upload','UploadJvm'),@('reading-recovery','ReadingJvm'))){
 $out=Join-Path $work ($fixture[0]+'-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $out|Out-Null
 Invoke-Native $Python @((Join-Path $root ('tests/release-fixtures/'+$fixture[0]+'/extract.py')),$root,$out)
 $files=@(Get-ChildItem $out -Recurse -Filter '*.java' | ForEach-Object {'"'+$_.FullName.Replace('\','/')+'"'})
 $list=Join-Path $out 'sources.txt';[IO.File]::WriteAllLines($list,$files,(New-Object Text.UTF8Encoding($false)))
 $classes=Join-Path $out 'classes';New-Item -ItemType Directory $classes|Out-Null
 Invoke-Native (Join-Path $java 'javac.exe') @('-J-Xmx128m','-encoding','UTF-8','-cp',$jar,'-d',$classes,('@'+$list))
 Invoke-Native (Join-Path $java 'java.exe') @('-Xmx128m','-cp',($classes+';'+$jar),('com.terminallink.remote.'+$fixture[1]))
}
Write-Output 'PASS isolated PC and Android JVM checks. No physical-device or live-session test performed.'
