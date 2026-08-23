<h1 align="center">DesktopPossible</h1>
<p align="center"><i>Organize your desktop like magic!</i></p>
<p align="center"><b>A free, open-source Stardock Fences alternative for Windows 10/11</b> — group desktop icons into frames, mirror folders, and tidy your desktop.</p>

<p align="center">
  <img width="150" height="150" alt="DesktopPossible logo" src="docs/images/logo.png" />
</p>

<p align="center">
  <img alt="A DesktopPossible frame holding developer-tool shortcuts on a Windows 11 desktop" src="docs/images/screenshot-frame.png" />
  <br /><sub>A Data frame on the desktop — title bar with the ♥ menu, position lock, move handle and keep-on-top pin. <a href="docs/images/screenshot-desktop.png">Full-desktop view</a>.</sub>
</p>

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/DevPossible/DesktopPossible)](https://github.com/DevPossible/DesktopPossible/releases)

---

> ### This is a hard fork
> DesktopPossible is a **hard fork** of [**Desktop Frames +**](https://github.com/limbo666/DesktopFramesPlus) (MIT) by **limbo666 / Nikos Georgousis**, which is itself a continuation of the original **BirdyFences** by **HakanKokcu**. All upstream work belongs to those authors — see [Credits](#credits).
>
> As a hard fork, this project **does not track or merge upstream commits** — it evolves independently from the point of the fork. If you want the original prebuilt tool, use the [upstream releases](https://github.com/limbo666/DesktopFramesPlus/releases).
>
> **A note on AI assistance:** in the interest of transparency, the fork-specific enhancements in this repository were built with substantial help from [Claude](https://www.anthropic.com/claude) (Anthropic's AI assistant) — used for writing, refactoring, and debugging the added code. The upstream projects are the work of their original human authors.

---

## Features

DesktopPossible creates **virtual frames** on your desktop, letting you group and organize icons cleanly.

### Core

- **Multiple frame types:** Data frames (custom shortcuts), Portal frames (live mirror of a folder, with navigation and filters), Note frames (quick text), Image frames, and Text frames (live system-info text drawn straight onto the wallpaper).
- **Tabs, workspace profiles, and a smart-desktop auto-sort engine** (one-click *Sort Desktop into Categories*, optional continuous Auto-Organize).
- **Edit Frames Mode:** frames are locked in place by default; flip the mode on to move and resize them, off to make them unmovable again.
- **Dynamic visibility:** peek-behind, roll-up, auto-hide, idle fade-out, keep-on-top pinning, and a *Focus Frame* hotkey that finds a frame by name and raises it.
- **Broad launch support:** files, folders, web links, Store apps, Steam games, Spotify URIs, run-as-admin / run-as-different-user.
- **Theming:** per-frame colours, global tint, and a wallpaper-matching "Chameleon" mode.
- **Portable or installed, your choice:** a zip that keeps its configuration beside the executable, or an MSI that installs to Program Files and keeps data in `%LocalAppData%`.

### Portal Details view

A Windows Explorer–style list view for Portal frames, as an alternative to the icon grid:

- **Toggle per frame:** right-click a Portal frame → **View → Icons / Details** (remembered per frame).
- **Columns:** Name, Date modified, Type, Size — resizable, with widths saved per frame.
- **Click-to-sort** with an ascending/descending indicator; right-click **Sort by / Group by** with Explorer-style buckets (Today / Yesterday / Earlier this week, size ranges, and more).
- **Native shell context menu** on right-click (Open with, Send to, cut/copy/paste, Properties, shell extensions), lazily loaded so the first right-click stays fast.
- **Zebra striping** (global default + per-frame override), and chrome (headers, scrollbars, selection) themed to the frame's colour.

### Portal icon view

- **Sort by** menu (Name / Date / Type / Size) with ascending/descending order, plus a "Sorted by …" heading.
- Data frames instead offer **Free arrange** — place each icon in exactly the cell you want.
- Themed scrollbar to match the frame (all frame types).

### Frames and title bar

- **Per-type title glyph** (folder / note / shortcut / image), tinted to match the other title-bar icons; Portal frames show their folder path on hover.
- **Rename Frame** from the context menu; hotkey shown in the title.
- **Per-frame transparency** override (Customize dialog) on top of the global Frame Tint.
- **Content lock** for Note and Image frames to prevent accidental edits.
- **Per-frame launch effects** (15 animations) and an optional **Create New Frame** entry in the desktop's right-click menu — draw a rectangle where the frame should go.
- **Frame housekeeping:** Export / Import a frame, Send to Profile, Restore Last Deleted Frame, Clear Dead Shortcuts, spacers, and an *Empty Frames to Desktop* safety tool for frames that store files.

### Hotkeys

- **Show / Hide all frames** (default `Ctrl + Alt + H`, customizable).
- **SpotSearch** (default `` Ctrl + ` ``) — type to find any icon in any frame and launch it.
- **Per-frame focus** hotkey (press-to-capture; supports groups and the Windows key) and **profile-switch** hotkeys (direct, previous, next).
- Optional **double-click empty desktop** to toggle native desktop icons.
- **Virtual desktops:** switch profiles automatically when Windows switches virtual desktop.

### Dark mode and theming

- **Dark-mode context menus** throughout (frame, icon, Notes editor, and the native shell menu) that follow the OS light/dark setting.

### Performance and footprint

- **Extension-based caching** of shell icons and type names for fast Portal loads.
- **Lazy context menus**, batched Portal reconciler, working-set trimming, and pre-warmed shell caches for a fast first right-click.
- **Update check:** the app quietly checks GitHub for a newer release and shows a link in Options and a dot on the tray icon — never a pop-up.

## Installation

Every [release](https://github.com/DevPossible/DesktopPossible/releases) ships two downloads:

**Installer (recommended):** run `DesktopPossible-<version>-win-x64.msi`. It installs to
`C:\Program Files\DesktopPossible`, adds a Start Menu shortcut, and upgrades an existing
install in place. Your configuration lives in `%LocalAppData%\DesktopPossible\Data`.
Uninstall from *Settings > Apps* like any other program (your data folder is left untouched).

**Portable:** download `DesktopPossible-<version>-win-x64.zip`, unzip it anywhere you like
(for example `C:\Tools\DesktopPossible`) and run `DesktopPossible.exe`. All configuration is
stored beside the executable, so moving the folder moves your setup with it.

Either way, to launch at boot enable the start-with-Windows option inside the app — it
registers (and cleans up) the autostart entry for you.

Requires Windows 10 or 11. The download is self-contained; no separate .NET installation is needed.

## Migrating from Desktop Frames +

Coming from the upstream Desktop Frames + app? Your profiles carry over: copy your old `Profiles` folder and `ProfileOptions.json` into the new folder and launch — an automatic migration engine translates your configuration. See the [migration guide](docs/migrating-from-upstream.md) for step-by-step instructions.

## Building from source

The project targets **.NET 10** (`net10.0-windows10.0.19041.0`) and builds with the plain **.NET SDK** on any OS (COM interop is late-bound; `EnableWindowsTargeting` is preconfigured). The app itself — and its test suite — runs on **Windows only**.

1. Install the [**.NET 10+ SDK**](https://dotnet.microsoft.com/download).
2. Clone the repository and run:

```powershell
./build.ps1        # Builds the solution (dotnet SDK)
./test-smoke.ps1   # Runs the unit test suite (Windows)
./start-app.ps1    # Builds (if needed) and launches the app
```

## Documentation

| Topic | Description |
|-------|-------------|
| [User Manual](docs/manual.md) | Full feature walkthrough — frames, customization, settings |
| [Tips & Tricks](docs/tips.md) | Power-user features: portal filters, SpotSearch, profiles |
| [Advanced Tweaks](docs/tweaks.md) | JSON-level configuration tweaks |
| [Migrating from Desktop Frames +](docs/migrating-from-upstream.md) | Moving your setup from the upstream app |
| [Upstream Version History](docs/upstream-version-history.md) | Pre-fork release history from the upstream project |

## Contributing

Contributions are welcome. Pull requests should target the `develop` branch — `main` is release-only and is updated by the release process.

We use [Conventional Commits](https://www.conventionalcommits.org/); commit messages drive automated versioning:

```
feat: add new frame grouping option    # Minor version bump
fix: correct portal refresh on rename  # Patch version bump
feat!: redesign profile storage        # Major version bump (breaking)
docs: update manual screenshots        # No release
```

Releases are maintainer-triggered; merged work ships when the maintainer cuts the next release.

## Versioning

Versions follow [Semantic Versioning](https://semver.org/) and are calculated automatically from conventional commit messages. Release tags (`vX.Y.Z`) are created by CI — never by hand.

## License

MIT — see [LICENSE](LICENSE), which retains the full copyright chain: DevPossible (this fork), limbo666 (Desktop Frames +), and HakanKokcu (BirdyFences). Third-party library and asset attributions are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Credits

- Original **BirdyFences** by **HakanKokcu**.
- **Desktop Frames +** by **limbo666 / Nikos Georgousis** — please star and support the [upstream project](https://github.com/limbo666/DesktopFramesPlus).
- **DesktopPossible** is maintained by [DevPossible](https://devpossible.com) ([GitHub](https://github.com/DevPossible)), building on the above under the MIT License, with development assistance from Claude (see the AI note above).
