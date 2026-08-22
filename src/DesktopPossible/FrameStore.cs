using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Desktop_Frames
{
    /// <summary>
    /// Per-frame file store: every Data frame owns a folder under
    /// %LocalAppData%\DesktopFramesPossible\Profiles\&lt;profile&gt;\Frames\&lt;frameId&gt;\
    /// holding the files its items are backed by (moved-in shortcuts and real files,
    /// plus the .lnk wrappers created for exes/folders). Items store the ABSOLUTE path
    /// of their backing file. Frame Ids (not titles) key the folders — titles change.
    ///
    /// Legacy items (relative "Shortcuts\x.lnk" paths or links pointing elsewhere)
    /// are untouched: nothing here ever deletes a file that is not inside a frame folder.
    /// The path/move helpers are pure and headless-testable.
    /// </summary>
    public static class FrameStore
    {
        /// <summary>Root of all per-profile frame stores.</summary>
        public static string RootDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopFramesPossible", "Profiles");

        /// <summary>
        /// Makes a profile name safe as a folder name: invalid path chars become '_',
        /// surrounding whitespace/dots are trimmed, empty falls back to "Default".
        /// </summary>
        public static string SanitizeProfileName(string? profileName)
        {
            string name = (profileName ?? "").Trim();
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            name = new string(chars).Trim(' ', '.');
            return name.Length == 0 ? "Default" : name;
        }

        /// <summary>Pure path builder: root\sanitizedProfile\Frames\frameId (no IO).</summary>
        public static string BuildFrameFolderPath(string root, string? profileName, string frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId)) throw new ArgumentException("Frame Id is required.", nameof(frameId));
            var invalid = Path.GetInvalidFileNameChars();
            string safeId = new string(frameId.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return Path.Combine(root, SanitizeProfileName(profileName), "Frames", safeId);
        }

        /// <summary>The frame's folder in the current profile's store, created on demand.</summary>
        public static string GetFrameFolder(dynamic frame)
        {
            string frameId = frame?.Id?.ToString() ?? "";
            string folder = BuildFrameFolderPath(RootDir, ProfileManager.CurrentProfileName, frameId);
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>True when the path lies inside ANY frame folder of the store (any profile).</summary>
        public static bool IsInsideAnyFrameFolder(string? path) => IsInsideAnyFrameFolder(RootDir, path);

        /// <summary>Pure variant of <see cref="IsInsideAnyFrameFolder(string?)"/> for a given root.</summary>
        public static bool IsInsideAnyFrameFolder(string root, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            try
            {
                string full = Path.GetFullPath(path);
                string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;

                // root\<profile>\Frames\<frameId>\<file>
                string[] rest = full.Substring(rootFull.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return rest.Length >= 4 && string.Equals(rest[1], "Frames", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// First free path for fileName inside folder: "name.ext", then "name (1).ext",
        /// "name (2).ext", ... Pure apart from existence checks.
        /// </summary>
        public static string UniqueDestinationPath(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int n = 1;
            do
            {
                candidate = Path.Combine(folder, $"{stem} ({n++}){ext}");
            } while (File.Exists(candidate) || Directory.Exists(candidate));
            return candidate;
        }

        /// <summary>
        /// Moves (or, with copy = true, copies) sourcePath into the frame's folder under a
        /// collision-safe name and returns the new absolute path. The source is never
        /// deleted when the operation fails.
        /// </summary>
        public static string MoveIntoFrame(dynamic frame, string sourcePath, bool copy)
        {
            return MoveIntoFolder(GetFrameFolder(frame), sourcePath, copy);
        }

        /// <summary>
        /// Moves (or copies) sourcePath into destFolder with " (n)" collision suffixing.
        /// Cross-volume moves (File.Move IOException) fall back to Copy + Delete; the
        /// source survives any failure. Returns the new absolute path.
        /// </summary>
        public static string MoveIntoFolder(string destFolder, string sourcePath, bool copy)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source file not found.", sourcePath);
            Directory.CreateDirectory(destFolder);

            string sourceFull = Path.GetFullPath(sourcePath);
            string dest = UniqueDestinationPath(destFolder, Path.GetFileName(sourceFull));

            if (copy)
            {
                File.Copy(sourceFull, dest);
                return dest;
            }

            try
            {
                File.Move(sourceFull, dest);
            }
            catch (IOException)
            {
                // Cross-volume or similar: copy first, delete the original only once the
                // copy is complete.
                File.Copy(sourceFull, dest);
                try { File.Delete(sourceFull); }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"FrameStore: copied '{sourceFull}' but could not remove the original: {ex.Message}");
                }
            }
            return dest;
        }

        /// <summary>
        /// Disposes of an item's backing file when the item is removed from a frame —
        /// ONLY when it lives inside a frame folder: shortcuts (.lnk/.url) are deleted,
        /// real files go to the Recycle Bin. Files outside the store (legacy links,
        /// desktop files) are never touched.
        /// </summary>
        public static void RemoveBackingFile(string? filename)
        {
            try
            {
                if (!IsInsideAnyFrameFolder(filename)) return;
                string path = Path.GetFullPath(filename!);
                if (!File.Exists(path)) return;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".lnk" || ext == ".url")
                {
                    File.Delete(path);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling,
                        $"FrameStore: deleted shortcut '{path}'");
                }
                else if (RecycleBin.TryMoveToRecycleBin(path, out string? error))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling,
                        $"FrameStore: recycled '{path}'");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"FrameStore: could not recycle '{path}': {error}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                    $"FrameStore: could not remove backing file '{filename}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Shell Recycle Bin deletion (SHFileOperation + FOF_ALLOWUNDO), shared by the Portal
    /// frame delete command and the frame store.
    /// </summary>
    public static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation([In] ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;

        /// <summary>Sends a file or folder to the Recycle Bin without a confirmation prompt.</summary>
        public static bool TryMoveToRecycleBin(string path, out string? error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    error = "Item not found";
                    return false;
                }

                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION,
                    pFrom = path + '\0' + '\0' // double null-terminated list
                };

                int result = SHFileOperation(ref shf);
                if (result != 0 || shf.fAnyOperationsAborted)
                {
                    error = $"SHFileOperation returned {result}";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
