# TerminalLink

TerminalLink는 **Windows Terminal에서 진행 중인 작업을 PC와 Android에서 이어서 확인하고 입력하는 프로그램**입니다. PC에서 작업을 시작해 두고 휴대폰으로 상태를 확인하거나, PC의 대화 화면에서 같은 터미널 작업을 이어갈 수 있습니다.

TerminalLink가 AI를 실행하거나 대신 제공하는 것은 아닙니다. Windows Terminal 안에서 직접 실행한 Codex 등 기존 프로그램에 연결합니다. Codex 대화 원본을 식별할 수 있으면 질문과 답변을 대화 형태로 보여 줍니다.

## 무엇을 할 수 있나요?

- **대화와 터미널 보기:** 선택한 Windows Terminal 탭의 Codex 대화를 읽거나 터미널 화면으로 전환합니다.
- **메시지와 요청 답변:** 모바일 또는 PC에서 메시지를 보내고, 지원되는 Codex 질문과 선택지에 답합니다.
- **알림:** 답변이나 요청이 도착하면 모바일에서 확인하고 지원되는 알림에 답할 수 있습니다.
- **임시 파일함:** 휴대폰에서 PC로 파일을 올리거나 PC에서 공유한 파일을 휴대폰에 내려받습니다. 파일함 복사본은 24시간 후 만료되며 PC 연결 프로그램이 실행되면 정리됩니다. 원본과 이미 내려받은 파일은 유지됩니다.
- **기존 터미널 유지:** PC Desktop은 기존 Windows Terminal 창을 닫지 않고 숨겼다가 다시 보여 줍니다. Windows Terminal 안의 PowerShell과 SSH 작업을 그대로 사용합니다.

## 어떤 프로그램이 필요한가요?

1. **PC 연결 프로그램:** Windows Terminal과 휴대폰을 연결합니다.
2. **Android 앱:** 휴대폰에서 대화, 터미널, 요청과 파일함을 사용합니다.
3. **Desktop 앱:** PC에서 대화와 터미널을 볼 때 사용하는 선택 프로그램입니다. 소스는 별도 [`feature/desktop-app` 브랜치](https://github.com/Bakzzi/TerminalLink-Public/tree/feature/desktop-app/desktop)에 있습니다.

PC가 켜져 있고 휴대폰에서 PC에 접근할 수 있어야 합니다. 지원되는 환경에서 SSH로 접속한 원격 Codex 기록도 사용할 수 있지만 [별도의 읽기 연결 설정](docs/SSH.md)이 필요할 수 있습니다. iOS 앱은 아직 없습니다.

## 시작하기

현재는 사전 출시 소스입니다. 설치 파일을 GitHub Releases에서 제공하지 않으므로 직접 빌드해야 합니다. Windows 10/11 x64와 Android 11 이상을 대상으로 합니다.

최신 PC 연결 프로그램과 Android 소스는 다음 명령으로 내려받을 수 있습니다.

```powershell
git clone --branch main https://github.com/Bakzzi/TerminalLink-Public.git
cd TerminalLink-Public
```

1. [빌드 안내](docs/BUILD.md)에 따라 PC 프로그램과 Android APK를 만듭니다. PC에는 .NET SDK와 Android 빌드 도구가 필요합니다.
2. Windows Terminal에서 사용할 PowerShell 또는 SSH 작업과 Codex를 실행합니다.
3. PC 연결 프로그램을 실행하고 공유할 Windows Terminal 창을 선택해 연결 대기를 시작합니다.
4. 휴대폰에 APK를 설치하고 연결 설정에서 PC가 표시한 QR 코드를 읽은 뒤 PC에서 등록을 승인합니다.
5. 휴대폰에서 탭을 선택해 사용합니다. PC Desktop을 쓸 경우 별도 브랜치의 [Desktop 빌드 안내](https://github.com/Bakzzi/TerminalLink-Public/tree/feature/desktop-app/desktop)를 참고하세요.

같은 네트워크 밖에서는 PC에 도달할 수 있는 네트워크 경로가 필요합니다. 자세한 내용은 [연결 안내](docs/SETUP.md)를 확인하세요. 기본 빌드는 백그라운드 연결을 유지하며, 저전력 푸시 알림은 [사용자 자신의 Firebase 설정](docs/FIREBASE.md)이 필요합니다.

## 보안과 현재 제한

등록된 기기는 선택한 터미널에 명령을 입력할 수 있으므로 **본인이 신뢰하는 기기만 등록**하세요. 등록에는 PC 승인이 필요하며, 이후 연결은 기기별 인증서와 암호화된 통신을 사용합니다. 인증서, QR 코드, 개인 키, 대화 기록과 파일함 내용은 공유하지 마세요. 자세한 내용은 [보안 안내](SECURITY.md)를 참고하세요.

사전 출시 상태라 Codex 버전, 원격 환경과 터미널 상태에 따라 대화·요청을 읽거나 답하는 데 실패할 수 있습니다. 원본 대화를 확인하지 못하면 전체 대화가 정상적으로 읽힌 것처럼 취급하지 않습니다. PC가 꺼지면 연결할 수 없고, PC 재부팅 후 기존 작업이 자동 복원되는 기능은 없습니다.

## 소스와 라이선스

PC 연결 프로그램과 Android 소스는 이 브랜치에 있고, Desktop 소스는 위의 별도 브랜치에 있습니다. 이 프로젝트는 OpenAI의 공식 제품이 아닙니다. 소스는 [MIT 라이선스](LICENSE)로 제공하며, 외부 구성 요소의 라이선스는 [안내](THIRD_PARTY_NOTICES.md)를 따릅니다.

과거 릴리스 식별 기록의 소스 커밋은 비공개 개발 저장소의 기록을 가리킵니다. 이 공개용 저장소는 현재 소스에서 새로 시작했으므로 해당 커밋은 여기에 포함되지 않습니다.
