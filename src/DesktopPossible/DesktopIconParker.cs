using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Keeps framed desktop items out of sight on the real desktop while the app runs (see
    /// <see cref="IconParking"/> for the rules). Framed items that live on the Desktop stay
    /// real files there — File Explorer still shows them normally — and only their desktop
    /// icon is parked off-screen; it goes back to its original spot when the item leaves its
    /// frame or the app exits.
    ///
    /// Safety nets, because a parked icon would otherwise stay invisible:
    ///  - the original positions are written to <c>parked-icons.json</c> BEFORE any icon moves;
    ///  - a guard process (this exe with <c>--icon-guard &lt;pid&gt;</c>), started with the first
    ///    parked icon, waits on the app and puts every icon back if it dies without a clean exit;
    ///  - the next launch replays the file, covering what the guard can't (power loss).
    /// Explorer pulls off-screen icons back into view on a refresh (F5), a display change or
    /// its own restart; a WinEvent hook on the desktop list view (plus a cheap poll for a new
    /// list view window) notices and re-parks them.
    /// All work runs on the UI thread (IFolderView needs STA).
    /// </summary>
    public static class DesktopIconParker
    {
        public const string GuardArgument = "--icon-guard";

        private static string StatePath => Path.Combine(AppPaths.DataRoot, "parked-icons.json");

        private static Dictionary<string, (int X, int Y)> _saved = new(StringComparer.OrdinalIgnoreCase);
        private static DispatcherTimer? _debounce;
        private static DispatcherTimer? _listViewPoll;
        private static IntPtr _listView, _hook;
        private static WinEventProc? _hookProc; // rooted: the hook calls it from native code
        private static bool _running;
        private static bool _autoArrangeWarned;
        private static bool _guardStarted;

        #region Lifecycle

        /// <summary>Starts parking. Call on the UI thread once frames are loaded.</summary>
        public static void Start()
        {
            if (_running) return;
            _running = true;
            _saved = LoadState(); // left over by a crash: Reconcile keeps or restores each entry

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Reconcile(); };

            _listViewPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _listViewPoll.Tick += (s, e) => { if (DesktopIconManager.GetDesktopListView() != _listView) { HookListView(); RequestReconcile(); } };
            _listViewPoll.Start();

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            HookListView();
            Reconcile();
        }

        /// <summary>Puts every parked icon back and stops. Call on the UI thread at exit / session end.</summary>
        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            _debounce?.Stop();
            _listViewPoll?.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Unhook();
            RestoreAllAndClear();
        }

        /// <summary>
        /// Guard-process entry point: waits for the app to exit, then restores whatever it left
        /// parked. A clean exit already restored everything and deleted the state file.
        /// </summary>
        public static void RunGuard(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid)) p.WaitForExit();
            }
            catch (ArgumentException) { } // already gone
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Icon guard: cannot wait on {pid}: {ex.Message}");
                return;
            }

            _saved = LoadState();
            if (_saved.Count == 0) return;
            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                $"Icon guard: app {pid} exited without restoring {_saved.Count} desktop icon(s); restoring them.");
            RestoreAllAndClear();
        }

        /// <summary>
        /// Starts the guard once, the first time an icon is about to be parked: users with no
        /// framed desktop items never pay for the extra process.
        /// </summary>
        private static void EnsureGuard()
        {
            if (_guardStarted) return;
            _guardStarted = true;
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                Process.Start(new ProcessStartInfo(exe, $"{GuardArgument} {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Icon guard could not start: {ex.Message}");
            }
        }

        #endregion

        #region Reconcile

        /// <summary>Frame items may have changed (save, load, profile switch): re-check soon.</summary>
        public static void RequestReconcile()
        {
            if (!_running) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_running || _debounce == null) return;
                _debounce.Stop();
                _debounce.Start();
            }));
        }

        private static void Reconcile()
        {
            if (!_running) return;
            try
            {
                var framed = CollectFramedPaths();

                // Nothing framed lives on the desktop and nothing is parked: leave Explorer alone
                // (frames that only hold store-backed items never pay for a COM round trip).
                if (_saved.Count == 0 && !framed.Any(p => IconParking.IsOnDesktop(p, DesktopFolders())))
                    return;

                var current = DesktopFolderView.GetPositions();
                if (current == null) return; // Explorer unavailable: the list-view poll retries

                var plan = IconParking.Compute(framed, current, _saved, p => File.Exists(p) || Directory.Exists(p));

                // Record original positions BEFORE moving anything: a crash right after the
                // park must still know where every icon belongs.
                if (plan.StateChanged)
                {
                    _saved = plan.State;
                    SaveState();
                }
                if (_saved.Count > 0) EnsureGuard();

                if (plan.Park.Count > 0)
                {
                    DesktopFolderView.SetPositions(plan.Park.Select(p => (p, IconParking.ParkX, IconParking.ParkY)).ToList());
                    WarnIfAutoArrange();
                }
                if (plan.Restore.Count > 0) DesktopFolderView.SetPositions(plan.Restore);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"DesktopIconParker.Reconcile: {ex.Message}");
            }
        }

        private static void RestoreAllAndClear()
        {
            try
            {
                if (_saved.Count > 0)
                {
                    var current = DesktopFolderView.GetPositions();
                    if (current == null) return; // keep the file: the next launch restores
                    DesktopFolderView.SetPositions(IconParking.RestoreAll(current, _saved));
                }
                _saved.Clear();
                try { File.Delete(StatePath); } catch { }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"DesktopIconParker restore: {ex.Message}");
            }
        }

        // Other profiles' framed paths, cached by frames.json write time (reconcile runs often).
        private static readonly Dictionary<string, (DateTime Stamp, HashSet<string> Paths)> _otherProfiles =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Full path of every item in every frame (all tabs) of EVERY profile. Desktop icons are
        /// shared by all profiles and virtual desktops (virtual-desktop automation switches
        /// profiles), so an item framed in an inactive profile must stay hidden too.
        /// </summary>
        private static HashSet<string> CollectFramedPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string activeDir = Path.GetFullPath(ProfileManager.CurrentProfileDir);

            // Active profile: live in-memory frames (may hold unsaved changes).
            var active = new ProfileFrameSet { Dir = activeDir, IsActive = true, Frames = FrameDataManager.FrameData ?? new List<dynamic>(), Save = () => { } };
            foreach (var (_, full, _) in AllProfileFrames.Items(active)) paths.Add(full);

            if (!Directory.Exists(AllProfileFrames.ProfilesRoot)) return paths;
            foreach (string dir in Directory.GetDirectories(AllProfileFrames.ProfilesRoot))
            {
                if (string.Equals(Path.GetFullPath(dir), activeDir, StringComparison.OrdinalIgnoreCase)) continue;
                string json = Path.Combine(dir, "frames.json");
                try
                {
                    if (!File.Exists(json)) continue;
                    DateTime stamp = File.GetLastWriteTimeUtc(json);
                    if (!_otherProfiles.TryGetValue(json, out var cached) || cached.Stamp != stamp)
                    {
                        var token = JToken.Parse(File.ReadAllText(json));
                        var set = new ProfileFrameSet
                        {
                            Dir = Path.GetFullPath(dir), IsActive = false, Save = () => { },
                            Frames = (token as JArray ?? new JArray(token)).OfType<JObject>().Cast<dynamic>().ToList(),
                        };
                        cached = (stamp, new HashSet<string>(AllProfileFrames.Items(set).Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase));
                        _otherProfiles[json] = cached;
                    }
                    paths.UnionWith(cached.Paths);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"DesktopIconParker: cannot read '{json}': {ex.Message}");
                }
            }
            return paths;
        }

        private static void WarnIfAutoArrange()
        {
            if (_autoArrangeWarned || DesktopFolderView.IsAutoArrange() != true) return;
            _autoArrangeWarned = true;
            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, "Desktop 'Auto arrange icons' is on: framed icons cannot be hidden.");
            SmartToast.Show("Desktop icons can't be hidden",
                "Framed items still show on the desktop because \"Auto arrange icons\" is on. Right-click the desktop → View and turn it off.");
        }

        #endregion

        #region Desktop placement

        /// <summary>The user and the all-users (Public) desktop folders.</summary>
        public static string[] DesktopFolders() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        public static bool IsDesktopItem(string? path) => IconParking.IsOnDesktop(path, DesktopFolders());

        /// <summary>Top-level window under a screen point (device pixels), or zero.</summary>
        public static IntPtr RootWindowAt(int screenX, int screenY)
        {
            IntPtr hwnd = WindowFromPoint(new POINT { x = screenX, y = screenY });
            return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT);
        }

        /// <summary>True when the screen point (device pixels) is over the bare desktop, not a window on it.</summary>
        public static bool IsOverDesktop(int screenX, int screenY)
        {
            IntPtr root = RootWindowAt(screenX, screenY);
            if (root == IntPtr.Zero) return false;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(root, cls, cls.Capacity);
            string name = cls.ToString();
            return name == "Progman" || name == "WorkerW";
        }

        /// <summary>
        /// Shows a desktop item's icon centred on a screen point (device pixels) and forgets its
        /// remembered position, so leaving its frame does not send it back there. Call before
        /// removing the item from its frame.
        /// </summary>
        public static bool PlaceAt(string path, int screenX, int screenY)
        {
            try
            {
                var pt = new POINT { x = screenX, y = screenY };
                IntPtr listView = DesktopIconManager.GetDesktopListView();
                if (listView == IntPtr.Zero || !ScreenToClient(listView, ref pt)) return false;

                // IFolderView positions are the icon's top-left: centre the icon cell on the cursor.
                const int halfCellX = 38, halfCellY = 32;
                bool moved = DesktopFolderView.SetPositions(new[] { (path, Math.Max(0, pt.x - halfCellX), Math.Max(0, pt.y - halfCellY)) });
                if (moved && _saved.Remove(path)) SaveState();
                return moved;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"DesktopIconParker.PlaceAt: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region State file

        private static Dictionary<string, (int X, int Y)> LoadState()
        {
            var state = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(StatePath)) return state;
                foreach (var entry in JArray.Parse(File.ReadAllText(StatePath)).OfType<JObject>())
                {
                    string? path = entry["Path"]?.ToString();
                    if (string.IsNullOrEmpty(path)) continue;
                    state[path] = ((int?)entry["X"] ?? 0, (int?)entry["Y"] ?? 0);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"parked-icons.json unreadable: {ex.Message}");
            }
            return state;
        }

        private static void SaveState()
        {
            try
            {
                if (_saved.Count == 0) { File.Delete(StatePath); return; }
                var array = new JArray(_saved.Select(kv => new JObject { ["Path"] = kv.Key, ["X"] = kv.Value.X, ["Y"] = kv.Value.Y }));
                AtomicFile.WriteAllText(StatePath, array.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Could not write parked-icons.json: {ex.Message}");
            }
        }

        #endregion

        #region Explorer change notifications

        private static void OnDisplaySettingsChanged(object? sender, EventArgs e) => RequestReconcile();

        // A desktop refresh fires REORDER/CREATE/DESTROY on the list view; our own
        // SelectAndPositionItems fires none, so re-parking never feeds back into itself.
        private const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001,
            EVENT_OBJECT_REORDER = 0x8004, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private static void HookListView()
        {
            Unhook();
            _listView = DesktopIconManager.GetDesktopListView();
            if (_listView == IntPtr.Zero) return;
            GetWindowThreadProcessId(_listView, out uint pid);
            _hookProc ??= OnWinEvent;
            _hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _hookProc, pid, 0, WINEVENT_OUTOFCONTEXT);
        }

        private static void Unhook()
        {
            if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        }

        private static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd != _listView) return;
            if (ev == EVENT_OBJECT_CREATE || ev == EVENT_OBJECT_DESTROY || ev == EVENT_OBJECT_REORDER || ev == EVENT_OBJECT_LOCATIONCHANGE)
                RequestReconcile();
        }

        #endregion

        #region Win32

        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

        #endregion
    }
}
