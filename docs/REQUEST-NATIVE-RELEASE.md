# Request lifecycle integration — 2026-10-21

Release targets: connector **0.9.50**, Android **0.9.45 (93)**, isolated Desktop
branch **0.1.32**. Publish the connector before the clients. The Desktop request
client requires the updated connector's connection listener to be running.

## Active production path

`RemoteServer` owns one `NativeRequestService` and DPAPI request journal. `EventHub`
feeds complete, verified original-record request lists into its coordinator;
temporary unavailable reads mark connection state stale, not resolved. Notifications
retain the previous verified request state across a read miss. `CodexFeed` no longer
discards questions above 32 and carries the original turn ID. Older records without
a turn use a distinct `async-call:<call-id>` namespace, not a fabricated provider turn.

Desktop submits/queries through a current-user-only named pipe. Mobile popup and
notification actions use the existing certificate-authenticated connection. Both
reach the same coordinator and submission journal. A submission nonce and answer
hash are persisted before native dispatch; repeated/competing clients cannot send
the same pending request twice. The native adapter revalidates the original and
the console, protects existing PC drafts, navigates queued questions, and sends
Enter once. Only an exact original `send_user_message_question_reply` receipt with
the request ID and answer proves delivery. Enter success, disappearing screen text,
read failure and end-of-turn are not delivery receipts.

Unknown outcomes remain queryable by nonce, including after connector restart.
The notification service queries unresolved request replies when it reconnects.
Popup users can explicitly query without repeating input. Definitive pre-submit
rejection unlocks the draft. Completed submissions do not reactivate on replay.
Storage/identity failures remain visible and do not reset the submission journal.

Connector 0.9.50 also counts Desktop IPC request operations when deciding whether
an automatic update is safe. Admission and update pause use one lock, so a reply
cannot start between the idle check and shutdown. A disposed journal owner cannot
write after the next connector starts. Connector 0.9.49 was superseded before its
installation was confirmed; the client protocol and client artifacts are unchanged.

## Presentation

Both popups pin the opened question by ID, independently of list order. A new first
question cannot overwrite the one being read. Desktop lists multiple questions
before opening a popup; Android has `다른 질문`. Per-question selections/drafts survive
popup/tab switching during the app session. They are not promised to survive OS
process termination; the connector's submission state does. An externally ended
request stays labelled ended while open, preserving any written text.

Ordinary composer sends, original conversation acquisition, attachments, pairing,
SSH credentials, terminal visibility and the native permission dialog route are
unchanged. There are no per-tab title or private thread-ID exceptions.

## Boundaries and remaining work

This activates the design's **native original-record adapter**, not the experimental
App Server protocol adapter. Its original discovery and input still depend on the
existing native compatibility checks. Unsupported CLI layouts or unreadable original
records may therefore block a send, but must not erase a popup or claim delivery.
Native permission prompts still use the existing guarded route. Native protocol
question groups and provider approvals are not claimed migrated by this release.
The protocol activation gates in REQUEST-IMPLEMENTATION-STATUS.md remain open.

The single-frame full coordinator snapshot endpoint is not used by production
clients; before a future client uses it, add revision-bound pagination. Current
clients retain the existing chunked conversation/request projection and exchange
small individual submission/status results. No pending request is truncated to
fit the IPC frame. No new permanent mobile poll/socket is added.

## Validation and release

- `tools/test.ps1`: coordinator/persistence/IPC, original parser, native receipt and
  input, TLS, Android production popup and notification logic with OS stubs.
- Desktop `desktop/build.ps1 -Checks`: boundary, synthetic session and offscreen UI.
- Android signed Firebase owner build; client pin compared to the protected owner
  setting and previous generated owner configuration. Private configuration remains
  untracked. Exact source/artifact identities are in the release validation manifest.

Synthetic checks are not a physical Galaxy S25 or live SSH reply test. No test sends
input into a user's terminal. Idle update publication and observed installed versions
are reported separately; publication alone does not prove installation on a phone.
