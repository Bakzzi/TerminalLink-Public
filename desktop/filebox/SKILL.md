---
name: terminallink-filebox
description: Publish explicitly requested files from this SSH server to the real TerminalLink PC and mobile temporary file box. Use when the user asks to put or upload a file in TerminalLink 파일함.
---

<!-- TerminalLink filebox managed v1 -->

The file box is on the paired Windows PC, not a folder in this server's project.
Only publish files the user requested. Never upload an entire project, secrets, or unrelated outputs automatically.

Run this command with the requested existing file paths (quote each path):

```sh
python3 ~/.local/share/terminallink-filebox-v1/publish.py '/absolute/path/result.zip'
```

The command snapshots the files, requests transfer over the PC's registered host-key-pinned SSH connection, and waits for the PC receipt. It does not need the PC password, a new listening port, or a public download URL. Original files remain unchanged. Shared copies expire after 24 hours from PC publication.

Only report **uploaded to TerminalLink file box** when the JSON result has `ok: true`, with file IDs, byte counts, SHA-256 hashes and expiry timestamps. Creating a local folder or obtaining `pending` is not upload success. If pending, explain that the PC bridge must be connected and check the SAME request, do not republish:

```sh
python3 ~/.local/share/terminallink-filebox-v1/publish.py --status REQUEST_ID --wait 120
```

Use `--cancel REQUEST_ID` only if the user cancels a pending upload; it stops future processing but cannot revoke an already published file. A timeout keeps the queued request for up to 24 hours. Requests are processed once; acknowledged copies are not republished after their expiry. Maximums match the existing file box: 32 files, 512 MiB per file, 2 GiB total. Compress directories to a file only if appropriate to the user's request. Do not call a remote folder named `파일함` a shared file box.
