# TerminalLink engineering rules

- Preserve running Windows Terminal, PowerShell, SSH and Codex sessions. Never terminate or inject test input into user work.
- Windows Terminal means the tabbed WindowsTerminal.exe host. Launch shells inside that host; do not silently substitute conhost, an embedded terminal, or a Codex desktop forwarding path.
- Apply behavior across existing and newly created tabs and windows. Do not special-case private titles, process IDs or conversation IDs.
- Original conversation identity requires actual ownership and unambiguous evidence. Fail closed for requests and attachments. Per the user's 2026-10-16 correction, Desktop ordinary text targets the selected existing terminal and must not require a history match: verify the native session/process lifetime and input state instead. Do not replace unreadable original history with visible screen text while claiming a complete conversation.
- Original messages remain unchanged. Display transformations must not affect identity, storage or request handling.
- Temporary request read failures preserve an open popup, selection and draft; revalidate identity before submission. Completion must be confirmed by the same original record.
- Keep per-user secrets outside source. Never commit pairing QR codes, keystores, service accounts, conversation dumps, local paths or private regression captures.
- Before changes choose the smallest sufficient validation and stop rule. Use synthetic fixtures and isolated loopback identities; no emulator or live-terminal tests by default.
- `tools/test.ps1` covers portable parser, request/notification logic and loopback TLS. Build both applications for shared transport changes. Report physical-device checks separately.
- Build scripts create artifacts only. Installation, production process replacement and publication are separate actions; do not terminate all matching process names.
- New dependencies must have pinned versions, source provenance and license notices. Generated output stays under ignored build/dist/signing directories.

## Isolated desktop app development

- This worktree is `feature/desktop-app`. The baseline is `17b362ea16587fd630892c450fd784317fabab72`. Keep the original worktree on `main`.
- Read `desktop/AGENTS.md` and `desktop/README.md` before desktop work. New PC client code, its tools, and synthetic tests belong under `desktop/`.
- Preserve Android, shared tools and production workflows at the baseline. The desktop links existing host sources directly; the only host adapters are `#if DESKTOP` blocks in `LocalDaemonFeed.cs` (private storage and verified incremental reads), `NotificationInput.cs` (verified ordinary message submission), and `LocalImageTarget.cs` (bounded read-only retries before input). The boundary checker must prove the non-desktop host source remains identical apart from equivalent braces. Do not merge this branch into main as part of ordinary development.
- Branch isolation does not isolate runtime data. Use a separate executable, output directory and application data namespace; never reuse the installed connector's identity/settings or mobile registration.
- Do not install over, stop or restart the existing PC connector while developing the desktop app. Do not publish Android updates from this worktree.
- Run `python desktop/check-boundary.py` before desktop builds and commits. It verifies source boundaries only, not runtime safety or functionality.
