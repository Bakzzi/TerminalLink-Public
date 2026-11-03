# Security and privacy

TerminalLink grants remote input access to selected terminal sessions. Only pair devices you control. A paired device can run commands with the privileges of the PC terminal user.

## Storage and transport

- Each PC and Android installation creates its own keys. There is no shared developer credential.
- Enrollment uses a short-lived random token plus explicit PC approval. Subsequent connections use pinned certificates and mutual TLS.
- PC identity, SSH read credentials and binding metadata are protected with Windows DPAPI for the current user. Android keys use Android Keystore.
- DPAPI is not protection against malware running as the same Windows user. Protect backups and the Windows account.
- Conversation records, uploads, shared files and application state can contain private text. Do not publish the per-user application data directory.
- Optional Firebase uses your own project and service account. The PC encrypts event payloads for the paired device; Google still processes delivery metadata.
- APK signing keys identify a publisher, not a device. Never share private signing keys, pairing QR codes, Firebase admin keys or SSH credentials in issues.

## Source distribution

Builds exclude runtime data, private credentials, local SDK settings and developer captures. `tools/check-publication.py --history` checks tracked files and local Git history, but pattern scanning is not a security audit or proof that every possible secret is absent.

Dependencies have fixed version numbers. The Gradle distribution and the test JSON JAR are checksum-pinned. Restore/build still trusts the configured official package registries; the project is not a fully hermetic build.

Keep network access limited to your own devices where possible. Avoid running terminals as administrator. Do not disable the firewall globally. Unregister lost devices in the PC application.

## Reporting

Use GitHub's private vulnerability reporting for this repository if available. Otherwise contact the maintainer without including exploit credentials or private data in a public issue. Redact identifiers and provide synthetic reproduction steps. This pre-release project has not undergone an independent security audit.
