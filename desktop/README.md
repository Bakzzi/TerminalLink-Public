# TerminalLink Desktop

TerminalLink Desktop은 Windows Terminal에서 진행 중인 작업을 PC의 대화 화면과 터미널 화면에서 확인하고 입력하는 프로그램입니다. Codex가 실행 중인 탭은 원본 대화를 확인할 수 있을 때 질문과 답변으로 보여 주고, 지원되는 요청에는 화면에서 답할 수 있습니다.

Desktop을 열 때 기존 Windows Terminal 작업을 종료하지 않습니다. 창을 숨겼다가 **Windows Terminal 열기**로 다시 표시할 수 있습니다. 터미널 안에서 사용 중인 PowerShell이나 SSH 세션을 새 프로그램으로 바꾸지 않습니다.

휴대폰 연결은 별도의 PC 연결 프로그램이 담당합니다. Desktop의 **휴대폰 연동** 화면에서 해당 연결 프로그램을 사용할 수 있습니다. 파일함은 PC와 모바일이 같은 임시 보관함을 사용하며, 파일함의 복사본은 24시간 후 만료되고 PC 연결 프로그램이 실행되면 정리됩니다.

## 빌드와 실행

Windows 10/11 x64, .NET SDK 10과 Python 3이 필요합니다. 저장소 루트의 PowerShell에서 실행하세요.

Desktop의 소스 경계 검사는 Git 기록을 사용합니다. ZIP 파일이나 얕은 복제가 아닌 전체 Git 복제로 Desktop 브랜치를 내려받으세요.

```powershell
git clone --branch feature/desktop-app https://github.com/Bakzzi/TerminalLink-Public.git TerminalLink-Desktop
cd TerminalLink-Desktop
```

```powershell
powershell -NoProfile -File desktop/build.ps1
```

결과물은 `dist/Desktop/TerminalLink-Desktop.exe`입니다. 다른 PC에 배포할 때는 `dist/Desktop`의 라이선스 파일도 함께 포함하세요. 이 브랜치는 Desktop 개발용이며 모바일과 PC 연결 프로그램의 최신 코드는 별도 [`main` 브랜치](https://github.com/Bakzzi/TerminalLink-Public/tree/main)에 있습니다.

과거 릴리스 식별 기록의 소스 커밋은 비공개 개발 저장소의 기록을 가리키며, 이 공개용 저장소에는 포함되지 않습니다.

자동 검사를 함께 실행하려면 다음 명령을 사용합니다. 검사는 별도의 가상 데이터와 시험 프로세스로 실행됩니다.

```powershell
powershell -NoProfile -File desktop/build.ps1 -Checks
```

## 현재 제한과 보안

Desktop은 AI를 대신 제공하지 않습니다. Windows Terminal에서 사용자가 직접 Codex 등 프로그램을 실행해야 합니다. Codex 버전과 터미널·SSH 상태에 따라 대화나 요청을 식별하지 못할 수 있으며, 이때 표시된 일부 화면을 전체 대화로 취급하지 않습니다.

휴대폰을 등록하면 선택한 터미널에 원격으로 명령을 입력할 수 있으므로 신뢰하는 기기만 등록하세요. 개인 키, 등록 QR 코드, 대화 기록과 파일함의 내용은 공유하지 마세요. 프로젝트는 사전 출시 상태이며 OpenAI의 공식 제품이 아닙니다.
