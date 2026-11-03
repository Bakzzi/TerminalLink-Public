# 선택 기능: Firebase 알림

기본 빌드에는 개발자의 Firebase 프로젝트를 넣지 않습니다. 계정 없이도 직접 연결 기능을 빌드할 수 있습니다. 기본 방식은 Android 연결 서비스를 유지하므로 전력 소모와 연결 알림이 있습니다.

저전력 푸시를 사용하려면:

1. 본인 Firebase 프로젝트를 만들고 Android 앱 `com.terminallink.remote`를 등록합니다.
2. 해당 프로젝트의 `google-services.json`을 `android/`에 로컬로 저장합니다. 이 파일은 Git에서 제외됩니다.
3. 같은 프로젝트의 FCM HTTP v1 사용에 필요한 서비스 계정 키를 발급합니다. 최소 권한을 부여하고 키를 공개하지 않습니다.
4. `tools/configure-push.ps1 -ServiceAccount <자신의 JSON 경로>`로 현재 Windows 사용자 저장소에 DPAPI로 암호화해 등록합니다. 원본 JSON은 자동 삭제하지 않으므로 별도로 안전하게 보관하거나 관리합니다.
5. `tools/build-android.ps1 -Firebase`로 빌드하고 설치합니다. PC가 이미 실행 중이었다면 자신의 작업 세션을 닫지 말고 PC 연결 프로그램만 정상적으로 다시 실행합니다.
6. Android 알림 권한을 허용하고 앱이 실제 푸시 등록 성공을 확인하는지 점검합니다. 등록에 실패하면 연결 유지 방식으로 남습니다.

서비스 계정과 Android 설정은 반드시 같은 프로젝트여야 합니다. FCM 지연·네트워크·기기 절전 정책에 따라 알림 전달 시점이 달라질 수 있습니다. Firebase 프로젝트 정책·할당량·서비스 계정 권한은 본인이 관리합니다.

공식 안내: https://firebase.google.com/docs/android/setup · https://firebase.google.com/docs/cloud-messaging/auth-server
