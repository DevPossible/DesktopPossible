using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Desktop_Frames
{
    /// <summary>
    /// Remembers where frames live on each display configuration and restores them when the
    /// configuration comes back — docking and undocking, plugging or unplugging a monitor,
    /// changing resolution or scaling, and connecting or disconnecting an RDP session.
    ///
    /// How it works:
    ///  1. Every display/session event marks the desktop UNSTABLE and restarts a debounce. A
    ///     dock burst fires several events over a few seconds; only the settled state matters.
    ///  2. Once quiet, the monitor set is fingerprinted (identity + bounds + scale + primary).
    ///  3. A known fingerprint restores its saved geometry. An unknown one is built by remapping
    ///     the LAST ACTIVE configuration's saved geometry onto the new monitors, then saved.
    ///
    /// The last-active layout is kept current continuously (see <see cref="MirrorActiveLayout"/>)
    /// rather than snapshotted when a change arrives, because by the time Windows tells us the
    /// display changed it has ALREADY shoved every window off the monitor that disappeared. The
    /// pre-event truth only exists if we were already writing it down. For the same reason,
    /// mirroring is frozen while unstable — otherwise Windows' emergency relocation gets saved
    /// as if the user had chosen it.
    /// </summary>
    public static class DisplayLayoutManager
    {
        /// <summary>Quiet period after the last display event before the configuration counts as settled.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(4);

        /// <summary>Second look before acting, so a configuration still mid-flight is not mistaken for settled.</summary>
        private static readonly TimeSpan ConfirmPeriod = TimeSpan.FromMilliseconds(750);

        /// <summary>Act regardless once this long has passed since the first event of a burst.</summary>
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(20);

        /// <summary>How many display configurations a profile remembers (LRU, active always kept).</summary>
        private const int MaxConfigs = 16;

        private const string StoreFileName = "layouts.json";

        private static LayoutStore _store = new LayoutStore();
        private static string _storePath = "";
        private static bool _initialized;
        private static bool _eventsHooked;
        private static DispatcherTimer? _settleTimer;
        private static DateTime _burstStartedUtc;
        private static string? _candidateFingerprint;

        /// <summary>
        /// True between the first display/session event and the moment the configuration settles.
        /// Frame geometry must not be persisted as the user's choice while this is set — Windows
        /// has already relocated windows off any monitor that went away.
        /// </summary>
        public static bool IsUnstable { get; private set; }

        /// <summary>Fingerprint of the configuration the frames are currently laid out for.</summary>
        public static string ActiveFingerprint => _store.ActiveFingerprint;

        #region Lifecycle

        /// <summary>
        /// Loads the current profile's sidecar and lays the frames out for the monitors that are
        /// attached right now. Called from LoadAndCreateFrames BEFORE the windows are created, so
        /// on startup and on every profile switch the frames are simply born in the right place.
        /// </summary>
        public static void ApplyForCurrentProfile()
        {
            try
            {
                _storePath = ProfileManager.GetProfileFilePath(StoreFileName);
                _store = LayoutStore.Load(_storePath);
                _initialized = true;

                var monitors = EnumerateMonitors();
                if (monitors.Count == 0)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        "DisplayLayout: no monitors reported; leaving frame geometry untouched.");
                    return;
                }

                Apply(monitors, DisplayConfig.Fingerprint(monitors), "profile load");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"DisplayLayout: profile load failed: {ex.Message}");
            }
        }

        /// <summary>Subscribes to display and session changes. Safe to call more than once.</summary>
        public static void Start()
        {
            if (_eventsHooked) return;
            _eventsHooked = true;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                "DisplayLayout: watching for display, session and power changes.");
        }

        /// <summary>Unsubscribes and flushes. SystemEvents holds static handlers, so this matters.</summary>
        public static void Stop()
        {
            if (_eventsHooked)
            {
                _eventsHooked = false;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }

            _settleTimer?.Stop();
            _settleTimer = null;

            // A pending burst must not freeze the last good layout out of the file forever.
            IsUnstable = false;
            MirrorActiveLayout();
        }

        #endregion

        #region Event handling and debounce

        private static void OnDisplaySettingsChanged(object? sender, EventArgs e) => NoteChange("display settings changed");

        private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                // Connecting or disconnecting a remote session swaps the display out from under
                // us; unlocking is when a docked-while-locked machine finally tells the truth.
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.RemoteDisconnect:
                case SessionSwitchReason.SessionUnlock:
                    NoteChange($"session {e.Reason}");
                    break;
            }
        }

        private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) NoteChange("resume from sleep");
        }

        /// <summary>
        /// Marks the desktop unstable and (re)arms the debounce. Runs on the SystemEvents
        /// thread, so everything real is marshalled onto the UI dispatcher.
        /// </summary>
        private static void NoteChange(string reason)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_initialized || !SettingsManager.EnableDisplayLayoutMemory) return;

                if (!IsUnstable)
                {
                    IsUnstable = true;
                    _burstStartedUtc = DateTime.UtcNow;
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"DisplayLayout: {reason}; waiting for the display configuration to settle.");
                }

                _candidateFingerprint = null;
                Rearm(QuietPeriod);
            }));
        }

        private static void Rearm(TimeSpan interval)
        {
            if (_settleTimer == null)
            {
                _settleTimer = new DispatcherTimer();
                _settleTimer.Tick += OnSettleTick;
            }
            _settleTimer.Stop();
            _settleTimer.Interval = interval;
            _settleTimer.Start();
        }

        private static void OnSettleTick(object? sender, EventArgs e)
        {
            try
            {
                var monitors = EnumerateMonitors();

                // Mid-transition Windows can briefly report nothing at all. Keep waiting rather
                // than treating "no desktop" as a configuration worth laying frames out for.
                if (monitors.Count == 0)
                {
                    Rearm(QuietPeriod);
                    return;
                }

                string fingerprint = DisplayConfig.Fingerprint(monitors);
                bool capped = DateTime.UtcNow - _burstStartedUtc >= MaxWait;

                // Read it twice, a moment apart, before acting on it.
                if (!capped && !string.Equals(fingerprint, _candidateFingerprint, StringComparison.Ordinal))
                {
                    _candidateFingerprint = fingerprint;
                    Rearm(ConfirmPeriod);
                    return;
                }

                _settleTimer?.Stop();
                _candidateFingerprint = null;
                IsUnstable = false;

                // Always re-apply, even when the fingerprint is unchanged: a monitor unplugged
                // and plugged back in within the quiet period leaves every frame Windows moved
                // sitting on the wrong screen, with nothing to show that anything happened.
                Apply(monitors, fingerprint, capped ? "display change (timed out)" : "display change");
            }
            catch (Exception ex)
            {
                _settleTimer?.Stop();
                IsUnstable = false;
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"DisplayLayout: settle failed: {ex.Message}");
            }
        }

        #endregion

        #region Applying a configuration

        /// <summary>
        /// Lays every frame out for <paramref name="monitors"/>: saved geometry when this
        /// configuration is already known, a remap of the last active configuration when it is
        /// not, and a per-frame remap for anything the known configuration has never seen
        /// (a frame created while docked, first seen back on the laptop screen).
        /// </summary>
        private static void Apply(List<MonitorInfo> monitors, string fingerprint, string reason)
        {
            if (!SettingsManager.EnableDisplayLayoutMemory) return;

            var frames = FrameDataManager.FrameData;
            if (frames == null) return;

            // Explicit loop, not LINQ: FrameData is List<dynamic>, so a Select would bind
            // dynamically and infer every tuple element as dynamic.
            var live = new List<(string Id, object Frame)>();
            foreach (object frame in frames)
            {
                string frameId = ReadId(frame);
                if (frameId.Length > 0) live.Add((frameId, frame));
            }

            var previous = _store.MostRecentOther(fingerprint);
            bool isNewConfig = _store.Find(fingerprint) == null;
            var config = _store.GetOrAdd(fingerprint, monitors);

            // Refresh the descriptor even for a known configuration: the work area moves when
            // the taskbar does, and the layout math reads the work area.
            config.Monitors = monitors;
            config.Label = DisplayConfig.Describe(monitors);
            config.LastSeenUtc = DateTime.UtcNow;

            var sourceMonitors = previous?.Monitors is { Count: > 0 } ? previous.Monitors : monitors;
            int moved = 0;

            foreach (var entry in live)
            {
                string id = entry.Id;
                FrameRect current = ReadRect(entry.Frame);

                if (!config.Frames.TryGetValue(id, out FrameRect? target) || target == null)
                {
                    FrameRect source = previous != null && previous.Frames.TryGetValue(id, out var saved) && saved != null
                        ? saved
                        : current;
                    target = DisplayConfig.Remap(source, sourceMonitors, monitors);
                    config.Frames[id] = target;
                }

                if (target.Matches(current)) continue;
                WriteRect(entry.Frame, id, target);
                moved++;
            }

            _store.ActiveFingerprint = fingerprint;
            _store.Prune(live.Select(x => x.Id), MaxConfigs);
            _store.Save(_storePath);

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                $"DisplayLayout: {reason} -> {config.Label} [{fingerprint}] " +
                $"({(isNewConfig ? "new layout built" : "saved layout restored")}, {moved} of {live.Count} frames moved).");

            if (moved > 0) FrameDataManager.SaveFrameData();
        }

        /// <summary>
        /// Copies the live frame geometry into the active configuration. Called at the end of
        /// every SaveFrameData so the configuration the user is sitting at is always written
        /// down BEFORE the next display change destroys the evidence.
        /// </summary>
        public static void MirrorActiveLayout()
        {
            if (!_initialized || IsUnstable || !SettingsManager.EnableDisplayLayoutMemory) return;

            try
            {
                // A profile switch tears the old frames down and loads the new ones before
                // ApplyForCurrentProfile re-points the store. A save landing in that window would
                // write the new profile's geometry into the old profile's file — so only mirror
                // while the loaded store still belongs to the current profile.
                if (!string.Equals(_storePath, ProfileManager.GetProfileFilePath(StoreFileName),
                        StringComparison.OrdinalIgnoreCase)) return;

                var config = _store.Find(_store.ActiveFingerprint);
                if (config == null) return;

                var frames = FrameDataManager.FrameData;
                if (frames == null) return;

                var ids = new List<string>();
                foreach (object frame in frames)
                {
                    string id = ReadId(frame);
                    if (id.Length == 0) continue;
                    ids.Add(id);
                    config.Frames[id] = ReadRect(frame);
                }

                config.LastSeenUtc = DateTime.UtcNow;
                _store.Prune(ids, MaxConfigs);
                _store.Save(_storePath);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Settings,
                    $"DisplayLayout: could not mirror the active layout: {ex.Message}");
            }
        }

        /// <summary>
        /// Options &gt; Tools: throws away the remembered layout for the display setup in use and
        /// rebuilds it from the most recent other setup. For when the saved layout for this
        /// configuration is the one that is wrong.
        /// </summary>
        public static void ForgetCurrentDisplayLayout()
        {
            if (!_initialized) return;

            var monitors = EnumerateMonitors();
            if (monitors.Count == 0) return;

            string fingerprint = DisplayConfig.Fingerprint(monitors);
            _store.Configs.RemoveAll(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.Ordinal));

            Apply(monitors, fingerprint, "layout forgotten by the user");
        }

        /// <summary>
        /// Discards every remembered layout for the current profile. Used after a restore, where
        /// the restored frames.json is the truth and the sidecar describes the world before it.
        /// </summary>
        public static void ForgetAllLayouts()
        {
            try
            {
                string path = ProfileManager.GetProfileFilePath(StoreFileName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

                _store = new LayoutStore();
                _storePath = path;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ImportExport,
                    "DisplayLayout: cleared remembered layouts; they will be re-seeded from the current frames.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ImportExport,
                    $"DisplayLayout: could not clear remembered layouts: {ex.Message}");
            }
        }

        /// <summary>Label of the display configuration in use, for the Options page.</summary>
        public static string DescribeCurrentDisplays() => DisplayConfig.Describe(EnumerateMonitors());

        #endregion

        #region Frame geometry access

        /// <summary>The frame's Id, or "" when the record has none. Never throws (see ReadNumber).</summary>
        private static string ReadId(object frame)
        {
            if (frame is Newtonsoft.Json.Linq.JObject jObject)
                return jObject.TryGetValue("Id", out var token) ? token?.ToString() ?? "" : "";

            if (frame is IDictionary<string, object> dictionary)
                return dictionary.TryGetValue("Id", out var value) ? value?.ToString() ?? "" : "";

            return "";
        }

        private static FrameRect ReadRect(object frame)
        {
            return new FrameRect
            {
                X = ReadNumber(frame, "X", 20),
                Y = ReadNumber(frame, "Y", 20),
                W = ReadNumber(frame, "Width", 230),
                H = ReadNumber(frame, "Height", 130),
                UnrolledHeight = ReadNumber(frame, "UnrolledHeight", 0)
            };
        }

        /// <summary>
        /// Reads one persisted number from a frame record. Goes through the dictionary interface
        /// rather than dynamic member access: frames are JObjects when loaded from disk and
        /// ExpandoObjects when freshly created, and a missing member on an Expando THROWS where
        /// a missing property on a JObject quietly yields null.
        /// </summary>
        private static double ReadNumber(object frame, string key, double fallback)
        {
            if (frame is Newtonsoft.Json.Linq.JObject jObject)
            {
                return jObject.TryGetValue(key, out var token) && token != null
                    ? Framemanager.ToDoubleInvariant(token.ToString(), fallback)
                    : fallback;
            }

            if (frame is IDictionary<string, object> dictionary)
            {
                return dictionary.TryGetValue(key, out var value) && value != null
                    ? Framemanager.ToDoubleInvariant(value, fallback)
                    : fallback;
            }

            return fallback;
        }

        /// <summary>Writes geometry into the frame record and moves the window if one is open.</summary>
        private static void WriteRect(dynamic frame, string frameId, FrameRect rect)
        {
            frame.X = rect.X;
            frame.Y = rect.Y;
            frame.Width = rect.W;
            frame.Height = rect.H;
            if (rect.UnrolledHeight > 0) frame.UnrolledHeight = rect.UnrolledHeight;

            var window = Application.Current?.Windows.OfType<NonActivatingWindow>()
                .FirstOrDefault(w => w.Tag?.ToString() == frameId);
            if (window == null) return;

            window.Left = rect.X;
            window.Top = rect.Y;
            window.Width = rect.W;
            window.Height = rect.H;
        }

        #endregion

        #region Monitor enumeration

        /// <summary>
        /// The monitors attached right now: bounds and work area in physical pixels from
        /// Screen.AllScreens, effective scale from GetDpiForMonitor, identity from
        /// EnumDisplayDevices. Every lookup degrades to a safe default rather than throwing.
        /// </summary>
        public static List<MonitorInfo> EnumerateMonitors()
        {
            var result = new List<MonitorInfo>();
            try
            {
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var bounds = screen.Bounds;
                    var work = screen.WorkingArea;
                    result.Add(new MonitorInfo
                    {
                        Id = HardwareId(screen.DeviceName),
                        Bounds = new PxRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                        WorkArea = new PxRect(work.X, work.Y, work.Width, work.Height),
                        Scale = ScaleOf(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
                        Primary = screen.Primary
                    });
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                    $"DisplayLayout: could not enumerate monitors: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// EDID vendor and product for a display device ("MONITOR\DEL4099"), trimmed of the
        /// per-instance suffix Windows appends — that suffix churns when a monitor comes back on
        /// a different port, which is precisely the case this feature has to survive. Returns
        /// "" when the identity cannot be read; the fingerprint stays stable either way.
        /// </summary>
        private static string HardwareId(string deviceName)
        {
            try
            {
                var device = new DISPLAY_DEVICE();
                device.cb = Marshal.SizeOf(device);
                if (!EnumDisplayDevices(deviceName, 0, ref device, 0)) return "";

                string id = device.DeviceID ?? "";
                if (id.Length == 0) return "";

                string[] parts = id.Split('\\');
                return parts.Length >= 2 ? $"{parts[0]}\\{parts[1]}" : id;
            }
            catch { return ""; }
        }

        /// <summary>Effective scale factor of the monitor containing a point, 1.0 when unavailable.</summary>
        private static double ScaleOf(int x, int y)
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                {
                    return Math.Round(dpiX / 96.0, 3);
                }
            }
            catch { /* Shcore unavailable or the call failed: fall through */ }
            return 1.0;
        }

        private const int MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        #endregion
    }
}
