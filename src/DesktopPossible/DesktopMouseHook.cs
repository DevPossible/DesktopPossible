using Desktop_Frames;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace DesktopFrames
{
    /// <summary>
    /// Installs a WH_MOUSE_LL hook and fires Framemanager.WakeUpFrames()
    /// when the user double-clicks the bare Windows desktop (Progman / WorkerW).
    /// Also remembers the last right-drag rectangle made on the desktop, so the context
    /// menu's "New Frame" can reuse the box the user already drew (see StartDrawMode).
    /// </summary>
    public static class DesktopMouseHook
    {
        // ── Win32 ────────────────────────────────────────────────────────────
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;

        // ListView Messages
        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        private const uint SMTO_ABORTIFHUNG = 0x0002;

        // ── State ────────────────────────────────────────────────────────────
        private static IntPtr _hookHandle = IntPtr.Zero;
        private static LowLevelMouseProc? _hookProc;

        private static uint _lastClickTime = 0;
        private static POINT _lastClickPoint = default;
        private const int CLICK_RADIUS = 4;

        // Right-drag capture (desktop rubber band → context menu → "New Frame").
        // A plain right-click CLEARS the stored box: the menu the user acts on came from
        // that release, and it carried no box — so a stale drag can never be misused.
        private static POINT _rightDownPoint;
        private static bool _rightDownOnDesktop;
        private static Rect _rightDragRect = Rect.Empty;
        private static DateTime _rightDragTimeUtc = DateTime.MinValue;
        private static readonly object _dragLock = new object();
        private const int DRAG_MIN_SIZE = 30; // device px per axis; anything smaller is a click

        // ── Public API ───────────────────────────────────────────────────────
        public static void Start()
        {
            if (_hookHandle != IntPtr.Zero) return;

            _hookProc = HookCallback;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule ?? throw new InvalidOperationException("Cannot obtain main module.");

            IntPtr hMod = GetModuleHandle(curModule.ModuleName);
            _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, hMod, 0);

            if (_hookHandle == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Failed to install low-level mouse hook.");
        }

        public static void Stop()
        {
            if (_hookHandle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookProc = null;
        }

        // ── Hook callback ────────────────────────────────────────────────────
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Guard the whole body: an exception must never prevent CallNextHookEx
            // (Windows silently drops misbehaving low-level hooks).
            try
            {
                if (nCode >= 0 && (int)wParam == WM_LBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    HandleClick(data.pt, data.time);
                }
                else if (nCode >= 0 && (int)wParam == WM_RBUTTONDOWN)
                {
                    // WindowFromPoint/GetClassName read window state without messaging
                    // Explorer, so they are safe inside the hook (unlike SendMessage).
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _rightDownPoint = data.pt;
                    _rightDownOnDesktop = IsDesktopWindow(WindowFromPoint(data.pt));
                }
                else if (nCode >= 0 && (int)wParam == WM_RBUTTONUP)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    HandleRightUp(data.pt);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"DesktopMouseHook: HookCallback error: {ex.Message}");
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private static void HandleClick(POINT pt, uint time)
        {
            uint dblClickMs = GetDoubleClickTime();
            uint elapsed = time - _lastClickTime;
            bool withinTime = elapsed <= dblClickMs;
            bool withinArea = WithinRadius(pt, _lastClickPoint, CLICK_RADIUS);

            if (withinTime && withinArea)
            {
                _lastClickTime = 0;
                _lastClickPoint = default;

                // Classification happens on the dispatcher side so the WH_MOUSE_LL callback
                // returns instantly and never messages Explorer from inside the hook (a busy
                // Explorer would otherwise stall ALL system mouse input and get our hook dropped).
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        IntPtr hWnd = WindowFromPoint(pt);
                        if (!IsDesktopWindow(hWnd) || HasSelectedDesktopIcon(hWnd)) return;

                        // Fences-style: double-clicking the bare desktop toggles the native icons.
                        if (SettingsManager.ToggleDesktopIconsOnDoubleClick)
                            Desktop_Frames.DesktopIconManager.ToggleDesktopIcons();

                        Framemanager.WakeUpFrames();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            $"DesktopMouseHook: double-click handling error: {ex.Message}");
                    }
                }));
            }
            else
            {
                _lastClickTime = time;
                _lastClickPoint = pt;
            }
        }

        private static void HandleRightUp(POINT up)
        {
            lock (_dragLock)
            {
                int w = Math.Abs(up.x - _rightDownPoint.x);
                int h = Math.Abs(up.y - _rightDownPoint.y);
                bool isDrag = _rightDownOnDesktop
                              && w >= DRAG_MIN_SIZE && h >= DRAG_MIN_SIZE
                              && IsDesktopWindow(WindowFromPoint(up));
                if (isDrag)
                {
                    _rightDragRect = new Rect(
                        Math.Min(up.x, _rightDownPoint.x),
                        Math.Min(up.y, _rightDownPoint.y), w, h);
                    _rightDragTimeUtc = DateTime.UtcNow;
                }
                else
                {
                    _rightDragRect = Rect.Empty;
                }
            }
        }

        /// <summary>
        /// One-shot: the last right-drag rectangle drawn on the bare desktop (device pixels),
        /// if it is younger than <paramref name="maxAge"/>. Consuming clears it.
        /// </summary>
        public static bool TryConsumeRightDragRect(TimeSpan maxAge, out Rect deviceRect)
        {
            lock (_dragLock)
            {
                deviceRect = _rightDragRect;
                _rightDragRect = Rect.Empty;
                if (deviceRect.IsEmpty || DateTime.UtcNow - _rightDragTimeUtc > maxAge)
                {
                    deviceRect = Rect.Empty;
                    return false;
                }
                return true;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static bool IsDesktopWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            var sb = new System.Text.StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            string cls = sb.ToString();

            return cls == "Progman" || cls == "WorkerW" || cls == "SysListView32" || cls == "SHELLDLL_DefView";
        }

        /// <summary>
        /// Sends a direct message to the desktop asking if any icons are selected.
        /// When double-clicking an icon, the first click selects it. 
        /// Therefore, at the exact moment of the double-click, this will be > 0.
        /// </summary>
        private static bool HasSelectedDesktopIcon(IntPtr hWnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);

            // We only query the list view itself
            if (sb.ToString() == "SysListView32")
            {
                // Bounded query: a hung Explorer must not block us (SMTO_ABORTIFHUNG, 50ms).
                // On timeout/failure assume "no selection" so a bare-desktop double-click still works.
                if (SendMessageTimeout(hWnd, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero,
                        SMTO_ABORTIFHUNG, 50, out IntPtr result) == IntPtr.Zero)
                    return false;

                return (int)result > 0;
            }

            return false;
        }

        private static bool WithinRadius(POINT a, POINT b, int radius)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            return (dx * dx + dy * dy) <= (radius * radius);
        }
    }
}