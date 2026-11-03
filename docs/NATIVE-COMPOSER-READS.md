# Native composer pre-send reads

Connector 0.9.52 and Desktop 0.1.34 retry only the read preceding an ordinary
message send. An empty-composer check previously treated a cursor on a temporarily
blank or differently drawn row as "PC input in use" and immediately rejected it.

The native terminal may redraw between cursor metadata and buffer reads. The
pre-send reader now retries up to eight times, with 100 ms between attempts, while
rechecking the same process lifetime, shell state and request boundaries each time.
A ready first frame adds no delay. Actual text identified by the existing empty
composer check remains protected. Persistent unreadability is reported as inability
to confirm the empty input, rather than claiming the user is typing on the PC.
Only after the original empty-composer guard passes does the ordinary writer paste
once and press Enter once. This is not an automatic retry of a submitted message.

The shared helper is copied unchanged and hash-pinned into the isolated Desktop
branch. Original history matching, requests, attachments, native drafts and mobile
transport are not relaxed. No Android source/configuration or package change is
needed for this connector-side fix; the previously verified 0.9.46 package remains
the pending mobile release.

Six focused synthetic checks cover immediate readiness, blank repaint, stale cursor,
real text at the prompt, bounded persistent failure and ownership changes while
waiting. Existing ordinary-send tests still prove single paste/Enter and preservation
of native drafts. A read-only live inventory found empty composers and one sample
without a prompt on its cursor row; that is evidence of the ambiguous read state,
not a reproduced failed mobile send or proof of every reported tab failure.

Publication and installation are separate. Check installed executable version/hash
and updater health before claiming this behavior is active.
