using System;
using System.IO;

namespace Desktop_Frames
{
    /// <summary>
    /// Where the app keeps its data (Profiles, MasterOptions.json, logs, backups, exports,
    /// category cache).
    ///
    /// Portable (zip) runs keep everything beside the executable, as always. An MSI install
    /// drops a <c>DesktopPossible.installed</c> marker next to the exe in Program Files —
    /// which standard users cannot write to — so installed runs keep their data under
    /// <c>%LocalAppData%\DesktopPossible\Data</c> instead (the <c>Data</c> leaf keeps it apart
    /// from the frame file store, which already lives at <c>%LocalAppData%\DesktopPossible\Profiles</c>).
    /// Nothing else in the app needs to know which mode it is in: every data path is built
    /// from <see cref="DataRoot"/>.
    /// </summary>
    public static class AppPaths
    {
        public const string InstalledMarkerFileName = "DesktopPossible.installed";

        /// <summary>Folder the executable runs from.</summary>
        public static string ExeDir => AppContext.BaseDirectory;

        /// <summary>True when running from an MSI install (marker file beside the exe).</summary>
        public static bool IsInstalled { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, InstalledMarkerFileName));

        /// <summary>Root of all user data; created on first access.</summary>
        public static string DataRoot { get; } = ResolveDataRoot();

        private static string ResolveDataRoot()
        {
            string root = IsInstalled
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPossible", "Data")
                : AppContext.BaseDirectory;
            try { Directory.CreateDirectory(root); } catch { }
            return root;
        }
    }
}
