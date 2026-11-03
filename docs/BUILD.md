# 빌드

Windows 10/11 x64에서 PowerShell을 사용합니다. 소스 폴더 이름에 공백이 있어도 됩니다.

## 준비

- .NET SDK 10 (`dotnet --version`)
- JDK 17 이상. Android Studio에 포함된 JBR도 사용할 수 있습니다.
- Android SDK Platform 36, Build Tools 36.1.0
- Python 3: 합성 회귀 검사와 공개 자료 검사에 필요합니다.
- 첫 빌드에는 NuGet, Google Maven, Maven Central, Gradle 배포 서버에 접근할 인터넷이 필요합니다.

`JAVA_HOME`과 `ANDROID_HOME`을 설정하거나 아래 스크립트에 경로를 전달합니다. 기본 Android Studio 설치 위치도 자동 검색합니다. `local.properties`나 개인 설정 파일은 공유하지 않습니다.

```powershell
powershell -NoProfile -File tools/build-pc.ps1
powershell -NoProfile -File tools/build-android.ps1
# 직접 지정할 때:
powershell -NoProfile -File tools/build-android.ps1 -JavaHome 'D:\Tools\Java' -AndroidSdk 'D:\Tools\Android SDK'
```

결과는 `dist/Windows/TerminalLink-PC.exe`, `dist/Android/TerminalLink-<버전>.apk`와 APK ZIP입니다. PC 배포 폴더 전체를 복사하면 .NET 런타임을 별도 설치하지 않아도 실행할 수 있습니다. 외부 라이선스 파일도 함께 배포합니다.

Android 첫 빌드는 `signing/`에 **자기 배포용 서명 키**를 생성합니다. 이 폴더와 비밀번호 파일은 비공개로 백업하세요. 이후 업데이트도 같은 키로 서명해야 합니다. 다른 사람이 빌드한 APK로 기존 앱을 덮어쓸 수는 없습니다. 키·비밀번호는 Git과 APK ZIP에 들어가지 않습니다.

기존 키를 사용할 때는 `-KeyStore <p12 경로> -PasswordFile <비밀번호 파일 경로>`를 함께 전달합니다. 별칭은 `terminallink`입니다. 키가 일부만 남아 있으면 새 키를 만들지 않고 실패합니다.

Firebase는 기본적으로 꺼져 있습니다. [설정 안내](FIREBASE.md)를 완료한 뒤 `-Firebase`로 빌드합니다.

## 검사

```powershell
powershell -NoProfile -File tools/test.ps1
python tools/check-publication.py --history
```

첫 명령은 PC 테스트용 EXE를 별도 `build/tests`에 만들고 합성 데이터·루프백 TLS·실제 Java 로직의 OS 대역 검사를 수행합니다. 운영 프로그램을 교체하지 않습니다. 두 번째 명령은 추적 파일과 로컬 Git 기록의 공개 부적합 패턴을 검사합니다. 자동 검색만으로 모든 비밀 노출을 증명할 수는 없으므로 새 자료는 검토해야 합니다.

Gradle wrapper는 8.13을 사용하고 배포 ZIP의 SHA-256을 확인합니다. 빌드는 메모리 절약을 위해 순차 실행하는 것을 권장합니다. Android 에뮬레이터는 필요 없습니다.

GitHub의 Clean source verification workflow는 공백이 있는 새 checkout 경로에서 같은 검사를 수행합니다. CI의 임시 서명 키와 테스트 인증서는 외부 artifact로 업로드하지 않습니다. 스크립트 실행 정책이 막으면 내용을 확인한 뒤 해당 실행에만 `powershell -NoProfile -ExecutionPolicy Bypass -File <스크립트 경로>`를 사용할 수 있습니다. 시스템 실행 정책을 영구적으로 바꿀 필요는 없습니다.

