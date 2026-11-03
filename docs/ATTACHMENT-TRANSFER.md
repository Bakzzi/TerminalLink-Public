# Attachment transfer

Connector 0.9.51 and Android 0.9.46 (94) allow general files on verified local
record-backed conversations and SSH conversations, as well as existing CLI records.
Desktop uses the same pinned attachment implementation on its isolated branch.

The former photo-only checks were incomplete feature gates, not a file-picker issue.
General files keep their existing 32 MiB per-file and four-total-attachments limits.
They are never converted into images or executed. Local files are referenced by
their PC storage path. SSH files are copied over the existing host-key-pinned SFTP
connection and referenced by their remote path in the selected existing Codex TUI.
Mixed sends attach images normally and include document references in one caption.

Original conversation, native process lifetime, empty draft and final draft checks
still apply to both types. Upload completion alone does not press Enter; identity
and file hashes are checked before native submission. A failed or uncertain send
does not automatically repeat input. This does not change ordinary message sends,
history discovery or request handling.

New SSH uploads use an owner-only `/tmp/terminallink-attachments-v2-<uid>` directory
so older image-only releases do not encounter new document names during cleanup.
Names are generated, extensions are limited to ASCII letters/digits, and display
names are sanitized separately. Files are mode 0600, directories mode 0700;
verification rejects symlinks, additional hard links, wrong owners, size/hash changes
and unexpected contents. Existing seven-day expiry and 512 MiB storage quota remain.
General file copies do not modify the selected original file. Older in-flight image
metadata and its storage directory remain understood.

The Android client requires advertised `localFileUpload`/`remoteFileUpload`
capabilities for these routes; older connectors show an update requirement while
keeping the selected files. Publish the connector before dependent clients.

Validation uses the actual remote Python metadata validator, C# native-delivery
callbacks with synthetic files/identities, Android capability tests, and existing
transport tests. These do not establish a live SSH transfer or physical-phone result.
