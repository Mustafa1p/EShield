# Project conventions

- Build on Windows x64 with the .NET 9 SDK and MSVC C++ build tools.
- Use `tools/build.ps1` and `tools/test.ps1` for build and verification.
- Launch each completed application update for the user to test, unless they explicitly ask otherwise. Use the newly built executable with `--replace` so only one instance remains.
- All public screenshots must use the synthetic fixtures in `tests/EShield.Tests/DemoScreenshots.cs`. Generate them with `tools/capture-screenshots.ps1`.
- Never publish real desktop screenshots, live connection lists, user profile paths, personal IPs/domains, account details, environment files, settings, databases, or diagnostic logs.
- Keep a visible demo-data label in each public screenshot. Add or refresh a screenshot when a visible feature changes.
- Public examples must use fictional apps, `example.com`/`example.net`/`example.org` domains, documentation IP ranges, and `C:\ExampleApps\...` paths.
- Preserve the distinction between app rules and app-scoped destination rules. Do not broaden an IP/domain block to a whole-app block.
- Tests must use isolated state directories and a separate firewall-rule prefix. Never modify the user's actual rules while testing.
- Do not commit build output, WebView2 profiles, PDB files, or downloaded MMDB databases. Keep attribution and download scripts in source control.
- Describe product limits accurately: no decrypted HTTPS URLs, incomplete DoH hostname visibility, shared-IP effects, and retry-after-approval semantics.
