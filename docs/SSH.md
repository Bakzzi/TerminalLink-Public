# 원격 SSH의 Codex 대화

Windows Terminal 안에서 SSH로 실행하는 Codex를 제어하는 방식은 유지됩니다. 원격 PC의 전체 대화 기록은 콘솔 화면과 별도이므로 읽기 연결 등록이 추가로 필요합니다. 기존 SSH 터미널에 등록 명령을 자동 입력하지 않습니다.

지원 조건: Linux 서버, Python 3, 동일 사용자 계정의 Codex 기록, 비밀번호 SSH 인증, 검증된 서버 호스트 키. 현재 helper는 IP 주소를 받으며 키 기반 로그인만 가능한 서버는 지원하지 않습니다.

1. 서버 관리자나 신뢰할 수 있는 별도 경로에서 서버의 호스트 키 종류와 SHA256 fingerprint를 확인합니다. 예: 서버에서 `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub -E sha256`를 실행해 확인합니다. 미확인 네트워크 검색 결과를 그대로 신뢰하지 마세요.
2. PC에서 아래 스크립트를 실행하고 비밀번호는 숨김 입력창에 직접 입력합니다.

```powershell
powershell -NoProfile -File tools/configure-ssh-read.ps1 -PcExe '.\dist\Windows\TerminalLink-PC.exe' -Address '<서버 IP>' -UserName '<본인 계정>' -HostKeyType 'ssh-ed25519' -HostKeySha256 'SHA256:<검증한 fingerprint>'
```

프로필은 연결 및 호스트 키 검증에 성공한 경우에만 현재 사용자의 DPAPI 저장소에 저장됩니다. 비밀번호는 명령행 인수나 Git에 저장하지 않습니다. 터미널과 읽기 연결이 같은 원격 사용자/호스트를 가리켜야 합니다.

현재 대상 프로세스와 원본 기록을 유일하게 확인하지 못하면 대화 읽기/입력이 실패할 수 있습니다. 원격 사용자 홈 전체를 공개하거나 서버 인증을 완화하는 해결책을 사용하지 않습니다.
