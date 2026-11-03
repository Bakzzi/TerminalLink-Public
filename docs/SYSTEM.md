# 데이터 흐름과 경계

`Windows Terminal → PC 연결 프로그램 ↔ TLS ↔ Android`

Windows Terminal이 PowerShell/SSH/Codex를 호스팅합니다. PC는 선택된 창의 콘솔 상태와 입력 대상을 확인합니다. 터미널 화면은 현재 콘솔 표시 상태이며, 대화 화면은 별도의 원본 기록 연결 결과입니다.

- `TerminalWindows`, `TerminalTabLocator`, `ConsoleBridge`, `TextRemote`: 창·탭·콘솔 식별과 터미널 입출력.
- `CodexFeed`, `LocalDaemonFeed`, `LocalBindingStore`, `RemoteCodexFeed`: 소유권과 질문/답변 증거를 대조한 원본 연결. SSH는 등록된 호스트 키를 확인하는 별도 연결을 사용합니다.
- 원격 화면에서 질문이 위로 사라졌으면 명확히 구분된 답변 본문을 같은 원본 메시지 안에서 순서대로 대조합니다. 현재 SSH 연결·TUI·작업 디렉터리·열린 CLI 기록 조건을 통과하고, 충분한 본문 증거에 일치하는 원본이 하나일 때만 연결합니다. 접기 안내, 작업 로그, 미전송 입력은 원본 식별 근거로 쓰지 않습니다.
- `ConversationReader`, `ConversationWire`, `DisplayText`: 읽기 상태, 증분 전송, 표시 정리. 질문 답장 포맷과 내부 인용은 표시 경계에서만 정리합니다. 원본 메시지를 수정하지 않습니다.
- `NotificationTarget`, `NotificationInput`: 입력 시 실제 대상·요청을 다시 확인합니다. 읽을 수 있다는 것만으로 입력 권한을 추정하지 않습니다.
- Android `ActionControls`, `RecordRequest`: 일시적인 읽기 실패와 요청 완료를 구별합니다. 실패 중에는 기존 내용을 보존하고 제출 전 재확인합니다.
- Android `LinkService`, `NotificationReplies`: 탭별 답변/요청 알림, 읽음 및 전송 결과 처리.
- `EventHub.repliedThrough`, Android `AnswerReceipts`: PC·모바일 어느 쪽에서든 원본에 기록된 사용자 답장 순번을 직접 연결과 암호화된 푸시로 전달합니다. 같은 PC·원본 대화에서 그 순번 이전의 답변 알림만 지우며, 이후 답변과 다른 대화는 유지합니다. 순번을 앱 내부에 저장하므로 재접속·늦은 푸시로 처리한 알림이 다시 생기지 않습니다. 입력 버튼 클릭이나 전송 실패만으로는 처리하지 않으며, 요청 알림의 완료 판정은 별도로 유지합니다. 휴대폰이 오프라인이거나 운영체제가 푸시를 지연하면 다음 수신·재연결 때 반영합니다.
- `Security`, `Protocol`, `Server`: 기기별 인증서, 일회 등록, mTLS, 메시지 경계.
- `PushDelivery`, Android `PushClient`: 선택적인 FCM. Google에는 전달 주소와 암호화된 이벤트 payload를 전송합니다. FCM 전송 메타데이터까지 숨기는 구조는 아닙니다.
- `SharedFiles`: 사용자가 지정한 파일만 임시 파일함에 복사하고 만료 시 복사본을 정리합니다. PC 원본을 삭제하는 기능이 아닙니다.

앱 마크는 `>_<`입니다. Android 홈 아이콘은 남색 바탕과 민트색 선을, 알림·빠른 설정은 같은 모양의 흰색 단색 선을 사용합니다. 아이콘 변경에 대화·연결 로직 변경을 섞지 않습니다.

원본 식별·상태·표시 규칙은 모든 탭에 공통입니다. 화면에 보이는 몇 줄로 전체 대화를 대신하지 않으며, 기록이 모호하면 입력을 차단합니다. 기록 개수의 임의 상한과 전송 청크 크기/알림 미리보기 길이는 다른 개념입니다.

프로젝트는 사전 출시 상태입니다. Codex의 내부 기록 형식과 daemon 동작에 의존하는 부분은 호환성 변화의 영향을 받습니다. 원격 기능에는 Linux의 Python 3와 지원되는 Codex 기록이 필요합니다. SSH 읽기 인증은 현재 비밀번호 기반이며, 키 기반 인증을 지원한다고 가정하면 안 됩니다.
