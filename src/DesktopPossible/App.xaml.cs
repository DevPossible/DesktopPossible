using DesktopFrames;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Desktop_Frames
{
    public partial class App : Application
    {
        private TrayManager _trayManager;
        private TargetChecker _targetChecker;

        // Registry trigger channel reader: a second launch writes a trigger (wake or CMD_DRAW)
        // to the registry and exits; this instance polls for it once per second.
        private System.Windows.Threading.DispatcherTimer _triggerPollTimer;
        private string _lastTriggerValue;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Crash logging: record any unhandled exception (with stack) so crashes aren't silent.
            this.DispatcherUnhandledException += (s2, ex2) =>
                LogManager.Diag($"DISPATCHER UNHANDLED: {ex2.Exception.GetType().Name}: {ex2.Exception.Message}\n{ex2.Exception.StackTrace}");
            AppDomain.CurrentDomain.UnhandledException += (s2, ex2) =>
            {
                var e3 = ex2.ExceptionObject as Exception;
                LogManager.Diag($"DOMAIN UNHANDLED: {e3?.GetType().Name}: {e3?.Message}\n{e3?.StackTrace}");
            };

            // --- 1. INITIALIZE PROFILES & SETTINGS FIRST ---
            // This ensures we know the user's true DisableSingleInstance preference immediately
            try
            {
                ProfileManager.Initialize();
                System.IO.Directory.SetCurrentDirectory(ProfileManager.CurrentProfileDir);
                SettingsManager.LoadSettings();
                FrameStore.MigrateLegacyRoot(); // rebrand: %LocalAppData%\DesktopFramesPossible -> DesktopPossible
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Profile Initialization Error: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // --- 2. SINGLE INSTANCE PROTECTION START ---
            // Named mutex acquired here and held for the process lifetime; exactly one of two
            // simultaneous launches wins. The loser hands off via the registry trigger and exits.
            bool isNewInstance = SingleInstanceChecker.TryAcquireSingleInstance();

            // Only exit if it's not a new instance AND the user hasn't explicitly disabled the check
            if (!isNewInstance && !SettingsManager.DisableSingleInstance)
            {
                bool isDrawCommand = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                     || Environment.GetCommandLineArgs().Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

                SingleInstanceChecker.HandleDuplicateInstance(isDrawCommand ? $"CMD_DRAW|{Guid.NewGuid()}" : null);

                Shutdown();
                return;
            }
            // --- SINGLE INSTANCE PROTECTION END ---

            try
            {
                // --- NEW: Sanitize Registry on Startup ---
                RegistryHelper.DeleteTrigger();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Startup: Working Directory set to {ProfileManager.CurrentProfileDir}");

                // 3. Continue Normal Startup
                // (Settings were already loaded above, right after the profile directory was set.)
                {
                    // --- NEW: Self-Heal Context Menu Path ---
                    // Ensures the registry key points to the current EXE location
                    RegistryHelper.RefreshContextMenuPath();

                    // Initialize InterCore system
                    // DISABLED (perf): InterCore ran a 1-second registry poll + easter eggs (Dance
                    // Party / Gravity Drop). Not needed. InterCore.ProcessTitleChange still works
                    // without Initialize(), so title-based effects are unaffected.
                    // InterCore.Initialize();

                    // --- CHAMELEON ENGINE ---
                    WallpaperColorManager.Initialize();
                    // --- AUTO-ORGANIZE ENGINE ---
                    AutoOrganizeManager.Initialize();
                    WallpaperColorManager.WallpaperColorChanged += (s, ev) =>
                    {
                        // Magically update all visuals live when Windows wallpaper changes
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Utility.UpdateFrameVisuals();
                        }));
                    };

                    // Initialize TrayManager BEFORE frames
                    _trayManager = new TrayManager();
                    _trayManager.InitializeTray();

         

                    // Initialize TargetChecker
                    _targetChecker = new TargetChecker(1000);
                    _targetChecker.Start();

                    // Start the Background Icon Loader Engine
                    LazyIconLoader.Start();

                    // Load frames (Now loads from Profile/fences.json)
                    Framemanager.LoadAndCreateFrames(_targetChecker);

                    // --- PRODUCTION START LOGIC ---
                    if (SettingsManager.EnableVirtualDesktopAutomation)
                    {
                        VirtualDesktopAutomationManager.Start();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Startup: Virtual Desktop Automation Engine ignited.");
                    }

                    // Ensure UI reflects the current state of profiles and automation
                    _trayManager.UpdateProfilesMenu();
                    _trayManager.UpdateTrayIcon();
                    _trayManager.UpdateHiddenFramesMenu();

                    // Initialize global hotkey monitoring
                    try
                    {
                        GlobalHotkeyManager.StartMonitoring();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                            "GlobalHotkeyManager: Successfully initialized hotkey monitoring");
                    }
                    catch (System.Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            $"GlobalHotkeyManager: Failed to initialize: {ex.Message}");
                    }



                    // --- NEW: Start Desktop Double-Click Listener ---
                    try
                    {
                        DesktopMouseHook.Start();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "DesktopMouseHook started.");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to start DesktopMouseHook: {ex.Message}");
                    }



                    // --- NEW: Direct Draw Mode Check ---
                    // If this MAIN instance was started via Context Menu, trigger draw mode now.
                    // Use the same robust check as above.
                    var allArgs = Environment.GetCommandLineArgs();
                    bool isDrawStartup = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                         || allArgs.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isDrawStartup)
                    {
                        // Wait 500ms for UI to settle, then draw
                        Task.Delay(500).ContinueWith(t => Dispatcher.Invoke(() => Framemanager.StartDrawMode()));
                    }

                    // Keep the idle memory footprint low: trim the working set after startup
                    // and periodically thereafter.
                    MemoryOptimizer.Start();

                    // --- Registry trigger channel reader ---
                    // A second launch (single-instance loser, or the desktop context menu's
                    // "-create") writes a trigger to the registry and exits. Poll it once per
                    // second: CMD_DRAW starts draw mode, a plain timestamp wakes hidden frames.
                    _triggerPollTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1)
                    };
                    _triggerPollTimer.Tick += OnTriggerPoll;
                    _triggerPollTimer.Start();

                    // Warm the context-menu subsystems (WPF popup + native shell handlers) once the
                    // UI is idle, so the FIRST right-click of the session isn't jittery.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { PreWarmManager.Run(); } catch { }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical Startup Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// Polls the registry trigger channel written by secondary launches.
        /// Handles CMD_DRAW (draw a new frame) and the plain wake trigger; the easter-egg
        /// effects of the old InterCore monitor stay retired.
        /// </summary>
        private void OnTriggerPoll(object sender, EventArgs e)
        {
            try
            {
                string currentValue = RegistryHelper.CheckForTrigger();

                // Empty registry -> reset memory so the next trigger is accepted.
                if (string.IsNullOrEmpty(currentValue))
                {
                    _lastTriggerValue = null;
                    return;
                }

                // Debounce: identical to the last processed value -> ignore.
                if (currentValue == _lastTriggerValue) return;
                _lastTriggerValue = currentValue;

                if (currentValue.StartsWith("CMD_DRAW", StringComparison.Ordinal))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        $"Trigger received: Draw Mode ({currentValue})");
                    Framemanager.StartDrawMode();
                }
                else
                {
                    // Timestamp (plain second-launch) -> wake hidden frames so the user sees a response.
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        $"Trigger received: Wake Up ({currentValue})");
                    if (Framemanager._areFramesAutoHidden)
                        Framemanager.WakeUpFrames();
                }

                RegistryHelper.DeleteTrigger();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Trigger poll error: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Frame position/size saves are debounced (500ms) — flush any pending save
            // so the last move/resize before exit is never lost.
            try { Framemanager.FlushPendingFrameSave(); } catch { }

            _triggerPollTimer?.Stop();
            InterCore.Cleanup();
            try
            {
                GlobalHotkeyManager.StopMonitoring();
            }
            catch { }

            // --- NEW: Stop Desktop Double-Click Listener ---
            try
            {
                DesktopMouseHook.Stop();
            }
            catch { }

            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}