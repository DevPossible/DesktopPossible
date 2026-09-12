// Inherited upstream code predates nullable reference types: nullable WARNINGS are off for this
// file until it is annotated (annotations remain valid). New files are fully nullable-clean.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    public enum IconVisibilityEffect
    {
        None, Glow, Shadow, Outline, AngelGlow, ColoredGlow, StrongShadow
    }

    /// <summary>
    /// Manages application settings with Strict "Hard Switch" Master support.
    /// </summary>
    public static class SettingsManager
    {
        /// <summary>True when this launch found no settings file at all (fresh install). Not persisted.</summary>
        public static bool IsFirstRun { get; private set; }

        // --- Properties ---
        public static bool EnableAutoBackup { get; set; } = true;
        public static DateTime LastAutoBackupDate { get; set; } = DateTime.MinValue;
        // Automatic backups kept before the oldest are deleted (manual backups are never auto-deleted).
        public static int MaxBackupCount { get; set; } = 7;
        // A backup archive larger than this is refused with an error (raise the limit to allow it).
        public static int MaxBackupSizeMB { get; set; } = 100;
        public static bool ShowPortalExtensions { get; set; } = false;
        public static bool NoWildcardsOnPortalFilter { get; set; } = false;
        public static bool IsSnapEnabled { get; set; } = true;
        public static bool ShowBackgroundImageOnPortalFrames { get; set; } = true;
        public static bool UseRecycleBin { get; set; } = true;
        public static bool ShowInTray { get; set; } = true;
        public static int TintValue { get; set; } = 85;
        public static int MenuTintValue { get; set; } = 30;
        public static int MenuIcon { get; set; } = 0;
        public static int LockIcon { get; set; } = 0;
        // Filter/search title-bar icon: 0 = magnifier, 1 = funnel, 2 = boxed magnifier.
        public static int FilterIcon { get; set; } = 0;
        public static string SelectedColor { get; set; } = "Gray";
        public static bool IsLogEnabled { get; set; } = false;
        public static int MaxDisplayNameLength { get; set; } = 20;
        public static int PortalBackgroundOpacity { get; set; } = 30;
        public static bool EnableIconGlowEffect { get; set; } = true;
        public static bool DisableSingleInstance { get; set; } = false;
        public static bool DeletePreviousLogOnStart { get; set; } = false;
        public static bool EnableBackgroundValidationLogging { get; set; } = false;
        public static bool SuppressLaunchWarnings { get; set; } = false;
        public static bool DisableFrameScrollbars { get; set; } = true;

        public static bool EnableChameleonMode { get; set; } = true;
        public static bool EnableProfileAutomation { get; set; } = false;
        public static bool EnableVirtualDesktopAutomation { get; set; } = true;

        public static bool EnableAutoOrganize { get; set; } = false;

        public static bool EnableAutoOrganizeNotifications { get; set; } = true;

        // --- NEW: Hidden Option for Manual Repositioning ---
        public static bool AllowAutoReposition { get; set; } = true;

        /// <summary>
        /// Remember where frames sit on each display configuration and restore them when it
        /// comes back (docking, monitor changes, resolution/scaling changes, RDP). See
        /// DisplayLayoutManager.
        /// </summary>
        public static bool EnableDisplayLayoutMemory { get; set; } = true;

        // --- Global Frame Edit Mode (default OFF: frames and text frames are position/size locked
        //     until the user turns editing on from the tray menu; app-global, so it survives
        //     profile / virtual-desktop switches) ---
        public static bool FrameEditMode { get; set; } = false;

        // --- One-time starter: the System Info text frame seeded on the Default profile ---
        public static bool SystemInfoFrameCreated { get; set; } = false;

        // --- Snap frame position/size to the icon grid (FrameGrid) when moving/resizing ---
        public static bool SnapFramesToGrid { get; set; } = true;

        // --- App-level, once-ever flag: the instructional "Startup Tips" frame has been seeded ---
        public static bool InstructionalFrameCreated { get; set; } = false;

        // --- NEW: Hidden Option for Square Corners ---
        public static bool FramesWithNoRoundCorners { get; set; } = false;

        // --- NEW: Context Menu Option ---
        public static bool EnableContextMenu { get; set; } = false;

        // --- NEW: Auto-Hide Frames Options ---
        public static bool AutoHideFrames { get; set; } = false;
        public static int AutoHideTime { get; set; } = 60;
        public static bool AutoResetHideTimer { get; set; } = true;
        public static bool HideFlashEffect { get; set; } = true;

        // --- NEW: Desktop Icon Visibility ---
        public static bool HideDesktopElementsOnStart { get; set; } = false;
        public static bool HideDesktopElementsOnAllFramesHide { get; set; } = false;
        public static bool ShowDesktopDot { get; set; } = true;
        // Fences-style: double-click empty desktop toggles the native desktop icons.
        public static bool ToggleDesktopIconsOnDoubleClick { get; set; } = false;
        // Global default for zebra striping in Portal Details view (per-frame override via DetailsStriped).
        public static bool PortalDetailsStriped { get; set; } = true;
        // System-tray icon glyph style: "Nested" | "Stacked" | "Grid" (theme-aware monochrome, drawn in GDI+).
        public static string TrayIconStyle { get; set; } = "Nested";
        // How dragged image files are added to Image frames: "Copy" (default) | "Reference" | "Ask".
        public static string ImageDropMode { get; set; } = "Copy";

        // --- NEW: Idle Fade-Out Settings ---
        public static bool FramesFadeOutFx { get; set; } = true;
        public static double FadeOutFxTargetAlpha { get; set; } = 0.3;
        public static int FadeOutTime { get; set; } = 5;
        /// <summary>Hover debounce for waking a faded frame (ms): the pointer must rest on the frame
        /// this long before it returns to full opacity, so a pointer just passing through doesn't wake it.</summary>
        public static int FadeWakeDelayMs { get; set; } = 250;

        // --- NEW: Hidden Auto-Roll Settings ---
        public static int AutoRollTime { get; set; } = 2;
        public static IconVisibilityEffect IconVisibilityEffect { get; set; } = IconVisibilityEffect.None;
        public static bool ExportShortcutsOnFrameDeletion { get; set; } = false;
        public static bool DeleteOriginalShortcutsOnDrop { get; set; } = false;

        public static bool EnableProfileHotkeys { get; set; } = false;
        public static bool AltGrWarningShown { get; set; } = false;
        // Show/Hide all frames hotkey (default Ctrl+Alt+H)
        public static bool EnableToggleFramesHotkey { get; set; } = true;
        public static int ToggleFramesKey { get; set; } = 0x48; // H
        public static string ToggleFramesModifier { get; set; } = "ctrl+alt";
        public static bool EnableDimensionSnap { get; set; } = true;
        public static bool SingleClickToLaunch { get; set; } = true;

  
        public static LaunchEffectsManager.LaunchEffect LaunchEffect { get; set; } = LaunchEffectsManager.LaunchEffect.Zoom;
        public static LogManager.LogLevel MinLogLevel { get; set; } = LogManager.LogLevel.Info;
        public static List<LogManager.LogCategory> EnabledLogCategories { get; set; } = new List<LogManager.LogCategory>
        {
            LogManager.LogCategory.General,
            LogManager.LogCategory.Error,
            LogManager.LogCategory.ImportExport,
            LogManager.LogCategory.Settings
        };

        private static string _activeOptionsPath;

        public static void LoadSettings()
        {
            // 1. DETERMINE SOURCE
            // Application settings are GLOBAL — profiles hold content (frames, layout),
            // not options. One MasterOptions.json at the app root serves every profile;
            // without this, toggles like virtual-desktop auto-switching silently "reset"
            // on every profile switch (each profile's options.json had its own copy).
            // Migration: when the master file doesn't exist yet, it is seeded from the
            // current profile's legacy options.json (the settings the user was running
            // with) — legacy per-profile files are left in place but no longer read.
            string appRoot = AppPaths.DataRoot;
            string masterPath = Path.Combine(appRoot, "MasterOptions.json");
            string localPath = ProfileManager.GetProfileFilePath("options.json");

            // Fresh install: no settings file anywhere yet. Sticky for the process lifetime —
            // LoadSettings re-runs on profile reloads, by which time SaveSettings has created
            // the file. TrayManager uses this to enable start-with-Windows by default.
            if (!File.Exists(masterPath) && !File.Exists(localPath)) IsFirstRun = true;

            if (!File.Exists(masterPath))
            {
                try
                {
                    if (File.Exists(localPath))
                    {
                        AtomicFile.WriteAllText(masterPath, File.ReadAllText(localPath));
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings,
                            "Migrated per-profile options.json to app-level MasterOptions.json (settings are global; profiles hold content only).");
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings,
                        $"Failed to seed MasterOptions.json: {ex.Message}");
                }
            }

            _activeOptionsPath = masterPath;

            // 2. READ DATA
            try
            {
                if (File.Exists(_activeOptionsPath))
                {
                    string jsonContent = File.ReadAllText(_activeOptionsPath);
                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        try
                        {
                            var optionsData = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                            if (optionsData != null) ApplyJsonToProperties(optionsData);
                        }
                        catch { /* Corrupt file, defaults will apply */ }
                    }
                }

                // --- FIX: Universal File Hydration ---
                // By unconditionally saving after loading the properties into memory, we guarantee:
                // 1. 0-byte profile files are instantly populated with default JSON.
                // 2. MasterOptions.json / options.json files from older app versions 
                //    automatically get newly added settings injected into them.
                SaveSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
                if (string.IsNullOrEmpty(_activeOptionsPath)) _activeOptionsPath = masterPath;
                SaveSettings();
            }
        }

        public static void SaveSettings()
        {
            if (string.IsNullOrEmpty(_activeOptionsPath))
            {
                _activeOptionsPath = ProfileManager.GetProfileFilePath("options.json");
            }

            try
            {
                // 3. WRITE TO THE ACTIVE SOURCE (Includes AllowAutoReposition now)
                var optionsData = GetCurrentPropertiesAsObject();
                string formattedJson = JsonConvert.SerializeObject(optionsData, Formatting.Indented);

                // Skip the disk write when the file already holds exactly this content.
                // LoadSettings calls SaveSettings unconditionally (to back-fill new keys into
                // old files) — without this check every profile switch rewrote options.json.
                try
                {
                    if (File.Exists(_activeOptionsPath) && File.ReadAllText(_activeOptionsPath) == formattedJson)
                        return;
                }
                catch { /* unreadable — fall through and write */ }

                AtomicFile.WriteAllText(_activeOptionsPath, formattedJson);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"Failed to save settings to {_activeOptionsPath}: {ex.Message}");
            }
        }

        // --- Helpers ---

        private static object GetCurrentPropertiesAsObject()
        {
            return new
            {
                IsSnapEnabled,
                ShowBackgroundImageOnPortalFrames,
                ShowInTray,
                UseRecycleBin,
                TintValue,
                MenuTintValue,
                MenuIcon,
                LockIcon,
                FilterIcon,
                SelectedColor,
                IsLogEnabled,
                SingleClickToLaunch,
                EnableDimensionSnap,
                PortalBackgroundOpacity,
                MaxDisplayNameLength,
                EnableIconGlowEffect,
                IconVisibilityEffect = IconVisibilityEffect.ToString(),
                LaunchEffect = LaunchEffect.ToString(),
                MinLogLevel = MinLogLevel.ToString(),
                EnabledLogCategories = EnabledLogCategories.Select(c => c.ToString()).ToList(),
                DeletePreviousLogOnStart,
                SuppressLaunchWarnings,
                EnableBackgroundValidationLogging,
                DisableSingleInstance,
                DisableFrameScrollbars,
                ExportShortcutsOnFrameDeletion,
                DeleteOriginalShortcutsOnDrop,
                NoWildcardsOnPortalFilter,
                ShowPortalExtensions,
                EnableAutoBackup,
                LastAutoBackupDate,
                MaxBackupCount,
                MaxBackupSizeMB,
                AutoRollTime,

                AllowAutoReposition,
                EnableDisplayLayoutMemory,
                FrameEditMode,
                SnapFramesToGrid,
                InstructionalFrameCreated,
                SystemInfoFrameCreated,
                FramesWithNoRoundCorners,
                EnableProfileAutomation,
                EnableVirtualDesktopAutomation,
                EnableChameleonMode,
                EnableAutoOrganize,
                EnableAutoOrganizeNotifications,
                // NEW
                EnableContextMenu,

                // Auto-Hide
                AutoHideFrames,
                AutoHideTime,
                AutoResetHideTimer,
                HideFlashEffect,
                // Desktop Icon Visibility
                HideDesktopElementsOnStart,
                HideDesktopElementsOnAllFramesHide,
                ShowDesktopDot,
                ToggleDesktopIconsOnDoubleClick,
                PortalDetailsStriped,
                TrayIconStyle,
                ImageDropMode,
                // Idle Fade-Out
                FramesFadeOutFx,
                FadeOutFxTargetAlpha,
                FadeOutTime,
                FadeWakeDelayMs,

                // Global Hotkeys
                EnableProfileHotkeys,
                AltGrWarningShown, // --- NEW ---
                EnableToggleFramesHotkey,
                ToggleFramesKey,
                ToggleFramesModifier,
                ProfileSwitchModifier,
                ProfileSwitchKeys,
                ProfilePrevModifier,
                ProfilePrevKey,
                ProfileNextModifier,
                ProfileNextKey
            };
        }

        private static void ApplyJsonToProperties(dynamic data)
        {
            try { EnableAutoBackup = data.EnableAutoBackup ?? true; } catch { EnableAutoBackup = true; }
            try { LastAutoBackupDate = data.LastAutoBackupDate ?? DateTime.MinValue; } catch { LastAutoBackupDate = DateTime.MinValue; }
            try { MaxBackupCount = data.MaxBackupCount ?? 7; } catch { MaxBackupCount = 7; }
            if (MaxBackupCount < 1 || MaxBackupCount > 999) MaxBackupCount = 7;
            try { MaxBackupSizeMB = data.MaxBackupSizeMB ?? 100; } catch { MaxBackupSizeMB = 100; }
            if (MaxBackupSizeMB < 1 || MaxBackupSizeMB > 100000) MaxBackupSizeMB = 100;
            try { IsSnapEnabled = data.IsSnapEnabled ?? true; } catch { IsSnapEnabled = true; }
            try { ShowBackgroundImageOnPortalFrames = data.ShowBackgroundImageOnPortalFrames ?? true; } catch { ShowBackgroundImageOnPortalFrames = true; }
            try { ShowInTray = data.ShowInTray ?? true; } catch { ShowInTray = true; }
            try { UseRecycleBin = data.UseRecycleBin ?? true; } catch { UseRecycleBin = true; }
            try { TintValue = data.TintValue ?? 85; } catch { TintValue = 85; }
            try { MenuTintValue = data.MenuTintValue ?? 30; } catch { MenuTintValue = 30; }
            try { MenuIcon = data.MenuIcon ?? 0; } catch { MenuIcon = 0; }
            try { LockIcon = data.LockIcon ?? 0; } catch { LockIcon = 0; }
            if (LockIcon < 0 || LockIcon > 1) LockIcon = 0; // old emoji configs stored 0-3; new scheme is 0=map pin, 1=pushpin
            try { FilterIcon = data.FilterIcon ?? 0; } catch { FilterIcon = 0; }
            if (FilterIcon < 0 || FilterIcon > 2) FilterIcon = 0;
            try { SelectedColor = data.SelectedColor ?? "Gray"; } catch { SelectedColor = "Gray"; }
            try { IsLogEnabled = data.IsLogEnabled ?? false; } catch { IsLogEnabled = false; }
            try { SingleClickToLaunch = data.SingleClickToLaunch ?? true; } catch { SingleClickToLaunch = true; }
            try { EnableDimensionSnap = data.EnableDimensionSnap ?? true; } catch { EnableDimensionSnap = true; }
            try { PortalBackgroundOpacity = data.PortalBackgroundOpacity ?? 30; } catch { PortalBackgroundOpacity = 30; }
            try { EnableIconGlowEffect = data.EnableIconGlowEffect ?? true; } catch { EnableIconGlowEffect = true; }
            try { DisableFrameScrollbars = data.DisableFrameScrollbars ?? true; } catch { DisableFrameScrollbars = true; }
            try { ExportShortcutsOnFrameDeletion = data.ExportShortcutsOnFrameDeletion ?? false; } catch { ExportShortcutsOnFrameDeletion = false; }
            try { DeleteOriginalShortcutsOnDrop = data.DeleteOriginalShortcutsOnDrop ?? false; } catch { DeleteOriginalShortcutsOnDrop = false; }
            try { ShowPortalExtensions = data.ShowPortalExtensions ?? false; } catch { ShowPortalExtensions = false; }
            try { NoWildcardsOnPortalFilter = data.NoWildcardsOnPortalFilter ?? false; } catch { NoWildcardsOnPortalFilter = false; }
            try { AutoRollTime = data.AutoRollTime ?? 2; } catch { AutoRollTime = 2; }

            try { AllowAutoReposition = data.AllowAutoReposition ?? true; } catch { AllowAutoReposition = true; }
            try { EnableDisplayLayoutMemory = data.EnableDisplayLayoutMemory ?? true; } catch { EnableDisplayLayoutMemory = true; }
            try { FrameEditMode = data.FrameEditMode ?? false; } catch { FrameEditMode = false; }
            try { SnapFramesToGrid = data.SnapFramesToGrid ?? true; } catch { SnapFramesToGrid = true; }
            try { InstructionalFrameCreated = data.InstructionalFrameCreated ?? false; } catch { InstructionalFrameCreated = false; }
            try { SystemInfoFrameCreated = data.SystemInfoFrameCreated ?? false; } catch { SystemInfoFrameCreated = false; }
            try { FramesWithNoRoundCorners = data.FramesWithNoRoundCorners ?? false; } catch { FramesWithNoRoundCorners = false; }
            try { EnableProfileAutomation = data.EnableProfileAutomation ?? false; } catch { EnableProfileAutomation = false; }
            try { EnableVirtualDesktopAutomation = data.EnableVirtualDesktopAutomation ?? true; } catch { EnableVirtualDesktopAutomation = true; }
            try { EnableChameleonMode = data.EnableChameleonMode ?? true; } catch { EnableChameleonMode = true; }
            try { EnableAutoOrganize = data.EnableAutoOrganize ?? false; } catch { EnableAutoOrganize = false; }
            try { EnableAutoOrganizeNotifications = data.EnableAutoOrganizeNotifications ?? true; } catch { EnableAutoOrganizeNotifications = true; }

            // NEW
            try { EnableContextMenu = data.EnableContextMenu ?? false; } catch { EnableContextMenu = false; }

            // Auto-Hide
            try { AutoHideFrames = data.AutoHideFrames ?? false; } catch { AutoHideFrames = false; }
            try { AutoHideTime = data.AutoHideTime ?? 60; } catch { AutoHideTime = 60; }
            try { AutoResetHideTimer = data.AutoResetHideTimer ?? true; } catch { AutoResetHideTimer = true; }
            try { HideFlashEffect = data.HideFlashEffect ?? true; } catch { HideFlashEffect = true; }

            // Desktop Icon Visibility
            try { HideDesktopElementsOnStart = data.HideDesktopElementsOnStart ?? false; } catch { HideDesktopElementsOnStart = false; }
            try { HideDesktopElementsOnAllFramesHide = data.HideDesktopElementsOnAllFramesHide ?? false; } catch { HideDesktopElementsOnAllFramesHide = false; }
            try { ShowDesktopDot = data.ShowDesktopDot ?? true; } catch { ShowDesktopDot = true; }
            try { ToggleDesktopIconsOnDoubleClick = data.ToggleDesktopIconsOnDoubleClick ?? false; } catch { ToggleDesktopIconsOnDoubleClick = false; }
            try { PortalDetailsStriped = data.PortalDetailsStriped ?? true; } catch { PortalDetailsStriped = true; }
            try { TrayIconStyle = (string)(data.TrayIconStyle ?? "Nested"); } catch { TrayIconStyle = "Nested"; }
            try { ImageDropMode = (string)(data.ImageDropMode ?? "Copy"); } catch { ImageDropMode = "Copy"; }

            // Idle Fade-Out
            try { FramesFadeOutFx = data.FramesFadeOutFx ?? true; } catch { FramesFadeOutFx = true; }
            try { FadeOutFxTargetAlpha = data.FadeOutFxTargetAlpha ?? 0.3; } catch { FadeOutFxTargetAlpha = 0.3; }
            try { FadeOutTime = data.FadeOutTime ?? 5; } catch { FadeOutTime = 5; }
            try { FadeWakeDelayMs = data.FadeWakeDelayMs ?? 250; } catch { FadeWakeDelayMs = 250; }

            try
            {
                int value = data.MaxDisplayNameLength ?? 20;
                MaxDisplayNameLength = Math.Max(5, Math.Min(50, value));
            }
            catch { MaxDisplayNameLength = 20; }

            try { DisableSingleInstance = data.DisableSingleInstance ?? false; } catch { DisableSingleInstance = false; }

            try
            {
                string effectName = data.IconVisibilityEffect?.ToString() ?? "None";
                if (Enum.TryParse<IconVisibilityEffect>(effectName, true, out IconVisibilityEffect parsedEffect))
                    IconVisibilityEffect = parsedEffect;
                else
                    IconVisibilityEffect = IconVisibilityEffect.None;
            }
            catch { IconVisibilityEffect = IconVisibilityEffect.None; }

            try
            {
                LaunchEffect = data.LaunchEffect != null
                    ? Enum.Parse(typeof(LaunchEffectsManager.LaunchEffect), data.LaunchEffect.ToString())
                    : LaunchEffectsManager.LaunchEffect.Zoom;
            }
            catch { LaunchEffect = LaunchEffectsManager.LaunchEffect.Zoom; }

            try { DeletePreviousLogOnStart = data.DeletePreviousLogOnStart ?? false; } catch { DeletePreviousLogOnStart = false; }
            try { SuppressLaunchWarnings = data.SuppressLaunchWarnings ?? false; } catch { SuppressLaunchWarnings = false; }
            try { EnableBackgroundValidationLogging = data.EnableBackgroundValidationLogging ?? false; } catch { EnableBackgroundValidationLogging = false; }

            try
            {
                MinLogLevel = data.MinLogLevel != null
                    ? Enum.Parse(typeof(LogManager.LogLevel), data.MinLogLevel.ToString())
                    : LogManager.LogLevel.Info;
            }
            catch { MinLogLevel = LogManager.LogLevel.Info; }

            try
            {
                EnabledLogCategories = data.EnabledLogCategories != null
                    ? ((JArray)data.EnabledLogCategories)
                        .Select(c => Enum.Parse(typeof(LogManager.LogCategory), c.ToString()))
                        .Cast<LogManager.LogCategory>()
                        .ToList()
                    : new List<LogManager.LogCategory> { LogManager.LogCategory.General, LogManager.LogCategory.Error, LogManager.LogCategory.ImportExport, LogManager.LogCategory.Settings };
            }
            catch
            {
                EnabledLogCategories = new List<LogManager.LogCategory> { LogManager.LogCategory.General, LogManager.LogCategory.Error, LogManager.LogCategory.ImportExport, LogManager.LogCategory.Settings };
            }


            // Global Hotkeys
            // --- FIX: Read existing, but default to false for fresh installs ---
            try { EnableProfileHotkeys = data.EnableProfileHotkeys ?? false; } catch { EnableProfileHotkeys = false; }
            try { AltGrWarningShown = data.AltGrWarningShown ?? false; } catch { AltGrWarningShown = false; }
            try { EnableToggleFramesHotkey = data.EnableToggleFramesHotkey ?? true; } catch { EnableToggleFramesHotkey = true; }
            try { ToggleFramesKey = data.ToggleFramesKey ?? 0x48; } catch { ToggleFramesKey = 0x48; }
            try { if (data.ToggleFramesModifier != null) ToggleFramesModifier = data.ToggleFramesModifier.ToString(); } catch { }
            try { if (data.ProfileSwitchModifier != null) ProfileSwitchModifier = data.ProfileSwitchModifier.ToString(); } catch { }
            try { if (data.ProfileSwitchKeys != null) ProfileSwitchKeys = ((JArray)data.ProfileSwitchKeys).Select(x => (int)x).ToArray(); } catch { }
            try { if (data.ProfilePrevModifier != null) ProfilePrevModifier = data.ProfilePrevModifier.ToString(); } catch { }
            try { if (data.ProfilePrevKey != null) ProfilePrevKey = (int)data.ProfilePrevKey; } catch { }
            try { if (data.ProfileNextModifier != null) ProfileNextModifier = data.ProfileNextModifier.ToString(); } catch { }
            try { if (data.ProfileNextKey != null) ProfileNextKey = (int)data.ProfileNextKey; } catch { }

            SanitizeHotkeys(); // Ensure nulls or invalid manual edits are safely overwritten
        }

        public static void SetMinLogLevel(LogManager.LogLevel level)
        {
            MinLogLevel = level;
            SaveSettings();
        }

        public static void SetEnabledLogCategories(List<LogManager.LogCategory> categories)
        {
            EnabledLogCategories = categories;
            SaveSettings();
        }

        #region Global Hotkey Configurations
        public static string ProfileSwitchModifier { get; set; } = "Control, Alt";
        public static int[] ProfileSwitchKeys { get; set; } = new int[] { 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 }; // Defaults to standard 0-9

        public static string ProfilePrevModifier { get; set; } = "Control, Alt";
        public static int ProfilePrevKey { get; set; } = 0xBC; // Default: VK_OEM_COMMA

        public static string ProfileNextModifier { get; set; } = "Control, Alt";
        public static int ProfileNextKey { get; set; } = 0xBE; // Default: VK_OEM_PERIOD

        /// <summary>
        /// Validates and repairs hotkey configuration to prevent hook crashes from manual JSON edits.
        /// </summary>
        public static void SanitizeHotkeys()
        {
            // Ensure modifiers aren't completely blank or null
            if (string.IsNullOrWhiteSpace(ProfileSwitchModifier)) ProfileSwitchModifier = "Control, Alt";
            if (string.IsNullOrWhiteSpace(ProfilePrevModifier)) ProfilePrevModifier = "Control, Alt";
            if (string.IsNullOrWhiteSpace(ProfileNextModifier)) ProfileNextModifier = "Control, Alt";

            // Fallback for missing or broken Profile Switch Array
            if (ProfileSwitchKeys == null || ProfileSwitchKeys.Length < 10)
                ProfileSwitchKeys = new int[] { 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 };

            // Fallback for totally invalid Virtual Key codes (must be within 0x01 and 0xFE)
            if (ProfilePrevKey <= 0 || ProfilePrevKey > 254) ProfilePrevKey = 0xBC;
            if (ProfileNextKey <= 0 || ProfileNextKey > 254) ProfileNextKey = 0xBE;
        }

        /// <summary>
        /// Injects the current hotkey settings into every available profile's options.json.
        /// This ensures uniform hotkey behavior regardless of the active profile, while respecting MasterOptions override.
        /// </summary>
        public static void BroadcastHotkeysToAllProfiles()
        {
            // Settings are app-global now (one MasterOptions.json; profiles hold
            // content + title only — per-frame display lives in frames.json).
            // Per-profile options.json files are no longer read, so broadcasting
            // into them is dead work. Kept as a no-op for call-site compatibility.
            return;
#pragma warning disable CS0162 // legacy body retained for reference

            try
            {
                string appRoot = AppPaths.DataRoot;
                string profilesDir = Path.Combine(appRoot, "Profiles");

                if (!Directory.Exists(profilesDir)) return;

                foreach (string dir in Directory.GetDirectories(profilesDir))
                {
                    string optionsFile = Path.Combine(dir, "options.json");
                    if (File.Exists(optionsFile))
                    {
                        try
                        {
                            string jsonContent = File.ReadAllText(optionsFile);
                            JObject data = JsonConvert.DeserializeObject<JObject>(jsonContent);
                            if (data == null) continue;

                            // Overwrite strictly the hotkey values
                            data["ProfileSwitchModifier"] = ProfileSwitchModifier;
                            data["ProfileSwitchKeys"] = JArray.FromObject(ProfileSwitchKeys ?? new int[] { 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 });
                            data["ProfilePrevModifier"] = ProfilePrevModifier;
                            data["ProfilePrevKey"] = ProfilePrevKey;
                            data["ProfileNextModifier"] = ProfileNextModifier;
                            data["ProfileNextKey"] = ProfileNextKey;
                            data["EnableProfileHotkeys"] = EnableProfileHotkeys;
                            data["AltGrWarningShown"] = AltGrWarningShown; // --- NEW ---
                            data["FramesFadeOutFx"] = FramesFadeOutFx;
                            data["FadeOutFxTargetAlpha"] = FadeOutFxTargetAlpha;
                            data["FadeOutTime"] = FadeOutTime;
                            data["FadeWakeDelayMs"] = FadeWakeDelayMs;

                            AtomicFile.WriteAllText(optionsFile, JsonConvert.SerializeObject(data, Formatting.Indented));
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"Failed to broadcast hotkeys to {optionsFile}: {ex.Message}");
                        }
                    }
                }
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings, "Global hotkeys successfully broadcasted to all individual profiles.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"Critical failure broadcasting hotkeys: {ex.Message}");
            }
        }

        #endregion

    }


}