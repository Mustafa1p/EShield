# Contributing

Use the build and test commands in [Readme.md](Readme.md). Keep changes focused and describe the user-visible behavior and relevant validation in pull requests.

## Rules and filtering

- Preserve app identity and destination scope all the way from the UI to the native filter conditions.
- Test explicit-block priority, default-deny exceptions, and removal behavior when changing policy code.
- Use isolated state directories and the `[EShield-Test] Rule ` prefix for persistent-rule tests.
- Never clear or replace another user's firewall rules during tests.
- Report unsupported capture or filtering behavior accurately in the UI and documentation.

## Public screenshots

Run `pwsh -File tools/capture-screenshots.ps1` and inspect the resulting images. The fixtures in `tests/EShield.Tests/DemoScreenshots.cs` are the only source for public screenshots.

Use fictional apps, example domains, documentation IP ranges, and generic example paths. Keep a visible demo-data label. Do not capture real traffic, account names, user profile paths, a desktop, or runtime diagnostics. Add a screenshot for a new visible feature and update affected screenshots when an existing view changes.

## Releases

The Windows workflow builds and tests source changes. A pushed `v*` tag builds a self-contained Windows x64 package, publishes a ZIP and SHA-256 checksum, and creates a GitHub release. Downloaded GeoIP data and third-party notices accompany the binary package; build output and databases stay out of source control.
