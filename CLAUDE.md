# CLAUDE.md - DesktopPossible Development Guide

This file provides context for Claude Code when working on DesktopPossible.

## 1. Project Overview

DesktopPossible is a free, open-source Stardock Fences alternative for
Windows 10/11: it creates virtual "frames" on the desktop to group icons,
mirror folders (Portal frames), hold notes and images, with profiles, hotkeys,
theming, and a smart auto-sort engine. It ships as a portable zip (config
beside the exe) and as an MSI installer (Program Files; config under
`%LocalAppData%\DesktopPossible\Data`) — see `AppPaths.DataRoot`.

**Hard-fork context:** this is a hard fork of
[Desktop Frames +](https://github.com/limbo666/DesktopFramesPlus) by
limbo666 / Nikos Georgousis (itself a continuation of BirdyFences by
HakanKokcu). We never sync or cherry-pick upstream commits — the codebase
evolves independently. Development happens on a private GitLab;
[github.com/DevPossible/DesktopPossible](https://github.com/DevPossible/DesktopPossible)
is the public shopfront receiving `main`, tags, and releases only.

**Naming:** the product/assembly name is `DesktopPossible`, but the
internal C# namespace is `Desktop_Frames` (inherited from upstream). This is
**deliberate** — do NOT rename the namespace; it keeps the diff against the
fork point minimal and avoids churn across ~58 files.

## 2. Build & COM Interop Model

The project builds with the **plain `dotnet` SDK on any OS** — including Linux
CI — because `EnableWindowsTargeting=true` is set in `Directory.Build.props`
and all COM interop (Windows Script Host `WScript.Shell` for .lnk shortcuts,
`Shell.Application` for shell automation) is **late-bound** via
`Type.GetTypeFromProgID(...)` + `dynamic`. There are NO `<COMReference>` items.

- Build via `./build.ps1` (or `dotnet build src/DesktopPossible.sln`).
- The app **runs on Windows only** (`net10.0-windows10.0.19041.0`, WPF + WinForms), and
  tests also **execute** on Windows only — Linux can compile and cross-publish
  but not run them.
- Never reintroduce `<COMReference>` items or compile-time interop assemblies —
  that reinstates a build-time Windows/VS dependency. New COM calls follow the
  same late-binding pattern (COM member names in `dynamic` dispatch are
  case-sensitive at runtime; no compiler safety — test on Windows).

## 3. Commands

| Command | Purpose |
|---------|---------|
| `./build.ps1` | Build the solution with the dotnet SDK |
| `./test-smoke.ps1` | Run unit tests (fast, headless-safe) |
| `./test-full.ps1` | Run the full test suite |
| `./start-app.ps1` | Build (unless `-NoBuild`) and launch the app |
| `./package.ps1` | Produce the self-contained single-file portable zip and (on Windows) the MSI installer; add `-Sign` to Authenticode-sign both |
| `./upload-msi.ps1` | **User-only** — after a release pipeline: build the *signed* MSI and zip on Windows and attach them to the GitHub release |
| `./create-release.ps1` | **User-only** — triggers a release (never run this) |
| `./scripts/get-version.ps1` / `.sh` | Calculate next semver from conventional commits |

## 4. Repository Layout

```text
DesktopPossible/
├── src/
│   ├── DesktopPossible.sln       # The only solution
│   └── DesktopPossible/          # WPF app (~58 single-class .cs files)
│       └── Resources/                  # Icons and logos
├── tests/
│   └── DesktopPossible.Tests/    # xUnit unit tests (headless-safe)
├── docs/                               # manual.md, tips.md, tweaks.md,
│                                       # migrating-from-upstream.md,
│                                       # upstream-version-history.md
├── remote/                             # LIVE app-served config (see below)
│   ├── getversion.json                 # Update/announcement manifest
│   └── dev.json                        # Dev/test variant of the manifest
├── scripts/                            # get-version.ps1/.sh
├── .gitlab-ci.yml                      # CI: build/test, mirror to GitHub, release
├── build.ps1 / test-*.ps1 / start-app.ps1 / package.ps1 / create-release.ps1
└── LICENSE                             # MIT with full fork copyright chain
```

**`remote/` is production config, not documentation.** Released builds fetch
`remote/getversion.json` from `raw.githubusercontent.com` on the `main` branch
at runtime (update check + announcements). Any change to `remote/` goes live
for all installed users as soon as it reaches `main`. Edit with care; keep the
JSON schema exactly as-is.

## 5. Architecture Map

The app is ~58 single-class files in `src/DesktopPossible/`, namespace
`Desktop_Frames`, grouped by subsystem. Note: some class names use lowercase
"manager" from upstream (`Framemanager`, `PortalFramemanager`,
`ImageFramemanager`) — match existing spelling.

### Frame lifecycle (the core)

- `FrameManager.cs` (~10,700 lines) — the heart of the app: creates, renders,
  and manages all frame windows, their chrome, menus, and interactions. Most
  features touch this file.
- `FrameDataManager.cs` — frame data persistence, validation, and migration
  (extracted from Framemanager).
- `FrameUtilities.cs` — standalone frame helper methods with minimal
  dependencies.

### Frame types

- `PortalFrameManager.cs` — Portal frames: live folder mirroring, Details
  view, filters, reconciler.
- `ImageFrameManager.cs` — Image frames (copied or linked image, content lock).

### Desktop integration

- `DesktopIconManager.cs` — native desktop icon show/hide.
- `DesktopMouseHook.cs` — WH_MOUSE_LL hook; double-click on the bare desktop
  wakes frames.
- `TrayManager.cs` — system tray icon and menu.
- `RegistryHelper.cs` — all registry operations (autostart, instance trigger,
  migrations).
- `GlobalHotkeyManager.cs` — low-level keyboard hook for all global shortcuts.

### UI forms (`*FormManager` convention)

`AboutFormManager`, `OptionsFormManager`, `CustomizeFrameFormManager`,
`NotificationFormManager`, `MessageBoxesManager` — each owns one dialog/window.
Related standalone forms:
`ProfileManagerForm`, `EditShortcutWindow`, `IconPickerDialog`,
`ItemMoveDialog`.

### Infrastructure

- `LogManager.cs` — application logging.
- `SettingsManager.cs` — application settings ("Hard Switch" master support).
- `ProfileManager.cs` — multi-profile support; central authority for file
  storage paths.
- `BackupManager.cs` — timestamped profile backups/restore.
- `RemoteInfoManager.cs` — fetches the remote manifest (update check +
  announcements).
- `LazyIconLoader.cs` — background/queued shell icon loading.
- `MemoryOptimizer.cs` — GC + working-set trimming.
- `PreWarmManager.cs` — pre-warms shell caches on idle so the first
  right-click is fast.

### Shared-state hubs

- `InterCore.cs` — shared interactive-features/animations hub (easter-egg
  effects, sparkle/gravity/legendary modes).
- `CoreUtilities.cs` — consolidated utility methods (Win32 interop, shared
  helpers) used across Framemanager/IconManager/PortalFramemanager.

### Supporting cast (selected)

`IconManager` / `IconDragDropManager` (icon rendering and drag-drop),
`ShellContextMenu` (native shell menu hosting), `DarkMenuTheme` /
`ThemedScrollBar` (theming), `WallpaperColorManager` (Chameleon mode),
`SnapManager` (frame snapping), `AutoOrganizeManager` + `AppCategorizer`
(desktop auto-categorization), `LaunchEffectsManager`, `SingleInstanceChecker`,
`SmartToast`, `TargetChecker`, `RemoteDataModel`, `Utility` /
`FilePathUtilities`.

## 6. Conventions

### Conventional Commits

Commit messages drive automated versioning. Format:
`<type>(<scope>): <subject>`.

| Type | Use For | Version Bump |
|------|---------|--------------|
| `feat` | New feature | MINOR |
| `fix` | Bug fix | PATCH |
| `perf` | Performance | PATCH |
| `refactor` | Code change (no feature/fix) | PATCH |
| `docs` | Documentation | none |
| `test` | Tests | none |
| `build` / `ci` / `chore` / `style` | Tooling, CI, maintenance | none |

Breaking change: `!` after the type (e.g. `feat!:`) or a `BREAKING CHANGE:`
footer → MAJOR bump.

Common scopes: `frames`, `portal`, `notes`, `tray`, `hotkeys`, `theme`, `ci`,
`docs`.

Examples:

```
feat(portal): add group-by size buckets to Details view
fix(hotkeys): release keyboard hook on profile switch
perf(frames): batch portal reconciler updates
docs: clarify migration steps for upstream users
```

### Versioning model

Versions are calculated by `scripts/get-version.ps1` from conventional commits.
Tags (`vX.Y.Z`) are created **by CI only**. Never hand-edit version numbers in
project files, manifests, or tags.

## 7. Release Policy (verbatim rules)

**Do NOT push `main`. Do NOT run `create-release.ps1`.**

1. Commit work to `develop` (the integration branch).
2. `main` is release-only; CI mirrors it (plus tags and releases) to GitHub.
3. The user triggers releases — never automate or initiate one yourself.
4. After the pipeline creates the GitHub release, the user runs `upload-msi.ps1`
   on Windows to attach the signed installer **and** the signed zip (which
   replaces the unsigned zip the Linux pipeline attached). A release is not
   finished until that step has run — until then the only published artifact is
   an unsigned zip.

## 8. Gotchas

- **csproj encoding:** `DesktopPossible.csproj` must stay UTF-8. Some
  editors/tools re-save it with a different encoding and break the build.
- **Data root is `AppPaths.DataRoot`, never the exe folder directly:** beside the
  exe when portable, `%LocalAppData%\DesktopPossible\Data` when the MSI's
  `DesktopPossible.installed` marker is present (Program Files is read-only for
  users). Build every data path from it.
- **MSI:** `installer/DesktopPossible.wxs` (WiX v4+ authoring, `wix` dotnet tool
  pinned in `.config/dotnet-tools.json`, built by `package.ps1`). WiX runs on
  Windows only, so the Linux CI publishes the zip alone and the MSI is attached
  afterwards with `upload-msi.ps1`. Keep the `UpgradeCode` GUID stable forever —
  it is what lets a new MSI upgrade an old install.
- **Code signing is Windows-only and happens outside CI:** release binaries are
  Authenticode-signed with **Azure Trusted Signing** (account `DevPossible`,
  certificate profile `CodeSigning`, endpoint `https://eus.codesigning.azure.net/`)
  via the pinned `sign` dotnet tool. Signing needs `az login` as a principal
  holding the **Artifact Signing Certificate Profile Signer** role on the signing
  account — subscription Owner alone is *not* enough, the data-plane action is
  RBAC-gated separately. The Linux CI cannot sign, so `package.ps1 -Sign` runs on
  Windows via `upload-msi.ps1`. Trusted Signing certificates are deliberately
  short-lived (they roll every few days), which is why every signature is RFC 3161
  timestamped — the timestamp is what keeps shipped binaries valid after the
  certificate expires. Never pin the `sign` tool by floating version: the NuGet id
  `sign` collides with an unrelated third-party package at 1.x, so only the
  Microsoft `0.9.1-beta.*` line is correct.
- **Portable config beside the exe:** the app reads/writes `Profiles/` and
  `ProfileOptions.json` next to the executable. Never rename these — the
  migration engine and user upgrades depend on the exact names.
- **Registry migrations are append-only:** `RegistryHelper` migration patterns
  clean up *old* entries by name (including upstream "Desktop Frames +"
  autostart entries). Extend the list; never replace or remove existing
  patterns, or old installs stop being cleaned up.
- **`remote/getversion.json` is live production config** — fetched by released
  builds from raw.githubusercontent on `main`. Changes go live on push to
  `main`. Keep the schema identical; never add donation/tracking links.
- **WPF + WinForms both enabled** (`UseWPF` + `UseWindowsForms`): interop is
  intentional (tray icon, shell context menus, some dialogs). Don't "clean up"
  either flag.
- **PublishSingleFile:** the release zip is a self-contained single-file
  publish. Any change touching publish settings, resources, or COM interop
  needs verification via `./package.ps1` — single-file has different
  extraction/loading behavior than a normal build.

## 9. Testing

Tests live in `tests/DesktopPossible.Tests` (xUnit).

**Headless-safe rules** — tests run on CI runners with no desktop session, so
they must NOT:

- write to the registry,
- create COM objects or hit the shell,
- perform network calls,
- write profile/config data outside the test directory.

Test pure logic (parsing, sorting, version calc, path/utility helpers), not
window plumbing.

Run a subset (Windows only — the tests target `net10.0-windows`):

```powershell
./build.ps1; dotnet test tests/DesktopPossible.Tests --no-build --filter "FullyQualifiedName~PortalSort"
```
