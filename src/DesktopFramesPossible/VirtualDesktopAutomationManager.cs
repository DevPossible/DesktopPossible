using System;
using System.Linq;
using WindowsDesktop;

namespace Desktop_Frames
{
    /// <summary>
    /// Event-driven profile automation triggered by Windows virtual desktop switches
    /// (Win+Ctrl+Left/Right). Mirrors AutomationManager's conventions (Start/Stop,
    /// manual-override + revert logic, defensive try/catch), but subscribes to
    /// VirtualDesktop.CurrentChanged instead of polling a timer.
    ///
    /// PRECEDENCE NOTE: process-automation (AutomationManager) and virtual-desktop
    /// automation are independent engines. If both fire, last-writer-wins; each
    /// tracks its own "automated" flag. This is a known limitation, not a bug.
    /// </summary>
    public static class VirtualDesktopAutomationManager
    {
        private static bool _started = false;
        private static bool _configured = false;
        private static bool _isCurrentlyAutomated = false;

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
                    "VirtualDesktopAutomation: started (listening for desktop switches).");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: failed to start: {ex.Message}");
            }
        }

        public static void Stop()
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
            _isCurrentlyAutomated = false;
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

        /// <summary>
        /// Safe desktop enumeration for the rules UI: (Guid string, display name) pairs.
        /// Unnamed desktops get "Desktop {index+1}". Returns an empty list on
        /// unsupported builds or COM failure so the form never crashes.
        /// </summary>
        public static System.Collections.Generic.List<(string Id, string Name)> GetDesktopsSafe()
        {
            var list = new System.Collections.Generic.List<(string Id, string Name)>();
            try
            {
                if (!EnsureConfigured()) return list;

                var desktops = VirtualDesktop.GetDesktops();
                for (int i = 0; i < desktops.Length; i++)
                {
                    string name = desktops[i].Name;
                    if (string.IsNullOrWhiteSpace(name)) name = $"Desktop {i + 1}";
                    list.Add((desktops[i].Id.ToString(), name));
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"VirtualDesktopAutomation: failed to enumerate desktops: {ex.Message}");
            }
            return list;
        }

        private static void OnCurrentDesktopChanged(object sender, VirtualDesktopChangedEventArgs args)
        {
            try
            {
                // THREADING: CurrentChanged is raised by the library's COM event sink and may
                // arrive off the UI thread. ProfileManager.SwitchToProfile manipulates WPF
                // windows and is normally driven from the UI thread (AutomationManager runs on
                // a DispatcherTimer). Marshal the WHOLE rule-evaluation-and-switch onto the
                // dispatcher — not just the tray update — so profile state never mutates
                // concurrently with the UI.
                string newDesktopId = args?.NewDesktop?.Id.ToString();
                if (string.IsNullOrEmpty(newDesktopId)) return;

                var app = System.Windows.Application.Current;
                if (app == null) return;

                app.Dispatcher.BeginInvoke(new Action(() => EvaluateDesktop(newDesktopId)));
            }
            catch
            {
                // Swallow exceptions so the event subscription survives.
            }
        }

        private static void EvaluateDesktop(string newDesktopId)
        {
            try
            {
                // Respect the Master Toggle (event-driven, so stop explicitly when disabled)
                if (!SettingsManager.EnableVirtualDesktopAutomation)
                {
                    Stop();
                    return;
                }

                var rule = ProfileManager.AutomationRules.FirstOrDefault(r =>
                    r.TriggerType == AutomationTriggerType.VirtualDesktop &&
                    string.Equals(r.VirtualDesktopId, newDesktopId, StringComparison.OrdinalIgnoreCase));

                if (rule != null)
                {
                    // NOTE: DelaySeconds is intentionally ignored for VD rules — the desktop
                    // switch is a discrete event, so a settle delay adds nothing.
                    if (ProfileManager.CurrentProfileName != rule.TargetProfile)
                    {
                        // "Manual Override" Check (same as AutomationManager):
                        // Only switch if we are currently on the "Home" profile OR the "Automated" profile.
                        // If user manually switched to a 3rd profile, don't interrupt them.
                        bool isSafeToSwitch = (ProfileManager.CurrentProfileName == ProfileManager.ManualBaseProfile) || _isCurrentlyAutomated;

                        if (isSafeToSwitch)
                        {
                            if (rule.IsPersisted)
                            {
                                // Persistent rules change the "Home" so we don't revert
                                ProfileManager.SetManualBaseProfile(rule.TargetProfile);
                                _isCurrentlyAutomated = false;
                            }
                            else
                            {
                                _isCurrentlyAutomated = true;
                            }

                            PerformProfileSwitch(rule.TargetProfile);
                        }
                    }
                }
                else if (_isCurrentlyAutomated)
                {
                    // REVERT LOGIC: switched to an unmapped desktop while in automated mode
                    _isCurrentlyAutomated = false;
                    PerformProfileSwitch(ProfileManager.ManualBaseProfile);
                }
            }
            catch
            {
                // Swallow exceptions so a bad rule/profile can never crash the dispatcher.
            }
        }

        /// <summary>Runs on the UI thread (dispatched by OnCurrentDesktopChanged).</summary>
        private static void PerformProfileSwitch(string profileName)
        {
            try
            {
                ProfileManager.SwitchToProfile(profileName);

                try
                {
                    TrayManager.Instance?.UpdateTrayIcon();
                }
                catch { }

                try
                {
                    TrayManager.Instance?.UpdateProfilesMenu();
                }
                catch { }
            }
            catch { }
        }
    }
}
