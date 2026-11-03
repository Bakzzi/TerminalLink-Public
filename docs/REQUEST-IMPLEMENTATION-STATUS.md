# Request coordinator implementation checkpoint — 2026-10-21

This records the protocol experiment and its remaining gates. The native original-record
adapter is now integrated for connector 0.9.50, Android 0.9.45 and Desktop 0.1.32.
See [active path and limitations](REQUEST-NATIVE-RELEASE.md).

## Implemented and checked

`host/Requests/` contains the shared contract, coordinator, protected journal,
same-user named-pipe server/client, authenticated-transport command dispatcher,
and an installed-version Codex request/receipt codec.

- Logical request identity separates instance/thread/turn/item from the JSON-RPC ID.
- Read loss, lifecycle and answer delivery have separate states. Partial snapshots,
  duplicate events, sequence gaps and old connection epochs do not delete requests.
- A completed request cannot be reopened by an old pending event. There is no
  32-request deletion limit in this new manager; the fixture keeps 71 open requests.
- A whole question group is validated before dispatch. Repeated text in different
  questions/threads does not merge their identities.
- Submission ID and content hash are saved before dispatch. Repeated same-ID calls
  return the saved state. Concurrent different-ID submissions cannot both write.
- Unknown outcomes survive restart and are not automatically resent. A transport
  write or `serverRequest/resolved` does not by itself accept an answer.
- The journal uses Windows current-user DPAPI, atomic replacement and one-writer
  locking. A storage failure blocks dispatch; an unreadable journal is not reset.
- IPC uses `CurrentUserOnly` on both endpoints, bounded frames and bounded concurrent
  handlers. Its dispatcher takes an authorized-instance resolver. It is not a new
  public listener and does not connect as a second paired mobile phone.
- Secret questions have a generic notification preview. Answer content is not
  retained in the submission journal; only its hash is retained there.

Run the focused suite:

```powershell
powershell -NoProfile -File tests/request-lifecycle/check.ps1
```

The initial checkpoint passed 43 synthetic checks without publishing an app.
The integrated native release adds exact-answer receipt and native IPC checks;
the 0.9.50 update-admission and disposed-journal checks bring the total to 50,
plus production native input, Android popup/notification and Desktop UI fixtures.
One existing nullable-reference warning remains in `ConversationWire.cs`.

## Installed Codex protocol experiment

The test uses **Codex CLI 0.163.0**, an empty per-run `CODEX_HOME`, a fake Responses
provider on loopback, an authenticated loopback App Server, and a real Codex TUI in
a test-owned ConPTY. No API key, paid model call, live thread or live terminal input
is required. The fixture shuts down only its own processes and removes its token.
Generated profiles and captures remain ignored under `build/`.

```powershell
python tests/request-protocol/probe.py --codex <absolute-path-to-codex.exe>
```

CLI SHA-256: `2692c8c7bb2cf8b0aefe638741fc90236f4b824c59578ce84994f9de58200993`.

Observed successfully:

1. Two questions arrive as one `item/tool/requestUserInput` request.
2. A second client and a late client receive the existing pending request.
3. Reconnecting a client preserves the logical thread/turn/item identity.
4. An answer from the second client resolves the request for the other clients.
5. The original TUI remains alive, accepts a new draft and submits another turn.
6. The verified original record contains the exact question-ID/answer map.
7. Interrupting a separate fixture turn also emits `serverRequest/resolved`.
   Therefore that notification alone cannot prove answer acceptance.

The previous live-daemon initialize failure was a transport error in the probe:
the control socket requires a WebSocket HTTP Upgrade handshake. A read-only
initialize through `app-server proxy` now succeeds. That live probe did not send
`thread/resume`, start a turn, answer a request or change thread ownership.

Reference: [official App Server transport documentation](https://learn.chatgpt.com/docs/app-server).

## Experimental protocol activation gates still open

**Do not activate the App Server protocol adapter just because its isolated test passes.**

- The experiment already knows the test thread ID. It does not prove automatic,
  stable identification of arbitrary pre-existing Windows Terminal tabs. The
  installed RPC schema does not expose a demonstrated TUI-process-to-thread mapping.
  Existing daemon display links still depend on screen evidence and are explicitly
  not authority to send. Reusing them as unquestioned submission authority would
  preserve the original defect and could select the wrong conversation.
- A TUI can switch/resume threads without replacing its process. PID/start-time
  identity alone is therefore insufficient. Observe a trustworthy thread binding
  and its invalidation before adapting any live tab.
- SSH version/capability, pending replay and reply behavior have not been checked
  in a separate remote fixture. Local 0.163.0 results cannot establish that behavior.
- This probe covers the native `request_user_input` protocol, not every orchestrator
  asynchronous question, permission/approval request or provider/version variant.
- A replay-end/complete-pending-snapshot boundary has not been established. The new
  manager must not be fed a partial replay as a complete snapshot.
- Native request integration now uses the common coordinator; see REQUEST-NATIVE-RELEASE.md.
- The current IPC snapshot is a bounded single frame. Before enabling it, implement
  revision-bound pagination for a snapshot larger than the frame limit; never drop
  outstanding requests to make the frame fit.

The next integration step is the binding/source adapter, followed by client
projection. Neither a new conversation nor a terminal screenshot may substitute
for those missing guarantees. Keep the last released connector/Desktop/Android
artifacts and the Desktop branch boundary intact.
