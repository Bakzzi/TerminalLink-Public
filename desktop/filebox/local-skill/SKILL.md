---
name: terminallink-local-filebox
description: Publish requested Windows files into the TerminalLink PC/mobile temporary file box when the user says TerminalLink 파일함 or asks for 파일함 while working through TerminalLink. Use the installed publisher, not ChatGPT Pages or Library, unless the user explicitly names those destinations.
---

<!-- TerminalLink local filebox managed v1 -->

The shared file box used by TerminalLink Desktop and mobile is `%LOCALAPPDATA%\TerminalLink\shared-files`. A folder created in a project, a local path link, and a ChatGPT Pages/Library upload do not publish to this box.

For explicitly requested files on this Windows PC:

1. Locate the existing source files. Preserve them. Do not collect unrelated outputs or upload a whole project.
2. Write a UTF-8 JSON manifest in a task-local temporary folder: `{"files":["C:/absolute/path/result.png"]}`. Use a JSON serializer for actual paths.
3. Run the installed `%LOCALAPPDATA%\Programs\TerminalLink\TerminalLink-PC.exe` with `--publish-files <manifest.json> <result.json>`. Quote both paths. This command publishes and exits; it does not open the connector window, restart the server or touch the active terminal.
4. Read the result array. Each requested item must have `ok: true` and a `file` object containing `id`, `name`, `size`, `sha256`, `created`, and `expires`. Compare SHA-256 and size against both the source and `shared-files/<id>/content.bin`; confirm the expiry is 24 hours after creation. Report success only after this check. If a process is still running or the result is uncertain, inspect that same result before retrying to avoid duplicate uploads.
5. Tell the user the published filename. If the file box was already open, reopen it to refresh its current snapshot. Do not expose private local paths as if they were a mobile download link.

Only shared copies expire after 24 hours; originals remain. Limits: 32 files, 512 MiB per file, 2 GiB total. Use the publisher rather than manually writing metadata.

For a file that exists only on an SSH server, use the server's `terminallink-filebox` skill and its registered transfer bridge. Do not pass a remote Linux path to this Windows publisher or claim a remote folder is the shared file box. This skill does not authorize deleting a mistaken Pages upload or modifying another conversation.
