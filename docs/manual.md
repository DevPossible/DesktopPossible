# DesktopPossible User Manual


<div style="width:20%; margin: auto;">


</div>


## Overview

DesktopPossible is a powerful desktop organization tool that creates virtual "Frames" on your desktop, allowing you to group and organize icons in a clean and convenient way. Think of Frames as visible containers that help you organize your desktop shortcuts, files, and folders into logical groups.

---

## Getting Started

### System Requirements

- Windows 10 or Windows 11
- Nothing else: the download is self-contained (.NET 10 is bundled)


### First Launch

When you first start DesktopPossible, the application will:

1. Create necessary basic configuration files
2. Add itself to the system tray

---

## Core Features

### 1. Creating Frames

**What are Frames?**
Frames are containers on your desktop that group related shortcuts and files together. You can think of them as boxes that keep your desktop organized.

**frame Types:**

- **Frames (Data Frames)**: Regular containers for shortcuts targeting files or folders and web links
- **Portal Frames**: Special Frames mirroring the contents of folders, with navigation, filters and an Explorer-style Details view
- **Note Frames**: Frames that display text
- **Image Frames**: Frames that display a picture (pasted, or copied from / linked to a file)
- **Text Frames**: Chromeless live text drawn straight onto the wallpaper, with tokens such as `{ComputerName}`, `{IP}`, `{CPUUsage}`, `{RAMUsage}`, `{Uptime}`, `{Date}`, `{Time}`, `{Profile}` (managed from Options → Text Frames)

**How to Create a frame:**

1. The first two Frames are created automatically on first run. Look on the top left corner of your screen.
2. From there, the heart ♥ menu offers *New Frame*, *New Portal Frame*, *New Note Frame*, *New Image Frame* and *New Text Frame*.
3. Optionally enable *Show 'New Frame' in Desktop Context Menu* (Options → General) and draw a rectangle on the desktop where the frame should go.

**Moving and resizing frames:** frames are locked in place by default. Turn on **Edit Frames Mode** (tray menu, or right-click a frame) to move and resize them, then turn it off again.

**Rename a frame:**

- CTRL + Click the frame title (or right-click the frame → *Rename Frame*). Type your preferred name and click out of the edit area to finish.

### 2. Adding Items to Frames

**Adding Shortcuts:**

- Drag and drop existing desktop shortcuts, folders, or files into any frame. This will generate automatically a shortcut for the target.

  **Supported types**

- Shortcuts (.lnk files)
- Web links (.url files), shortcuts targeting web sites or links from web browsers
- Folders and directories
- Document files
- Network targers

### 3. Moving Items Between Frames

**Using the Move Dialog:**

1. Right-click on any item in a frame
2. Select "Move to..." from the context menu
3. Choose the destination frame from the hierarchical list
4. Confirm the move operation

**Using Copy Item:**

1. Right-click on any item in a frame
2. Select "Copy Item" from the context menu
3. Right-click on any other frame and select "Paste Item"

---

## Customization Options

### 1. frame Appearance

Access frame customization by right-clicking on a frame title bar and selecting "Customize...":
This will show the Customize frame window.



**frame Settings:**

- **Custom Color**: Choose from 13 preset colors (Red, Green, Teal, Blue, Bismark, White, Beige, Gray, Black, Purple, Fuchsia, Yellow, Orange) or Transparent; leave on Default to follow the global colour (and Chameleon mode)
- **Override default transparency**: per-frame tint instead of the global Frame Tint
- **Focus Hotkey**: a key combination that brings this frame to the front
- **Custom Launch Effect**: Customize the effect on icon click. See Launch Effects below
- **Border Color**: Customize the frame border appearance
- **Border Thickness**: Adjust border width (1-5 pixels)

**Title**

- **Title Text Color**: Change frame title color
- **Title Bar Color**: Colour the title bar independently of the frame body
- **Title Text Size**: Small, Medium, or Large
- **Bold Title Text**: Make frame titles bold

**Icons**

- **Icon Size**: Tiny (24px), Small (32px), Medium (40px), Large (48px), Huge (64px)
- **Icon Spacing**: Adjust space between icons (0-20 pixels)
- **Text Color**: Color for item labels
- **Disable Text Shadow**: Remove shadow effects from text
- **Grayscale Icons**: Convert all icons to grayscale ()

