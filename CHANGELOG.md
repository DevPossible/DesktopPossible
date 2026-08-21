# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

DesktopFrames+Possible is a hard fork of
[Desktop Frames +](https://github.com/limbo666/DesktopFramesPlus). The pre-fork
release history is preserved in
[docs/upstream-version-history.md](docs/upstream-version-history.md).

## [Unreleased]

### Added

- New application logo and icon set for the DesktopFrames+Possible brand.
- Root build and workflow scripts: `build.ps1`, `test-smoke.ps1`, `test-full.ps1`,
  `start-app.ps1`, `package.ps1`, and `create-release.ps1`.
- Conventional-commit version calculation scripts (`scripts/get-version.ps1` / `.sh`).
- Unit test project (`tests/DesktopFramesPossible.Tests`) with headless-safe tests.
- GitLab CI pipeline that builds and tests on Windows, mirrors `main` and tags to
  GitHub, and publishes GitHub releases with a self-contained portable zip.
- Project documentation: `SECURITY.md`, `THIRD-PARTY-NOTICES.md`, `CLAUDE.md`,
  and a migration guide for upstream users.

### Changed

- Rebranded as **DesktopFrames+Possible**, a hard fork maintained by
  [DevPossible](https://devpossible.com); the executable is now
  `DesktopFramesPossible.exe`. Existing profiles migrate automatically.
- Restructured the repository to the standard `src/` + `tests/` layout with a
  single solution (`src/DesktopFramesPossible.sln`).
- Update/announcement manifest now served from this repository (`remote/`).
- Distribution is a self-contained single-file build; no separate .NET runtime
  install is required.

### Removed

- Upstream update channel, donation links, and upstream-specific branding.
