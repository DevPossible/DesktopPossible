using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    /// <summary>
    /// Lazily creates and caches a single frozen ImageSource of the application icon for
    /// dialog window icons. Previously every dialog open extracted a fresh Icon and passed
    /// its HICON to CreateBitmapSourceFromHIcon without ever destroying the handle, leaking
    /// one GDI/USER handle per dialog. This extracts the icon once, destroys the native
    /// handle, and hands out the same frozen (thread-safe) ImageSource forever after.
    /// </summary>
    public static class DialogIconCache
    {
        private static readonly Lazy<ImageSource> _appIcon = new Lazy<ImageSource>(CreateAppIcon);

        /// <summary>The application icon as a frozen ImageSource, or null if extraction failed.</summary>
        public static ImageSource AppIcon => _appIcon.Value;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static ImageSource CreateAppIcon()
        {
            try
            {
                string exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return null;

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon == null) return null;

                IntPtr hIcon = icon.Handle;
                try
                {
                    var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    // CreateBitmapSourceFromHIcon copies the pixels; the HICON must be destroyed.
                    DestroyIcon(hIcon);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                    $"Failed to create cached dialog icon: {ex.Message}");
                return null;
            }
        }
    }
}