### 2. Launch Effects

When you click on items in Frames, choose from these visual effects:

- **Zoom**: Items grow before launching
- **Bounce**: Items bounce up and down
- **FadeOut**: Items fade away
- **SlideUp**: Items slide upward
- **Rotate**: Items spin before launching
- **Agitate**: Items shake vigorously
- **GrowAndFly**: Items grow and fly off screen
- **Pulse**: Items pulse with light
- **Elastic**: Items stretch elastically
- **Flip3D**: Items flip in 3D space
- **Spiral**: Items spiral outward
- **Shockwave**: Creates a shockwave effect
- **Matrix**: Matrix-style digital rain effect
- **Supernova**: Explosive light effect
- **Teleport**: Items teleport with particle effects


## Change Icon Order

Hold down the CTRL button and drag the icon to its new position within the frame.


## Customize Shortcuts

You can check and change the icons properties by right clicking the icon and selecting "Edit..."

In the edit window you can set name, target path, arguments and icon for the shortcut. Note: The program supports ico, exe and dll files as icon sources. 



## Manage Invalid Shortcuts

The icons on the Frames which are targeting files and folders are getting continuously checked for target validity. If target is missing or it is not accessible an icon indicate that this shortcut is not functioning, so you can check the target and fix it or delete the dead shortcut. You can right click on this icon and select "Remove".   

**Clear Dead Shortcuts:** If you want to remove all dead shortcuts within a frame with just one move right click on the frame and select "Clear Dead Shortcuts"



## Convenient Usage Functions

**Hide frame**: Right click a frame an select "Hide frame". This sets the frame as hidden and the number of hidden Frames increases ion the tray icon. To show the frame again you can use the tray icon context menu where the hidden Frames are shown as items on "Show Hidden Frames" menu

**Peek Behind**: Right click a frame and select "Peek Behind". The frame hides for 10 second to help access the desktop behind the frame. This feature is helpful if for some reason you need to see behind the frame for a short time. A count down timer is shown in place.

**Hide/Unhide All Frames**: Double click the tray icon. All Frames will toggle. Note: This function doesn't change the status of hidden Frames with teh above mentioned "Hide frame" function.

**Rollup/RollDown a frame**: Double click on frame title. The frame will roll up into its title; double click the title again to roll it back down. Options → Style & FX → *Idle Auto-Roll* can do this automatically after a period of inactivity.

**Frame Lock**: Click the lock icon in the top-right corner of the frame to pin its position and size; the move handle next to it drags the frame, and the pin keeps it on top of other windows. Rollup/Rolldown is still available when locked. Note that all frames are unmovable unless **Edit Frames Mode** is on.

**Content Lock**: Right-click → "Lock (prevent changes)" stops accidental edits — a Note becomes read-only, an Image frame refuses new pictures, and Data/Portal frames refuse drops.



## Advanced usage

Right clicking any shortcut the following usage options are available:
**Run as Administrator :** Executes the target program using administrator rights. Note: This uses also the passed arguments on the icon.

**Always run as Administrator :** Marks the shortcut to be executed using administrator rights on each launch. Note: This uses also the passed arguments on the icon.

**Copy Path -> Folder Path** : Copies the target folder path

**Copy Path -> Full Path** : Copies the target file path

**Open target folder** : Open target folder in file explorer



## Import/Export Frames

Click on the heart ❤️ menu and select "Export this frame" to export a frame into a  *.frame file.  The file is written to the "Exports" subfolder of the data folder (see File Locations).
To import a frame exported previously click on the heart ❤️ menu and select "Import a frame", browse for a *.frame file and import it.



---

## Application Settings

Access settings through the system tray icon → "Options":

### General Tab

**Startup:** Start with Windows.

**Selections:** Single Click to Launch (restart required) · Enable Snap Near Frames · Enable Dimension Snap · Snap frames to grid · Enable Tray Icon · Use Recycle Bin on Portal Frames 'Delete item' · Show 'New Frame' in Desktop Context Menu · Enable Portal Frames Watermark · Disable Frame Scrollbars.

