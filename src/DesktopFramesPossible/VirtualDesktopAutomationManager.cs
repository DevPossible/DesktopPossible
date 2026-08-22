using System;
using System.Linq;
using WindowsDesktop;

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
    /// </summary>
    public static class VirtualDesktopAutomationManager
    {
        private const string DefaultProfileName = "Default";

        private static bool _started = false;
        private static bool _configured = false;

        public static void Start()
        {
            // SAFETY: everything wrapped so a COM/interop failure can never take the app down.
            try
            {
                if (_started) return;

                // Respect the Master Toggle
                if (!SettingsManager.EnableVirtualDesktopAutomation) return;

                // Virtual desktops are not available on all Windows builds.
                if (!VirtualDesktop.IsSupported)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "VirtualDesktopAutomation: virtual desktops not supported on this OS build. Automation disabled.");
                    return;
                }

                if (!EnsureConfigured()) return;

                VirtualDesktop.CurrentChanged += OnCurrentDesktopChanged;
                _started = true;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "VirtualDesktopAutomation: started (following virtual desktops by name).");

                // Apply immediately for the desktop we are on right now, so enabling the
                // setting (or app startup with it enabled) takes effect without requiring
                // a desktop switch first.
                Dispatch(() => ApplyForDesktop(VirtualDesktop.Current));
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
                if (_started)
                {
                    VirtualDesktop.CurrentChanged -= OnCurrentDesktopChanged;
                }
            }
            catch { }
            _started = false;

            if (switchToDefault)
            {
                Dispatch(() => PerformProfileSwitch(DefaultProfileName));
            }
        }

        /// <summary>
        /// Ensures the VirtualDesktop library is initialized. The library requires
        /// Configure() before first use (it compiles/caches the OS-build-specific
        /// COM interop assembly). Returns false when virtual desktops are unavailable.
        /// </summary>
        public static bool EnsureConfigured()
        {
            try
            {
                if (!VirtualDesktop.IsSupported) return false;
                if (!_configured)
                {
                    VirtualDesktop.Configure();
                    _configured = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: Configure failed: {ex.Message}");
                return false;
            }
        }

        private static void OnCurrentDesktopChanged(object sender, VirtualDesktopChangedEventArgs args)
        {
            try
            {
                // THREADING: CurrentChanged is raised by the library's COM event sink and may
                // arrive off the UI thread. ProfileManager.SwitchToProfile manipulates WPF
                // windows, so the whole evaluation-and-switch is marshaled onto the dispatcher.
                var desktop = args?.NewDesktop;
                if (desktop == null) return;
                Dispatch(() => ApplyForDesktop(desktop));
            }
            catch
            {
                // Swallow exceptions so the event subscription survives.
            }
        }

        /// <summary>Runs on the UI thread. Resolves the desktop's name and activates
        /// the matching profile (or Default when no profile matches).</summary>
        private static void ApplyForDesktop(VirtualDesktop desktop)
        {
            try
            {
                if (!SettingsManager.EnableVirtualDesktopAutomation)
                {
                    Stop();
                    return;
                }
                if (desktop == null) return;

                string desktopName = GetDesktopDisplayName(desktop);
                if (string.IsNullOrWhiteSpace(desktopName)) return;

                string target = ProfileManager.GetProfiles()
                    .FirstOrDefault(p => p.Name.Equals(desktopName, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? DefaultProfileName;

                PerformProfileSwitch(target);
            }
            catch
            {
                // Swallow exceptions so a bad profile/desktop can never crash the dispatcher.
            }
        }

        /// <summary>
        /// The desktop's user-given name, or "Desktop {position}" when unnamed
        /// (matching the naming Windows shows in the Win+Tab switcher).
        /// </summary>
        private static string GetDesktopDisplayName(VirtualDesktop desktop)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(desktop.Name)) return desktop.Name;

                var desktops = VirtualDesktop.GetDesktops();
                for (int i = 0; i < desktops.Length; i++)
                {
                    if (desktops[i].Id == desktop.Id) return $"Desktop {i + 1}";
                }
            }
            catch { }
            return null;
        }

        /// <summary>Runs on the UI thread. No-op when the target is already active.</summary>
        private static void PerformProfileSwitch(string profileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profileName)) return;
                if (string.Equals(ProfileManager.CurrentProfileName, profileName, StringComparison.OrdinalIgnoreCase)) return;

                ProfileManager.SwitchToProfile(profileName);

                try { TrayManager.Instance?.UpdateTrayIcon(); } catch { }
                try { TrayManager.Instance?.UpdateProfilesMenu(); } catch { }
            }
            catch { }
        }

        private static void Dispatch(Action action)
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(action);
            }
            catch { }
        }
    }
}
