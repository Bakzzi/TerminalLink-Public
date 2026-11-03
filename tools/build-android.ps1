param([string]$OutputDirectory='', [string]$AndroidSdk='', [string]$JavaHome='', [switch]$Firebase, [string]$KeyStore='', [string]$PasswordFile='')
. (Join-Path $PSScriptRoot 'common.ps1')
$root=Split-Path $PSScriptRoot -Parent
$env:JAVA_HOME=Find-Java $JavaHome
$env:ANDROID_HOME=Find-AndroidSdk $AndroidSdk
$bin=Join-Path $env:JAVA_HOME 'bin';$bt=Join-Path $env:ANDROID_HOME 'build-tools\36.1.0'
if(!(Test-Path (Join-Path $bt 'apksigner.bat'))){throw 'Install Android SDK build-tools 36.1.0.'}
if($Firebase -and !(Test-Path (Join-Path $root 'android\google-services.json'))){throw 'Supply your own android/google-services.json before using -Firebase.'}
if([bool]$KeyStore -ne [bool]$PasswordFile){throw 'Pass both -KeyStore and -PasswordFile.'}
$custom=[bool]$KeyStore
if(!$custom){$KeyStore=Join-Path $root 'signing\release.p12';$PasswordFile=Join-Path $root 'signing\password.txt'}
$hasKey=Test-Path -LiteralPath $KeyStore;$hasPass=Test-Path -LiteralPath $PasswordFile
if($hasKey -ne $hasPass -or ($custom -and !$hasKey)){throw 'Signing files are incomplete. Restore the matching key/password; a replacement will not be generated.'}
if(!$hasKey){
 New-Item -ItemType Directory (Split-Path $KeyStore -Parent) -Force|Out-Null
 $bytes=New-Object byte[] 32;$rng=[Security.Cryptography.RandomNumberGenerator]::Create();try{$rng.GetBytes($bytes)}finally{$rng.Dispose()}
 [IO.File]::WriteAllText($PasswordFile,[Convert]::ToBase64String($bytes));[Array]::Clear($bytes,0,$bytes.Length)
 Invoke-Native (Join-Path $bin 'keytool.exe') @('-genkeypair','-keystore',$KeyStore,'-storetype','PKCS12','-storepass:file',$PasswordFile,'-alias','terminallink','-keyalg','RSA','-keysize','3072','-validity','10000','-dname','CN=TerminalLink Build')
 Write-Output 'Created an independent signing key. Back up signing/ privately for future updates.'
}
$firebaseValue=if($Firebase){'true'}else{'false'}
Invoke-Native (Join-Path $root 'android\gradlew.bat') @('-p',(Join-Path $root 'android'),'--no-daemon','--max-workers=1','--console=plain',"-PenableFirebase=$firebaseValue",'assembleRelease')
$v=Read-Version $root
if(!$OutputDirectory){$OutputDirectory=Join-Path $root 'dist\Android'}
New-Item -ItemType Directory $OutputDirectory -Force|Out-Null
$work=Join-Path $root 'android\build';New-Item -ItemType Directory $work -Force|Out-Null
$apk=Join-Path $OutputDirectory ("TerminalLink-"+$v.name+'.apk');$aligned=Join-Path $work 'aligned.apk'
Invoke-Native (Join-Path $bt 'zipalign.exe') @('-f','-p','4',(Join-Path $root 'android\gradle-build\outputs\apk\release\TerminalLink-release-unsigned.apk'),$aligned)
Invoke-Native (Join-Path $bt 'apksigner.bat') @('sign','--ks',$KeyStore,'--ks-pass',("file:"+$PasswordFile),'--ks-key-alias','terminallink','--out',$apk,$aligned)
Invoke-Native (Join-Path $bt 'apksigner.bat') @('verify','--verbose','--print-certs',$apk)
Invoke-Native (Join-Path $bt 'aapt2.exe') @('dump','badging',$apk)
Compress-Archive -LiteralPath $apk -DestinationPath (Join-Path $OutputDirectory ("TerminalLink-"+$v.name+'.zip')) -Force
Get-FileHash $apk -Algorithm SHA256
