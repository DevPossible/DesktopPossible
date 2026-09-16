
# DesktopPossible | Tips & Tricks

Welcome to the hidden features guide. This document highlights advanced functionality and "power-user" shortcuts to help you get the most out of DesktopPossible.

---

## The Power of the CTRL Key
The `CTRL` key acts as a "modifier" that reveals advanced context menus and shortcuts. When in doubt, try holding `CTRL`.

* **Rename:** `CTRL + Left Click` on a frame title (or right-click the frame → *Rename Frame*).
* **Arrange Icons:** `CTRL + Drag` an icon to its new location in the frame. Data frames can also switch to **Free arrange** (right-click → *Free arrange*) to place icons in exact cells.
* **Frame Icons Export:** `CTRL + Right Click` an empty area inside a frame to access hidden administrative tools (e.g., *Export all icons to desktop*).
* **Portal Navigation:** `CTRL + Left Click` a folder inside a **Portal Frame** to navigate into that folder within the same frame, rather than opening a new Windows Explorer window.
* **Portal Frame Naming:** `CTRL + Right Click` and select "Name Frame After Target Path" renames a Portal frame to its target path.
* **Adding separators (spacers) to Data frames:** `CTRL + Right Click` any area on a Data frame and select "Add spacer" > "Blank" or "Dot". Move it with `CTRL + Drag` like any icon.
* **Export all shortcuts from a frame:** `CTRL + Right Click` any area on a Data frame and select "Export all icons to desktop"
* **Export a single shortcut:** `CTRL + Right Click` an icon and select "Send to desktop"
* **Run as different user:** right-click an icon and select "Run as different user..." (or "Always run as different user").
* **Move frames:** frames are locked in place until you turn on **Edit Frames Mode** (tray menu or frame right-click menu). Turn it off again when your layout is done.

 ---

## Portal Frame Filters
Click the **Filter Icon** (located next to the Lock icon) to toggle the filter bar. Filters allow you to dynamically control which files are visible.

### Usage & Syntax:
* **Wildcards:** Use `*` to match patterns (e.g., `*.txt` displays only text files).
* **Multi-Filter:** Separate multiple formats with commas (e.g., `*.mp4, *.avi`).
* **Negative Filtering:** Use the `>` prefix to exclude specific terms.
    * *Example:* `*.mp3, >*live*` (Shows all MP3s EXCEPT those with "live" in the filename).
* **Persistent State:** Filters stay active until cleared. An **orange indicator** signifies an active filter. Use the **"X"** on the right of the bar to reset.


---

## Show or hide all frames
Press `Ctrl + Alt + H` (default; Options → Style & FX → *Enable Show/Hide all frames hotkey*) or double-click the tray icon to toggle every frame at once. Pin an individual frame above other windows with the pin icon in its title bar (or right-click → *Always on top*).

Every frame can also have its own **focus hotkey**: right-click the frame → Customize... → *Focus Hotkey* and press the combination you want. 


### Profiles
Profiles offering multiple configuration switching. It is a flexible method to switch icon collections between work and home, desk and presentation, work and fun or whatever mode you like to switch to.  
Profiles are the best way to keep your desktop organized and grouped according to your needs.  
Profiles can be created from the tray menu (Profiles → Create New Profile...) or from Options → Profiles.

  
Switching profiles can be done by selecting the appropriate profile name on tray menu or by using the hotkeys (default `CTRL + ALT [Profile number 0 to 9]`) or by swicthing to next (default `CTRL + ALT + .`) or previous (default `CTRL + ALT + ,`)  

    Profile Manager (Options → Profiles)
   - Switch, arrange, delete and rename profiles, and *Empty Frames to Desktop* for frames that store files.

  Moving frames between profiles
   - Heart ♥ menu → *Send to Profile...* moves a frame to another profile; *Export this Frame* / *Import a Frame...* write and read portable `.frame` files; *Restore Last Deleted Frame* brings back the last one you deleted.

  Virtual desktops
   - Options → General → *Automatically Switch Profiles with Virtual Desktop* ties a profile to each Windows virtual desktop, so switching desktop switches your frames.
  
  

### Smart Desktop (Auto-Organize)
 - Options → Smart Desktop → **Arrange Now** looks at every loose icon on the desktop, works out what each one is (browser, developer tool, game, document, ...) and moves it into a matching category frame, creating frames as needed. One click, no rules to write.
 - **Enable Auto-Organize** (tray menu or Options → Smart Desktop) keeps doing this in the background as new icons land on the desktop; *Show execution toast notifications* tells you when it moved something.
 - Options → Smart Desktop also has **Arrange Frames** (auto-size and tile all frames).
  
 

---

## Advanced JSON Tweaks
For deeper customization, you can manually edit the profile's `options.json` file (see [File Locations](manual.md#file-locations)). Below are the most impactful variables:

### Filter & Display Tweaks
| Variable | Description |
| :--- | :--- |
| `NoWildcardsOnPortalFilter` | Set to `true` to match text without needing `*`. (e.g., `.mp3` instead of `*.mp3`). |
| `ShowPortalExtensions` | Set to `true` to display file extensions within Portal Frames. |
| `MaxDisplayNameLength` | Set the character limit for shortcut names (Range: `5` to `50`). |

### Workflow & Deletion
| Variable | Description |
| :--- | :--- |
| `ExportShortcutsOnFrameDeletion` | If `true`, all icons inside a frame are automatically moved to the desktop when that frame is deleted. |
| `DeleteOriginalShortcutsOnDrop` | If `true`, dropping a desktop icon into a frame will delete the original desktop file, effectively "moving" it into the frame. |

### System
| Variable | Description |
| :--- | :--- |
| `DisableSingleInstance` | Set to `true` to allow running multiple separate instances of the program. |



---
