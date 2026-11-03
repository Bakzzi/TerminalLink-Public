$ErrorActionPreference='Stop'
function Find-Java([string]$JavaHome='') {
 $candidates=@($JavaHome,$env:JAVA_HOME,(Join-Path $env:ProgramFiles 'Android\Android Studio\jbr'))
 foreach($candidate in $candidates){if($candidate -and (Test-Path (Join-Path $candidate 'bin\java.exe'))){return [IO.Path]::GetFullPath($candidate)}}
 $java=Get-Command java.exe -ErrorAction SilentlyContinue
 if($java){return Split-Path (Split-Path $java.Source -Parent) -Parent}
 throw 'Install JDK 17+ (Android Studio includes a JDK), then set JAVA_HOME.'
}
function Find-AndroidSdk([string]$AndroidSdk='') {
 foreach($candidate in @($AndroidSdk,$env:ANDROID_HOME,$env:ANDROID_SDK_ROOT,(Join-Path $env:LOCALAPPDATA 'Android\Sdk'))){if($candidate -and (Test-Path (Join-Path $candidate 'platforms\android-36\android.jar'))){return [IO.Path]::GetFullPath($candidate)}}
 throw 'Install Android SDK platform 36 and build-tools 36.1.0; set ANDROID_HOME or pass -AndroidSdk.'
}
function Invoke-Native([string]$File,[string[]]$Arguments) {
 $ErrorActionPreference='Continue'
 & $File @Arguments
 $code=$LASTEXITCODE
 $ErrorActionPreference='Stop'
 if($code -ne 0){throw "Command failed (exit $code): $File"}
}
function Read-Version([string]$Root){
 [xml]$m=Get-Content (Join-Path $Root 'android\AndroidManifest.xml') -Raw -Encoding UTF8
 return @{name=$m.manifest.GetAttribute('versionName','http://schemas.android.com/apk/res/android');code=[int]$m.manifest.GetAttribute('versionCode','http://schemas.android.com/apk/res/android')}
}
