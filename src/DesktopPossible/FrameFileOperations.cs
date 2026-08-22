using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// File-safety administration for folder-backed frames: everything that moves or
    /// deletes the files physically stored inside a frame folder
    /// (<see cref="FrameStore"/>: root\profile\Frames\frameId). Used by frame deletion,
    /// profile deletion and "Empty Frames to Desktop".
    ///
    /// Only files INSIDE frame folders are ever touched. Legacy items whose Filename
    /// points elsewhere are never moved or deleted — only their frame entries are
    /// affected, and only when they lie inside the frame folder.
    ///
    /// Every operation has a root/desktop-parameterised overload so the logic is
    /// headless-testable against a temp directory; the parameterless variants bind to
    /// the real store (<see cref="FrameStore.RootDir"/>) and the user's Desktop.
    /// </summary>
    public static class FrameFileOperations
    {
        /// <summary>The user's physical Desktop folder.</summary>
        public static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        #region Frame-level operations

        /// <summary>All files (recursively) stored in the frame's folder of the given profile.</summary>
        public static IReadOnlyList<string> EnumerateFrameFiles(string profileName, string frameId)
            => EnumerateFrameFiles(FrameStore.RootDir, profileName, frameId);

        public static IReadOnlyList<string> EnumerateFrameFiles(string root, string profileName, string frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId)) return Array.Empty<string>();
            string folder = FrameStore.BuildFrameFolderPath(root, profileName, frameId);
            return EnumerateFilesIn(folder);
        }

        /// <summary>
        /// Moves every file in the frame's folder to the Desktop with " (n)" collision
        /// suffixing (Copy+Delete fallback across volumes). A file that fails to move is
        /// left in place and logged; nothing is ever deleted on failure. The frame folder
        /// is removed afterwards only if it is completely empty. Returns the moved count.
        /// </summary>
        public static int MoveFrameFilesToDesktop(string profileName, string frameId)
            => MoveFrameFilesToDesktop(FrameStore.RootDir, DesktopDir, profileName, frameId);

        public static int MoveFrameFilesToDesktop(string root, string desktopDir, string profileName, string frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId)) return 0;
            string folder = FrameStore.BuildFrameFolderPath(root, profileName, frameId);
            int moved = 0;

            foreach (string file in EnumerateFilesIn(folder))
            {
                try
                {
                    string dest = FrameStore.MoveIntoFolder(desktopDir, file, copy: false);
                    moved++;
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling,
                        $"FrameFileOperations: moved '{file}' -> '{dest}'");
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"FrameFileOperations: could not move '{file}' to the desktop: {ex.Message}");
                }
            }

            RemoveFolderIfEmpty(folder);
            return moved;
        }

        /// <summary>
        /// Permanently deletes every file in the frame's folder (the caller's typed
        /// confirmation is the safety net) and then removes the folder itself.
        /// Returns the number of files deleted.
        /// </summary>
        public static int DeleteFrameFiles(string profileName, string frameId)
            => DeleteFrameFiles(FrameStore.RootDir, profileName, frameId);

        public static int DeleteFrameFiles(string root, string profileName, string frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId)) return 0;
            string folder = FrameStore.BuildFrameFolderPath(root, profileName, frameId);
            int deleted = 0;

            foreach (string file in EnumerateFilesIn(folder))
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling,
                        $"FrameFileOperations: deleted '{file}'");
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                        $"FrameFileOperations: could not delete '{file}': {ex.Message}");
                }
            }

            RemoveFolderIfEmpty(folder);
            return deleted;
        }

        /// <summary>Removes the frame's folder when it holds no files (frame deletion cleanup).</summary>
        public static void RemoveFrameFolder(string profileName, string frameId)
            => RemoveFrameFolder(FrameStore.RootDir, profileName, frameId);

        public static void RemoveFrameFolder(string root, string profileName, string frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId)) return;
            RemoveFolderIfEmpty(FrameStore.BuildFrameFolderPath(root, profileName, frameId));
        }

        #endregion

        #region Profile-wide operations

        /// <summary>root\profile\Frames — parent of every frame folder of the profile.</summary>
        public static string ProfileFramesDir(string root, string? profileName)
            => Path.Combine(root, FrameStore.SanitizeProfileName(profileName), "Frames");

        /// <summary>
        /// Frame Ids that have a folder under the profile's Frames dir. Folders exist
        /// independently of frames.json, so the directory itself is the source of truth.
        /// </summary>
        public static IReadOnlyList<string> EnumerateProfileFrameIds(string profileName)
            => EnumerateProfileFrameIds(FrameStore.RootDir, profileName);

        public static IReadOnlyList<string> EnumerateProfileFrameIds(string root, string profileName)
        {
            string framesDir = ProfileFramesDir(root, profileName);
            if (!Directory.Exists(framesDir)) return Array.Empty<string>();
            try
            {
                return Directory.GetDirectories(framesDir)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Total number of files across all frame folders of the profile.</summary>
        public static int CountProfileFiles(string profileName) => CountProfileFiles(FrameStore.RootDir, profileName);

        public static int CountProfileFiles(string root, string profileName)
            => EnumerateProfileFrameIds(root, profileName).Sum(id => EnumerateFrameFiles(root, profileName, id).Count);

        /// <summary>Moves every file of every frame folder of the profile to the Desktop, then removes the profile's Frames dir.</summary>
        public static int MoveProfileFilesToDesktop(string profileName)
            => MoveProfileFilesToDesktop(FrameStore.RootDir, DesktopDir, profileName);

        public static int MoveProfileFilesToDesktop(string root, string desktopDir, string profileName)
        {
            int moved = EnumerateProfileFrameIds(root, profileName)
                .Sum(id => MoveFrameFilesToDesktop(root, desktopDir, profileName, id));
            RemoveFolderIfEmpty(ProfileFramesDir(root, profileName));
            return moved;
        }

        /// <summary>Permanently deletes every file of every frame folder of the profile, then removes the profile's Frames dir.</summary>
        public static int DeleteProfileFiles(string profileName) => DeleteProfileFiles(FrameStore.RootDir, profileName);

        public static int DeleteProfileFiles(string root, string profileName)
        {
            int deleted = EnumerateProfileFrameIds(root, profileName)
                .Sum(id => DeleteFrameFiles(root, profileName, id));
            RemoveFolderIfEmpty(ProfileFramesDir(root, profileName));
            return deleted;
        }

        #endregion

        #region Clear frame items (empty-to-desktop flow)

        /// <summary>
        /// Removes the items backed by files inside the frame folder from the frame's
        /// data (main list + every tab), leaving legacy items alone. For the ACTIVE
        /// profile this edits the in-memory frame, saves, and refreshes the frame window;
        /// for any other profile it rewrites that profile's frames.json directly (same
        /// shape FrameDataManager writes, via AtomicFile) without switching profiles.
        /// Returns the number of items removed.
        /// </summary>
        public static int ClearFrameItems(string profileName, string frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId)) return 0;
            string folder = FrameStore.BuildFrameFolderPath(FrameStore.RootDir, profileName, frameId);

            bool isActive = string.Equals(profileName, ProfileManager.CurrentProfileName, StringComparison.OrdinalIgnoreCase);
            if (isActive && FrameDataManager.FrameData != null)
            {
                dynamic? live = FrameDataManager.FindFrameById(frameId);
                if (live == null) return 0;

                int removed = RemoveItemsInsideFolder(live.Items as JArray, live.Tabs as JArray, folder);
                if (removed > 0)
                {
                    FrameDataManager.SaveFrameData();
                    Framemanager.RefreshFrameById(frameId);
                }
                return removed;
            }

            string jsonPath = Path.Combine(ProfileManager.GetProfileDir(profileName), "frames.json");
            return ClearFrameItemsInJson(jsonPath, frameId, folder);
        }

        /// <summary>
        /// Pure file variant of <see cref="ClearFrameItems"/>: loads a frames.json array,
        /// strips the items inside <paramref name="frameFolder"/> from the frame with
        /// <paramref name="frameId"/>, and writes it back atomically (only when something
        /// changed). Returns the number of items removed.
        /// </summary>
        public static int ClearFrameItemsInJson(string framesJsonPath, string frameId, string frameFolder)
        {
            if (!File.Exists(framesJsonPath)) return 0;

            JArray frames;
            try
            {
                var token = JToken.Parse(File.ReadAllText(framesJsonPath));
                frames = token as JArray ?? new JArray(token);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Settings,
                    $"FrameFileOperations: could not parse '{framesJsonPath}': {ex.Message}");
                return 0;
            }

            var frame = frames.OfType<JObject>().FirstOrDefault(f => f["Id"]?.ToString() == frameId);
            if (frame == null) return 0;

            int removed = RemoveItemsInsideFolder(frame["Items"] as JArray, frame["Tabs"] as JArray, frameFolder);
            if (removed > 0)
                AtomicFile.WriteAllText(framesJsonPath, JsonConvert.SerializeObject(frames, Formatting.Indented));
            return removed;
        }

        /// <summary>
        /// Removes from <paramref name="items"/> and from every tab's Items the entries whose
        /// Filename lies inside <paramref name="frameFolder"/>. Pure (no IO).
        /// </summary>
        public static int RemoveItemsInsideFolder(JArray? items, JArray? tabs, string frameFolder)
        {
            int removed = RemoveFromArray(items, frameFolder);
            if (tabs != null)
            {
                foreach (var tab in tabs.OfType<JObject>())
                    removed += RemoveFromArray(tab["Items"] as JArray, frameFolder);
            }
            return removed;
        }

        /// <summary>True when <paramref name="path"/> is an absolute path inside <paramref name="folder"/>.</summary>
        public static bool IsInsideFolder(string folder, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            try
            {
                string full = Path.GetFullPath(path);
                string folderFull = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static int RemoveFromArray(JArray? items, string frameFolder)
        {
            if (items == null) return 0;
            var doomed = items.Where(i => IsInsideFolder(frameFolder, i["Filename"]?.ToString())).ToList();
            foreach (var item in doomed) items.Remove(item);
            return doomed.Count;
        }

        #endregion

        #region Helpers

        private static IReadOnlyList<string> EnumerateFilesIn(string folder)
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            try
            {
                return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling,
                    $"FrameFileOperations: could not enumerate '{folder}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>Deletes <paramref name="folder"/> (including empty sub-folders) only when it contains no files.</summary>
        private static void RemoveFolderIfEmpty(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any()) return;
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling,
                    $"FrameFileOperations: could not remove folder '{folder}': {ex.Message}");
            }
        }

        #endregion
    }
}