**Virtual Desktops:** Automatically Switch Profiles with Virtual Desktop.

### Style & FX Tab

**Appearance:** Enable Chameleon Mode (frames take the dominant colour of your wallpaper and follow it when the wallpaper changes) · Frame Tint and Menu Tint sliders · default Color and Launch Effect.

**Auto-Hide Frames / Idle Fade-Out / Idle Auto-Roll:** hide, fade or roll frames up after a period of inactivity, with a configurable wake-up hover delay.

**Desktop Icon Visibility:** hide the native desktop icons while the program runs or while frames are hidden; double-click the empty desktop to show/hide them.

**Frames:** Enable Show/Hide all frames hotkey · Striped rows in Portal Details view.

**Icons:** pick the menu, position-lock and filter glyphs and the tray icon style.

### Tools Tab

**Backup:** Backup · Restore... · Open Backups Folder · Automatic Backup (Daily).

**Maintenance:** Screen Bound Frames (pull every frame back onto a visible monitor).

**Reset:** Reset Styles · Clear All Data.

### Profiles Tab

Create, rename, reorder and delete profiles, and *Empty Frames to Desktop* for frames that store files.

Naming a profile the same as a Windows virtual desktop activates that profile automatically when you switch to that desktop (with General → *Automatically Switch Profiles with Virtual Desktop* enabled); desktops without a matching profile fall back to Default.

### Hotkeys Tab

Profile switching hotkeys (direct profile 0–9, previous, next). Changes take effect after a restart.

### Smart Desktop Tab

Enable Auto-Organize · Show execution toast notifications · Arrange Now · Arrange Frames. See [Tips & Tricks](tips.md#smart-desktop-auto-organize).

### Text Frames Tab

Add and remove Text frames. Editing (template, font, colour, draw mode, refresh, opacity) happens in the shared Customize dialog — open it from this tab's *Customize...* button, or right-click the Text frame in Edit Frames Mode → *Customize...*.

### Look Deeper Tab

Enable logging · Open Log · log level and per-category switches.

---

## Troubleshooting

### Common Issues

**Elements on help forms are not aligned properly **

1. Check the resolution scaling.

2. Set scaling to 100% if it is possible.

   This is an ongoing development and will be fixed in future versions 

**Performance Issues**

1. Disable logging if enabled. Log can delay the program performance

2. Set launch effect to Zoom or FadeOut

   

**Configuration Issues**

1. Reset the program: Options → Tools → *Reset Styles* (keeps your frames) or *Clear All Data* (factory reset)



**Portal Frames Issues**

1. Verify the path exists and is accessible
2. Delete the Portal frame and create it again



---

## Tips for Best Use

### Organization Strategies

**By Category:**

- Create Frames for different types of applications (Games, Work, Utilities)
- Use color coding to visually distinguish categories
- Keep related items together for easy access

**By Frequency:**

- Place frequently used items in easily accessible Frames
- Use larger icon sizes for important applications
- Consider using launch effects for visual feedback

**By Project:**

- Create temporary Frames for specific projects

- Include all related files, documents, and tools

  

---

## Technical Information

### File Locations

**Data root:** beside `DesktopPossible.exe` for the portable zip; `%LocalAppData%\DesktopPossible\Data` for the MSI install.

Inside the data root:

- `ProfileOptions.json`: the list of profiles and which one is active
- `Profiles\<name>\frames.json`: that profile's frames and layout
- `Profiles\<name>\options.json`: that profile's settings (see [Tweaks](tweaks.md))
- `Profiles\<name>\Shortcuts\`: the shortcut files held by Data frames
- `Profiles\<name>\Backups\`: timestamped backups
- `Exports\`: exported `.frame` files
- `Desktop_Frames.log`: application log file

**Dependencies:** none to install — .NET 10 (WPF + Windows Forms) is bundled in the executable.

---

### Version Information

The application version is displayed in the About dialog, accessible through the system tray menu. This information is useful when reporting issues or checking for updates.

---

*This manual covers the core functionality of DesktopPossible. The application includes many additional features and customization options discoverable through exploration and experimentation.*

*Ctrl + Click is your friend* <br>


Originally written by Nikos Georgousis (Desktop Frames +, Nov 2025); updated for DesktopPossible.

