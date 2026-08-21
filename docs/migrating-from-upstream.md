# Migrating from Desktop Frames +

DesktopFrames+Possible is a hard fork of
[Desktop Frames +](https://github.com/limbo666/DesktopFramesPlus). If you have
been using the upstream app, your entire setup — profiles, frames, shortcuts,
layouts, and backups — carries over.

Because both apps are fully portable, migrating is safe and requires no
installer. DesktopFrames+Possible includes an automated migration engine that
translates your existing configuration files to the current format across all
profiles on first launch.

## Migration Steps

**Step 1: Shut down the old app.** Make sure Desktop Frames + is completely
closed: right-click its icon in the Windows system tray and select **Exit**.

**Step 2: Prepare your new folder.** Download the latest
`DesktopFramesPossible-<version>-win-x64.zip` from the
[releases page](https://github.com/DevPossible/DesktopFramesPossible/releases)
and unzip it anywhere you like (for example `C:\Tools\DesktopFramesPossible`).
The app is 100% portable, so the folder name and location are up to you.

**Step 3: Transfer your profile data.** Open your **old** Desktop Frames +
folder and copy the following items:

- the entire `Profiles` folder *(all your profile configurations, layouts,
  shortcuts, and backups)*
- `ProfileOptions.json` *(your master profile configuration file)*

Paste both directly into your **new** DesktopFrames+Possible folder. If
prompted, choose to overwrite/replace any existing files.

**Step 4: Launch and let it upgrade.** Run `DesktopFramesPossible.exe`. On
launch, the app scans your `Profiles` folder, automatically translates your
old configuration files to the current standard, and loads your desktop
exactly as you left it.

**Step 5: Verify and clean up.** Check that your frames, shortcuts, and
profiles all display correctly. Once you are satisfied, you can safely delete
the old program folder.

## Startup Registration

Start-with-Windows registration does not transfer — it is tied to the
executable path. Enable it again via the in-app option in the new app. Old
"Desktop Frames +" autostart entries left behind by the upstream app are
detected and cleaned up automatically.
