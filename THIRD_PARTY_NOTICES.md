# Third-party notices

TerminalLink's own code is MIT. Dependency licenses remain in effect. The full texts under `docs/licenses/` accompany source and PC distributions.

| Component | Version / use | License / upstream |
| --- | --- | --- |
| .NET runtime and Windows Desktop runtime | 10.0, self-contained PC | MIT and bundled notices: https://github.com/dotnet/runtime ; https://github.com/dotnet/wpf ; https://github.com/dotnet/winforms |
| Microsoft.Extensions.Logging.Abstractions / DependencyInjection.Abstractions | 8.0.3 / 8.0.2, transitive | MIT, `Microsoft-Extensions.txt` |
| SSH.NET | 2026.0.0 | MIT, `SSH.NET.txt`, https://github.com/sshnet/SSH.NET |
| QRCoder | 1.6.0 | MIT, `QRCoder.txt`, https://github.com/codebude/QRCoder |
| BouncyCastle.Cryptography | transitive SSH.NET dependency | MIT, `BouncyCastle.txt`, https://github.com/bcgit/bc-csharp |
| ZXing core | 3.5.3 | Apache-2.0, https://github.com/zxing/zxing |
| Firebase Messaging and Google/AndroidX support libraries | pinned top-level dependency in android/build.gradle; transitive versions in Gradle dependency report | Apache-2.0 notices supplied by the upstream artifacts, https://github.com/firebase/firebase-android-sdk ; https://source.android.com/docs/setup/about/licenses |
| Gradle wrapper | 8.13, build tooling | Apache-2.0, https://github.com/gradle/gradle |
| JSON-java | 20250517, JVM tests only | Public Domain, `JSON-java.txt`, https://github.com/stleary/JSON-java |

`Apache-2.0.txt` includes the full Apache license. `dotnet-runtime-THIRD-PARTY.txt` records runtime component attributions. `tools/collect-notices.py` also extracts dependency LICENSE/NOTICE entries from the resolved Android archives for a binary distribution; generated notices contain no local cache paths.

Build tools downloaded separately (Android SDK, JDK, .NET SDK) have their own terms and are not redistributed as part of the source checkout. Firebase service configuration and service-account credentials are not included.
