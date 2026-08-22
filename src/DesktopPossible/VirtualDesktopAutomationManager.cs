// Inherited upstream code predates nullable reference types: nullable WARNINGS are off for this
// file until it is annotated (annotations remain valid). New files are fully nullable-clean.
#nullable disable warnings

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Desktop_Frames
{
    /// <summary>
    /// Automatically switches profiles to follow Windows virtual desktops
    /// (Win+Ctrl+Left/Right). Mapping is by NAME: when the current desktop's name
    /// matches a profile name (case-insensitive), that profile is activated;
    /// otherwise the "Default" profile is activated. Unnamed desktops resolve to
    /// "Desktop {position}" (e.g. "Desktop 2"), so a profile named "Desktop 2"
    /// matches the second desktop.
    ///
    /// Enabling the feature applies the mapping immediately for the current
    /// desktop; disabling it switches back to the Default profile.
    ///
    /// DETECTION: uses only build-stable surfaces — the documented shell COM API
    /// IVirtualDesktopManager (to find which desktop the user is on, probed via
    /// the visible top-level windows) and the Explorer registry keys under
    /// HKCU\...\Explorer\VirtualDesktops (desktop order and user-given names).
    /// The undocumented IVirtualDesktopManagerInternal interfaces (and wrapper
    /// libraries around them) break on new Windows builds — do not reintroduce
    /// them. Detection polls a DispatcherTimer on the UI thread, mirroring
    /// AutomationManager's polling pattern, so no cross-thread marshaling is
    /// needed.
    /// </summary>
    public static class VirtualDesktopAutomationManager
    {
        private const string DefaultProfileName = "Default";
        private const string VirtualDesktopsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private static DispatcherTimer _timer;
        private static IVirtualDesktopManager _manager;
        private static Guid _lastDesktopId = Guid.Empty;

        public static void Start()
        {
            // SAFETY: everything wrapped so a COM/registry failure can never take the app down.
            try
            {
                if (_timer != null) return;

                // Respect the Master Toggle
                if (!SettingsManager.EnableVirtualDesktopAutomation) return;

                _manager = CreateManager();
                if (_manager == null)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "VirtualDesktopAutomation: IVirtualDesktopManager unavailable on this system. Automation disabled.");
                    return;
                }

                _lastDesktopId = Guid.Empty; // force an immediate apply on the first tick
                _timer = new DispatcherTimer { Interval = PollInterval };
                _timer.Tick += (s, e) => Poll();
                _timer.Start();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "VirtualDesktopAutomation: started (following virtual desktops by name).");

                // Apply immediately for the desktop we are on right now, so enabling the
                // setting (or app startup with it enabled) takes effect without requiring
                // a desktop switch first.
                Poll();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: failed to start: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops following virtual desktops. When <paramref name="switchToDefault"/> is
        /// true (user turned the feature off), the Default profile is activated.
        /// </summary>
        public static void Stop(bool switchToDefault = false)
        {
            try
            {
                _timer?.Stop();
            }
            catch { }
            _timer = null;
            _manager = null;
            _lastDesktopId = Guid.Empty;

            if (switchToDefault)
            {
                PerformProfileSwitch(DefaultProfileName);
            }
        }

        /// <summary>Runs on the UI thread (DispatcherTimer). Detects desktop changes
        /// and applies the name-based profile mapping.</summary>
        private static void Poll()
        {
            try
            {
                if (!SettingsManager.EnableVirtualDesktopAutomation)
                {
                    Stop();
                    return;
                }

                // A switch is already running — this poll can tick re-entrantly, because
                // ReloadFrames pumps messages (DoEvents). Skip the tick entirely; since
                // _lastDesktopId is only committed on success, the switch retries next tick.
                if (ProfileManager.IsSwitching) return;

                // The Options dialog edits the CURRENT profile; switching underneath it would
                // make Save push the old profile's snapshot into the new profile's options.json.
                // Suspend VD-driven switching until the dialog closes.
                if (OptionsFormManager.IsOpen) return;

                // Same hazard while a categorize-and-sort pass is running: it mutates the
                // CURRENT profile's frame data (creating frames, moving items); a switch
                // underneath it would tear that data out from under the in-flight moves.
                if (AppCategorizer.IsSorting) return;

                // Defer while the user is interacting: Mouse.Captured covers WPF context menus
                // (and any capture-based interaction); the pressed left button covers DragMove's
                // modal loop — DispatcherTimer ticks DO pump during DragMove, and destroying the
                // window being dragged would lose its final position.
                if (System.Windows.Input.Mouse.Captured != null ||
                    System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                    return;

                Guid current = GetCurrentDesktopId();
                if (current == Guid.Empty || current == _lastDesktopId) return;

                string desktopName = GetDesktopDisplayName(current);
                if (string.IsNullOrWhiteSpace(desktopName)) return;

                string target = ProfileManager.GetProfiles()
                    .FirstOrDefault(p => p.Name.Equals(desktopName, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? DefaultProfileName;

                // Info (not Debug): fires only on actual desktop changes — low volume,
                // and the primary signal when diagnosing switching issues.
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: now on desktop '{desktopName}' ({current}) -> profile '{target}'.");

                // Commit the desktop id only after the switch succeeded (already-active counts
                // as success), so a failed or refused switch is retried on the next tick.
                if (PerformProfileSwitch(target))
                {
                    _lastDesktopId = current;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: poll failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The current desktop's Guid. Primary source: Explorer's registry value
        /// CurrentVirtualDesktop (root VirtualDesktops key on current builds, the
        /// per-session SessionInfo key on older ones) — updated by Explorer on every
        /// switch and valid even for desktops with no windows on them. Fallback:
        /// probing visible top-level windows via IVirtualDesktopManager (cannot
        /// identify empty desktops). Returns Guid.Empty when undeterminable this
        /// tick — callers keep the last known id.
        /// </summary>
        private static Guid GetCurrentDesktopId()
        {
            // 1) Root key (Windows 11 current builds)
            Guid fromRegistry = ReadCurrentDesktopFromRegistry(VirtualDesktopsKeyPath);
            if (fromRegistry != Guid.Empty) return fromRegistry;

            // 2) Per-session key (older Windows 10/11 builds)
            fromRegistry = ReadCurrentDesktopFromRegistry(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{System.Diagnostics.Process.GetCurrentProcess().SessionId}\VirtualDesktops");
            if (fromRegistry != Guid.Empty) return fromRegistry;

            // 3) Window probe (cannot identify desktops that have no windows on them)
            return ProbeCurrentDesktopFromWindows();
        }

        private static Guid ReadCurrentDesktopFromRegistry(string keyPath)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    if (key?.GetValue("CurrentVirtualDesktop") is byte[] bytes && bytes.Length >= 16)
                    {
                        byte[] slice = new byte[16];
                        Array.Copy(bytes, 0, slice, 0, 16);
                        return new Guid(slice);
                    }
                }
            }
            catch { }
            return Guid.Empty;
        }

        private static Guid ProbeCurrentDesktopFromWindows()
        {
            var mgr = _manager;
            if (mgr == null) return Guid.Empty;

            uint ownPid = (uint)Environment.ProcessId;
            Guid found = Guid.Empty;
            NativeMethods.EnumWindows((hwnd, lparam) =>
            {
                try
                {
                    if (!NativeMethods.IsWindowVisible(hwnd)) return true;

                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == ownPid) return true; // skip our own frames (see summary)

                    if (mgr.GetWindowDesktopId(hwnd, out Guid id) == 0 && id != Guid.Empty &&
                        mgr.IsWindowOnCurrentVirtualDesktop(hwnd, out int onCurrent) == 0 && onCurrent == 1)
                    {
                        found = id;
                        return false; // stop enumerating
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>
        /// The desktop's user-given name (registry), or "Desktop {position}" when
        /// unnamed — matching the naming Windows shows in the Win+Tab switcher.
        /// </summary>
        private static string GetDesktopDisplayName(Guid desktopId)
        {
            try
            {
                using (var desktopKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    $@"{VirtualDesktopsKeyPath}\Desktops\{{{desktopId}}}"))
                {
                    string name = desktopKey?.GetValue("Name") as string;
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }

                // Unnamed: derive "Desktop {position}" from the ordered id list.
                using (var rootKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(VirtualDesktopsKeyPath))
                {
                    if (rootKey?.GetValue("VirtualDesktopIDs") is byte[] ids && ids.Length >= 16)
                    {
                        for (int i = 0; i + 16 <= ids.Length; i += 16)
                        {
                            byte[] slice = new byte[16];
                            Array.Copy(ids, i, slice, 0, 16);
                            if (new Guid(slice) == desktopId) return $"Desktop {(i / 16) + 1}";
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Runs on the UI thread. Returns true when the target profile is active
        /// afterwards (switched now, or already active); false when the switch was refused
        /// or failed — failures are logged at Error level, never silently swallowed.</summary>
        private static bool PerformProfileSwitch(string profileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profileName)) return false;
                if (string.Equals(ProfileManager.CurrentProfileName, profileName, StringComparison.OrdinalIgnoreCase)) return true;

                if (!ProfileManager.SwitchToProfile(profileName, silent: true))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                        $"VirtualDesktopAutomation: switch to profile '{profileName}' failed or was refused; will retry next tick.");
                    return false;
                }

                try { TrayManager.Instance?.UpdateTrayIcon(); } catch { }
                try { TrayManager.Instance?.UpdateProfilesMenu(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: switch to profile '{profileName}' threw: {ex.Message}");
                return false;
            }
        }

        private static IVirtualDesktopManager CreateManager()
        {
            try
            {
                // Documented shell CLSID VirtualDesktopManager — stable across Windows builds.
                var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
                if (type == null) return null;
                return (IVirtualDesktopManager)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: failed to create IVirtualDesktopManager: {ex.Message}");
                return null;
            }
        }

        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);
            [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
            [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
        }

        private static class NativeMethods
        {
            public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

            [DllImport("user32.dll")]
            public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lparam);

            [DllImport("user32.dll")]
            public static extern bool IsWindowVisible(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        }
    }
}
