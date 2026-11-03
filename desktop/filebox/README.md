# SSH 원격 파일함 연결 모듈 0.1.0

원격 Codex의 출력 파일을 **기존 PC·모바일 공통 임시 파일함**에 실제로 등록한다. SSH 원격 폴더를 파일함인 것처럼 보여주지 않는다. Desktop 개발 브랜치의 독립 모듈이며 Android 및 PC 연결 프로그램의 코드, 등록 정보, 통신 형식을 변경하지 않는다.

## 동작

1. 사용자 요청을 받은 원격 에이전트가 설치된 `terminallink-filebox` 스킬의 명령을 실행한다. 요청한 파일만 개인 권한의 전송 대기 폴더로 복사하고 SHA-256을 기록한다. 일반 폴더를 감시하거나 모든 결과물을 자동 업로드하지 않는다.
2. 별도 `TerminalLink-FileBox.exe --run` 프로세스가 기존 등록 정보와 고정 SSH 호스트 키로 접속해 명시적인 요청만 받는다. 연결은 재사용하고, 유휴 대기 중 5초마다 작은 대기 목록만 확인한다. 별도 포트, 방화벽 변경, 원격에 PC 비밀번호 저장, TUI 입력이 없다.
3. 192 KiB 스트림 버퍼로 받은 내용을 검증하고 기존 `SharedFiles.Publish`로 PC 파일함에 등록한다. 등록된 사본의 크기·SHA-256·24시간 만료를 다시 확인한 뒤 원격에 결과를 기록한다. PC 및 모바일의 기존 파일함에서 동일한 파일을 내려받는다.
4. 원격 명령은 PC 결과를 확인한 경우에만 성공한다. 끊김·시간 초과는 `pending`이며 같은 요청 ID로 확인한다. 명령 대기 시간 초과가 요청 취소는 아니다. 재연결 시 남은 작업을 계속하며 요청 내용 변경, 만료, 해시 불일치를 거부한다. 부분 성공은 기록해 다시 올리지 않는다. 게시 직후 중단도 실제 파일함 메타데이터와 맞춰 복구한다.
5. 원본은 보존한다. 원격 전송 사본은 성공 확인 후 제거하고, 만료된 요청은 다음 업로드 시 안전하게 정리한다. PC 공유 사본은 기존 파일함의 24시간 정책을 따른다. PC가 오프라인이면 새 업로드는 완료되지 않으며 24시간 후 만료된다.

PC 수신 모듈은 Desktop 창과 독립적으로 작동하므로 창을 닫고 휴대폰만 사용할 때도 받을 수 있다. 명시적인 설치 명령에서 `-EnableOnLogin`을 지정하면 사용자 로그인 때 시작한다. 별도의 Windows 서비스나 관리자 권한은 필요하지 않다. PC 자체가 꺼져 있으면 전송되지 않는다.

## 빌드·설치

전제: Windows의 기존 TerminalLink SSH 읽기 프로필에 대상 서버와 고정 호스트 키가 등록돼 있어야 한다. 원격 서버에는 Python 3 및 SFTP가 필요하다. 프로필은 DPAPI로 보호된 기존 파일에서 읽기만 하며, 이 모듈의 설정에는 활성 프로필의 해시만 저장한다.

```powershell
powershell -NoProfile -File desktop/filebox/build.ps1
./dist/FileBoxBridge/TerminalLink-FileBox.exe --setup-remote
./dist/FileBoxBridge/TerminalLink-FileBox.exe --remote-check
powershell -NoProfile -File desktop/filebox/install.ps1 -ExpectedHash <SHA256> -EnableOnLogin -Launch
```

빌드는 배포하거나 접속하지 않는다. `--setup-remote`는 등록된 서버에 `~/.local/share/terminallink-filebox-v1/publish.py` 및 `~/.codex/skills/terminallink-filebox/SKILL.md`를 설치하고 해시를 확인한다. 관리 표시가 없는 기존 파일은 덮어쓰지 않는다. 실행 중인 Codex의 프롬프트나 작업을 변경하지 않는다. 이미 열린 에이전트가 새 스킬을 발견하지 못하면 위 스킬 파일을 읽도록 안내하거나 아래 명령을 직접 사용한다. 새 업로드 가능 여부를 확인하기 위해 기존 작업을 재시작할 필요는 없다.

```sh
python3 ~/.local/share/terminallink-filebox-v1/publish.py '/absolute/path/result.zip'
python3 ~/.local/share/terminallink-filebox-v1/publish.py --status REQUEST_ID --wait 120
```

원격 사용자는 PC 연결 비밀번호를 입력하지 않는다. 여러 탭은 같은 사용자 큐를 공유하며 파일함 제한은 기존과 같다: 최대 32개, 파일당 512 MiB, 전체 2 GiB. 보낸 파일 이름이 Windows에서 금지된 문자/장치명이면 안전한 이름으로 변환하며 결과에 실제 이름을 반환한다.

## 검증과 운영

`--check`는 합성 로컬 파일함으로 경로·용량·해시 거부, 24시간 보관, 재시도 및 중단 복구를 확인한다. `--remote-check`는 명시적으로 등록 서버의 격리 임시 경로에서 Python 검사를 하고, 전용 테스트 파일 하나를 실제 PC 파일함까지 전달한 뒤 기존 모바일 목록 프로토콜로 확인한다. 해당 검사 파일만 제거한다. 휴대폰 화면에서 다운로드한 증거와는 구분한다.

`%LOCALAPPDATA%/TerminalLinkDesktopPreview/filebox-bridge/`에 요청별 게시 기록과 마지막 연결 상태를 저장한다. 원격 자격 증명이나 대화는 기록하지 않는다. 실행 파일·라이선스·소스·의존성 lockfile로 구성하며 개인 프로필은 배포에 넣지 않는다.

수신 중지는 해당 설치본의 `--stop`으로 요청한다. 제거할 때는 자신의 시작프로그램 바로가기와 모듈만 제거한다. 사용자 원본, PC 파일함, 미처리 원격 요청, 기존 SSH 등록, 대화, Desktop 및 연결 프로그램은 제거 대상이 아니다. 원격 스킬/도우미 제거와 미처리 큐 삭제는 별도의 명시적인 정리 작업이다.
