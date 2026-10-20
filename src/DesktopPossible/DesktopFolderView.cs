using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Desktop_Frames
{
    /// <summary>
    /// Reads and sets individual desktop icon positions through Explorer's own desktop view
    /// (IShellWindows.FindWindowSW(SWC_DESKTOP) → IShellBrowser → IFolderView): the documented,
    /// injection-free way to position desktop icons from another process.
    ///
    /// The view is re-acquired on every call, so an Explorer restart needs no special handling.
    /// Must be called on an STA thread (the WPF UI thread). Every method fails soft: null/false
    /// when the desktop view is unavailable (Explorer not running, mid-restart).
    /// IFolderView is IUnknown-only, so it cannot be late-bound through dynamic like the
    /// WScript.Shell calls; [ComImport] declarations need no interop assembly or COMReference
    /// (same approach as ShellContextMenu).
    /// </summary>
    internal static class DesktopFolderView
    {
        private const int SWC_DESKTOP = 8;
        private const int SWFO_NEEDDISPATCH = 1;
        private const uint SVGIO_ALLVIEW = 2;
        private const uint SVSI_POSITIONITEM = 0x80;

        /// <summary>Every file-system item in the desktop view, by full path, with its position.</summary>
        public static Dictionary<string, (int X, int Y)>? GetPositions()
        {
            var view = GetView();
            if (view == null) return null;
            var result = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var (path, pidl) in EnumerateItems(view))
                {
                    try
                    {
                        view.GetItemPosition(pidl, out var pt);
                        result[path] = (pt.x, pt.y);
                    }
                    finally { Marshal.FreeCoTaskMem(pidl); }
                }
                return result;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"DesktopFolderView.GetPositions: {ex.Message}");
                return null;
            }
            finally { Marshal.ReleaseComObject(view); }
        }

        /// <summary>Moves the given desktop items (by full path) in one batch. Unknown paths are skipped.</summary>
        public static bool SetPositions(IReadOnlyCollection<(string Path, int X, int Y)> moves)
        {
            if (moves.Count == 0) return true;
            var view = GetView();
            if (view == null) return false;

            var wanted = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in moves) wanted[m.Path] = (m.X, m.Y);

            var pidls = new List<IntPtr>();
            var points = new List<(int X, int Y)>();
            IntPtr pidlArray = IntPtr.Zero, pointArray = IntPtr.Zero;
            try
            {
                foreach (var (path, pidl) in EnumerateItems(view))
                {
                    if (wanted.TryGetValue(path, out var pt)) { pidls.Add(pidl); points.Add(pt); }
                    else Marshal.FreeCoTaskMem(pidl);
                }
                if (pidls.Count == 0) return true;

                pidlArray = Marshal.AllocHGlobal(IntPtr.Size * pidls.Count);
                pointArray = Marshal.AllocHGlobal(8 * pidls.Count);
                for (int i = 0; i < pidls.Count; i++)
                {
                    Marshal.WriteIntPtr(pidlArray, i * IntPtr.Size, pidls[i]);
                    Marshal.WriteInt32(pointArray, i * 8, points[i].X);
                    Marshal.WriteInt32(pointArray, i * 8 + 4, points[i].Y);
                }
                view.SelectAndPositionItems((uint)pidls.Count, pidlArray, pointArray, SVSI_POSITIONITEM);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"DesktopFolderView.SetPositions: {ex.Message}");
                return false;
            }
            finally
            {
                foreach (var p in pidls) Marshal.FreeCoTaskMem(p);
                if (pidlArray != IntPtr.Zero) Marshal.FreeHGlobal(pidlArray);
                if (pointArray != IntPtr.Zero) Marshal.FreeHGlobal(pointArray);
                Marshal.ReleaseComObject(view);
            }
        }

        /// <summary>
        /// True when "Auto arrange icons" is on: Explorer then ignores explicit positions, so
        /// icons cannot be parked. Null when the desktop view is unavailable.
        /// </summary>
        public static bool? IsAutoArrange()
        {
            var view = GetView();
            if (view == null) return null;
            try { return view.GetAutoArrange() == 0; } // S_OK = on, S_FALSE = off
            catch { return null; }
            finally { Marshal.ReleaseComObject(view); }
        }

        /// <summary>Yields (full path, child pidl) for each file-system item; the caller frees each pidl.</summary>
        private static IEnumerable<(string Path, IntPtr Pidl)> EnumerateItems(IFolderView view)
        {
            view.ItemCount(SVGIO_ALLVIEW, out int count);
            var sb = new StringBuilder(1024);
            for (int i = 0; i < count; i++)
            {
                IntPtr pidl;
                try { view.Item(i, out pidl); } catch { continue; }
                if (pidl == IntPtr.Zero) continue;

                // Desktop view items are children of the namespace root, so a child pidl is
                // also an absolute one. Non-file items (Recycle Bin, This PC) have no path.
                sb.Clear();
                if (SHGetPathFromIDListW(pidl, sb) && sb.Length > 0) yield return (sb.ToString(), pidl);
                else Marshal.FreeCoTaskMem(pidl);
            }
        }

        private static IFolderView? GetView()
        {
            object? windows = null;
            try
            {
                var type = Type.GetTypeFromCLSID(CLSID_ShellWindows);
                if (type == null) return null;
                windows = Activator.CreateInstance(type);
                if (windows is not IShellWindows shellWindows) return null;

                object loc = 0; object? root = null; // CSIDL_DESKTOP
                object? disp = shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
                if (disp is not IServiceProvider sp) return null;

                Guid sid = SID_STopLevelBrowser, iid = typeof(IShellBrowser).GUID;
                var browser = (IShellBrowser)sp.QueryService(ref sid, ref iid);
                return browser.QueryActiveShellView() as IFolderView;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"DesktopFolderView: desktop view unavailable: {ex.Message}");
                return null;
            }
            finally
            {
                if (windows != null && Marshal.IsComObject(windows)) Marshal.ReleaseComObject(windows);
            }
        }

        #region COM declarations

        private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder pszPath);

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IShellWindows
        {
            // Only FindWindowSW is called; the preceding members hold their dispatch slots.
            void _Count(); void _NewEnum(); void _Item(); void _Register(); void _RegisterPending();
            void _Revoke(); void _OnNavigate(); void _OnActivated();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW(ref object pvarloc, ref object? pvarlocRoot, int swClass, out int phwnd, int swfwOptions);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService(ref Guid guidService, ref Guid riid);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            // IOleWindow + IShellBrowser slots before QueryActiveShellView.
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB();
            void RemoveMenusSB(); void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB();
            void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryActiveShellView();
        }

        [ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            void GetCurrentViewMode(out uint pViewMode);
            void SetCurrentViewMode(uint viewMode);
            void GetFolder(ref Guid riid, out IntPtr ppv);
            void Item(int iItemIndex, out IntPtr ppidl);
            void ItemCount(uint uFlags, out int pcItems);
            void Items(uint uFlags, ref Guid riid, out IntPtr ppv);
            void GetSelectionMarkedItem(out int piItem);
            void GetFocusedItem(out int piItem);
            void GetItemPosition(IntPtr pidl, out POINT ppt);
            void GetSpacing(ref POINT ppt);
            void GetDefaultSpacing(out POINT ppt);
            [PreserveSig] int GetAutoArrange();
            void SelectItem(int iItem, uint dwFlags);
            void SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
        }

        #endregion
    }
}
