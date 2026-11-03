# Automatic updates

Available from connector **0.9.43** and Android **0.9.42 (90)**. Desktop **0.1.24** implements the same Windows publication contract on its separate branch.

## Scope and trust

Updates are published from a verified build on the owner's PC. This is not an anonymous GitHub download service. No GitHub token, signing key, pairing secret or internet-supplied executable path is embedded in the clients. Windows reads only its own install directory's `automatic-updates/release.json`. The directory inherits the Windows user's LocalAppData access control. A different OS account must not have write access. This protects against other users and remote callers, not malware already running as the same Windows user.

The publisher requires the intended executable SHA-256, clean committed source and exact source commit. Publish only after scoped tests and Git push have succeeded. It builds a digest-addressed ZIP containing the EXE and license files, then atomically publishes product, increasing four-component file version, ZIP size, SHA-256 and source commit. The consumer rejects other products, downgrades, malformed hashes, changed bytes, duplicate/traversal/unexpected ZIP entries, links/junctions and an EXE whose file version disagrees with the manifest. No shell command, arbitrary URL or installation destination comes from the manifest.

## Windows lifecycle

The running app checks the small local manifest at most every 15 seconds. There is no permanent updater process or internet polling. Build and test output directories never activate automatic updates.

Connector waits until its window is not being used, pairing dialogs are closed and **all mobile connections have ended** (normally the phone parks its connection when background push is available). It atomically stops accepting new connections before normal shutdown. Desktop waits until it is inactive, with no draft, selected attachment, request/dialog, tab operation or message send. An open request draft can deliberately postpone installation.

A temporary copy of the current executable validates the bundle, waits for the exact old PID/start-time/path and its workers to finish, replaces only the app and license files, and launches it again. It never kills Windows Terminal, PowerShell, SSH, Codex or the connector as a side effect of a Desktop update. Connector restores sharing, address, port and listening state; Desktop restores selected tab, view, window geometry and tray state. Registration, original conversations and credentials remain in their current storage.

Startup health is checked. If the replacement exits before confirmation, the saved previous EXE is restored and launched. A replacement still running without health confirmation is not forcibly killed; a diagnostic and previous EXE remain in `automatic-updates`. A failed attempted bundle is not installed repeatedly. A corrected release must have a newer version. `last-result.json` records result, version, hash and source commit. An empty `automatic-updates/disabled` file pauses automatic installation if recovery requires it.

Build first with the normal build tool. After validation and commit/push:

```powershell
./tools/publish-pc-update.ps1 -Product connector -BuildDirectory ./dist/Windows -ExpectedExeHash <SHA256> -SourceCommit <40-character-commit>
```

On the Desktop branch use `./desktop/publish-update.ps1 -Product desktop -BuildDirectory ./dist/Desktop ...`. The branches and install directories remain separate. The first version containing the updater must be installed once with the existing deployment procedure. Subsequent releases use publication, not forced process termination.

## Android lifecycle

When an authenticated connection exists, the service checks the PC's existing update catalog. It automatically downloads in the background, using the existing mutually authenticated TLS connection. It does not create an always-on socket, wake a parked phone for polling, or add a new server. A phone that is offline/parked learns of a release on its next connection. The downloader belongs to `LinkService`, so Activity recreation/focus changes do not create duplicate downloads. Partial downloads never install. Failed automatic downloads back off for an hour; manual retry remains available.

After complete SHA-256, package, increasing version and **same signing certificate** validation, the app asks Android's PackageInstaller for a self-update without user action (`UPDATE_PACKAGES_WITHOUT_USER_ACTION`, API 31+). It waits while the app is in use, a draft/attachment/request is pending, or a message/file/notification reply is being sent. No permission or security prompt is bypassed. If installation permission is missing, Android requires confirmation, or a device policy blocks the request, an update notification opens the permitted manual installation path. Cancellation/failure never causes an automatic installation loop. Existing manual update controls remain available.

The installer result is addressed to a non-exported receiver, bound to the exact installer session and a random nonce; it survives replacement of the app. `MY_PACKAGE_REPLACED` clears the update notice and resumes an enabled connection. Answer/request notification channels are unchanged. APKs are not submitted to Play Protect by tests.

The bootstrap APK still needs one installation using the old updater. Samsung-specific unattended installation behavior must be reported separately from builds and isolated tests.

Reference: [Android PackageInstaller self-update conditions](https://developer.android.com/reference/android/content/pm/PackageInstaller.SessionParams#setRequireUserAction(int)).

## Verification and retention

`tests/auto-update/check.ps1` builds tiny isolated old/new executables and checks product/version/hash rejection, ZIP traversal rejection, successful replacement, busy deferral and startup rollback. It never targets an installed product. Android synthetic checks exercise the actual service coordinator with stubs; APK signature verification and compilation use the normal build tool. Physical device results are separate.

Keep current and previous known-good releases, plus installed/pinned/recovery artifacts. Delete only measured obsolete generated outputs after push and successful deployment. Never remove user data, pairing configuration, attachments or signing keys.
