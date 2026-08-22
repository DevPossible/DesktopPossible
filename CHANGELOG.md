# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

DesktopPossible is a hard fork of
[Desktop Frames +](https://github.com/limbo666/DesktopFramesPlus). The pre-fork
release history is preserved in
[docs/upstream-version-history.md](docs/upstream-version-history.md).

## [Unreleased]

### Added

- New application logo and icon set for the DesktopPossible brand.
- Root build and workflow scripts: `build.ps1`, `test-smoke.ps1`, `test-full.ps1`,
  `start-app.ps1`, `package.ps1`, and `create-release.ps1`.
- Conventional-commit version calculation scripts (`scripts/get-version.ps1` / `.sh`).
- Unit test project (`tests/DesktopPossible.Tests`) with headless-safe tests.
- GitLab CI pipeline that builds and tests on Windows, mirrors `main` and tags to
  GitHub, and publishes GitHub releases with a self-contained portable zip.
- Project documentation: `SECURITY.md`, `THIRD-PARTY-NOTICES.md`, `CLAUDE.md`,
  and a migration guide for upstream users.

### Changed

- Rebranded as **DesktopPossible**, a hard fork maintained by
  [DevPossible](https://devpossible.com); the executable is now
  `DesktopPossible.exe`. Existing profiles migrate automatically.
- Renamed the interim `DesktopFramesPossible` identity to **DesktopPossible**
  across the UI, docs, and update manifest. Installs of the interim build
  migrate automatically: the registry tree moves from
  `HKCU\SOFTWARE\DevPossible\DesktopFramesPossible` to
  `...\DevPossible\DesktopPossible`, the start-with-Windows Run entry is
  renamed, and the per-frame file store moves from
  `%LocalAppData%\DesktopFramesPossible` to `%LocalAppData%\DesktopPossible`
  (item paths are remapped on load; if both folders exist, neither is touched).
- Restructured the repository to the standard `src/` + `tests/` layout with a
  single solution (`src/DesktopPossible.sln`).
- Update/announcement manifest now served from this repository (`remote/`).
- Distribution is a self-contained single-file build; no separate .NET runtime
  install is required.

### Removed

- Upstream update channel, donation links, and upstream-specific branding.
