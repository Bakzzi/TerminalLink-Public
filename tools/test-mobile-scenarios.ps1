param([string]$JavaHome='',[string]$Python='python')
. (Join-Path $PSScriptRoot 'common.ps1')
$root=Split-Path $PSScriptRoot -Parent
$work=Join-Path $root 'build/mobile-scenarios'
$jar=Join-Path $root 'build/tests/json-20250517.jar'
if(!(Test-Path -LiteralPath $jar) -or (Get-FileHash -LiteralPath $jar).Hash -ne '3EA61B2A06E31EDF1C91134FE9106B0EBB16628BE169F3DB75BC7A2B06B45796'){throw 'Run tools/test.ps1 to obtain the verified JSON test dependency'}
$java=Join-Path (Find-Java $JavaHome) 'bin'
foreach($fixture in @(@('record-request-popup','PopupJvm'),@('notification-reply','NotificationReplyJvm'),@('attachment-send','AttachmentSendJvm'),@('filebox-upload','UploadJvm'),@('reading-recovery','ReadingJvm'))){
 $out=Join-Path $work $fixture[0];New-Item -ItemType Directory -Path $out -Force|Out-Null
 Invoke-Native $Python @((Join-Path $root ('tests/release-fixtures/'+$fixture[0]+'/extract.py')),$root,$out)
 $files=@(Get-ChildItem -LiteralPath $out -Recurse -Filter '*.java'|ForEach-Object {'"'+$_.FullName.Replace('\','/')+'"'})
 $list=Join-Path $out 'sources.txt';[IO.File]::WriteAllLines($list,$files,(New-Object Text.UTF8Encoding($false)))
 $classes=Join-Path $out 'classes';New-Item -ItemType Directory -Path $classes -Force|Out-Null
 Invoke-Native (Join-Path $java 'javac.exe') @('-J-Xmx128m','-encoding','UTF-8','-cp',$jar,'-d',$classes,('@'+$list))
 foreach($entry in $fixture[1..($fixture.Length-1)]){Invoke-Native (Join-Path $java 'java.exe') @('-Xmx128m','-cp',($classes+';'+$jar),('com.terminallink.remote.'+$entry))}
}
$attachments=Join-Path $work 'attachments';New-Item -ItemType Directory -Path $attachments -Force|Out-Null
Invoke-Native (Join-Path $java 'javac.exe') @('-J-Xmx128m','-encoding','UTF-8','-d',$attachments,(Join-Path $root 'android/src/com/terminallink/remote/AttachmentAccess.java'),(Join-Path $root 'tests/attachment-transfer/AttachmentAccessJvm.java'))
Invoke-Native (Join-Path $java 'java.exe') @('-Xmx128m','-cp',$attachments,'com.terminallink.remote.AttachmentAccessJvm')
