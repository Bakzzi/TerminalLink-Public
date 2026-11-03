# PC connection window

PC connector 0.9.40 uses the same palette as TerminalLink Desktop: background `#151719`, surfaces `#101214`, input `#232629`, primary buttons `#397B74`, and the mint `>_<` mark `#73D8CE`. `host/ConnectorTheme.cs` contains the WinForms presentation components; the connector does not depend on the separate desktop branch or executable.

The main window groups terminal sharing, phone connection and connection logs. QR registration and confirmation/error dialogs share the palette. The QR itself retains a white background for scanning. Existing endpoint/port values, terminal selection, automatic sharing, server start/stop, registration, revocation and approval handlers remain in `MainForm`.

The shared client-drawn title bar provides minimize, maximize/restore and close buttons, dragging and edge resizing. Windows still draws the outside border using [documented DWM colors](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) on Windows 11. Dialogs hide unavailable caption actions. Confirmation dialogs default to cancel. Closing a dialog or pressing Escape cannot authorize registration or revocation. The one-use pairing window closes even if its UI throws.

## Scoped validation

```powershell
powershell -NoProfile -File tools/build-pc.ps1
./dist/Windows/TerminalLink-PC.exe --theme-preview ./build/connector-theme
```

The explicit preview command renders only synthetic windows and an invalid example QR. It does not construct `Identity`, discover live terminals, start network listeners, access registration files or send input. It writes main/minimum-size/QR/confirmation PNGs plus a check report. Check cancellation and explicit-approval semantics, inspect the generated images, and verify the installed executable matches the validated artifact. Presentation-only changes do not need an Android build or emulator.
