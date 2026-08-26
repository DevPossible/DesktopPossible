using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Keeps Data-frame items in sync with outside file operations on their backing files
    /// (the shortcuts/files stored inside the per-frame store folders). The icon context menu
    /// is the native shell menu, so Windows' own Delete/Rename act directly on the backing
    /// file — and Explorer or scripts can touch the store too. One recursive watcher on the
    /// store root (a single ReadDirectoryChangesW handle) covers every profile and frame:
    /// a deleted backing file drops the now-orphaned item entry (no more broken icons),
    /// a renamed one has its item follow the new name.
    /// </summary>
    public static class FrameStoreWatcher
    {
        private static FileSystemWatcher? _watcher;
        private static DispatcherTimer? _debounce;
        // old full path -> new full path (null = deleted). Debounced: shell operations fire
        // bursts of events, and a slow delete-to-recycle-bin settles within the window.
        private static readonly Dictionary<string, string?> _pending = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        public static void Start()
        {
            if (_watcher != null) return;
            try
            {
                string root = FrameStore.RootDir;
                Directory.CreateDirectory(root);

                _watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 32 * 1024
                };
                _watcher.Deleted += (s, e) => Queue(e.FullPath, null);
                _watcher.Renamed += (s, e) => Queue(e.OldFullPath, e.FullPath);
                _watcher.EnableRaisingEvents = true;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"FrameStoreWatcher: watching '{root}' (recursive)");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"FrameStoreWatcher: failed to start: {ex.Message}");
                _watcher?.Dispose();
                _watcher = null;
            }
        }

        public static void Stop()
        {
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
            _debounce?.Stop();
        }

        private static void Queue(string oldPath, string? newPath)
        {
            lock (_lock) { _pending[oldPath] = newPath; }

            // Watcher events arrive on a threadpool thread; frame data is UI-thread state.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_debounce == null)
                {
                    _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    _debounce.Tick += (s, e) => { _debounce!.Stop(); Process(); };
                }
                _debounce.Stop();
                _debounce.Start();
            }));
        }

        private static void Process()
        {
            KeyValuePair<string, string?>[] batch;
            lock (_lock)
            {
                batch = _pending.ToArray();
                _pending.Clear();
            }
            if (batch.Length == 0) return;

            var touchedFrames = new HashSet<string>();
            bool changed = false;

            try
            {
                foreach (var entry in batch)
                {
                    string oldPath = entry.Key;
                    string? newPath = entry.Value;

                    // A move/replace can fire Deleted even though the file comes straight
                    // back — only reconcile when the old path is really gone.
                    if (File.Exists(oldPath) || Directory.Exists(oldPath)) continue;

                    bool renamed = newPath != null && (File.Exists(newPath) || Directory.Exists(newPath));

                    foreach (dynamic frame in FrameDataManager.FrameData.ToList())
                    {
                        if (frame?.ItemsType?.ToString() != "Data") continue;
                        string? frameId = null;
                        try { frameId = frame.Id?.ToString(); } catch { }

                        foreach (JArray items in ItemArrays(frame))
                        {
                            var match = items.OfType<JObject>()
                                .FirstOrDefault(i => PathEquals(i["Filename"]?.ToString(), oldPath));
                            if (match == null) continue;

                            if (renamed)
                            {
                                match["Filename"] = newPath; // follow the rename
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                                    $"FrameStoreWatcher: backing file renamed, item follows: '{oldPath}' -> '{newPath}'");
                            }
                            else
                            {
                                items.Remove(match); // deleted outside the app: drop the orphaned entry
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                                    $"FrameStoreWatcher: backing file deleted, removed orphaned item '{oldPath}'");
                            }

                            changed = true;
                            if (!string.IsNullOrEmpty(frameId)) touchedFrames.Add(frameId!);
                        }
                    }
                }

                if (!changed) return;
                FrameDataManager.SaveFrameData();
                foreach (string id in touchedFrames) Framemanager.RefreshFrameById(id);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"FrameStoreWatcher: reconcile failed: {ex.Message}");
            }
        }

        /// <summary>The frame's main Items array plus every tab's Items array.</summary>
        private static IEnumerable<JArray> ItemArrays(dynamic frame)
        {
            JArray? main = null;
            try { main = frame.Items as JArray; } catch { }
            if (main != null) yield return main;

            JArray? tabs = null;
            try { tabs = frame.Tabs as JArray; } catch { }
            if (tabs == null) yield break;
            foreach (var tab in tabs.OfType<JObject>())
            {
                if (tab["Items"] is JArray tabItems) yield return tabItems;
            }
        }

        private static bool PathEquals(string? candidate, string fullPath)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(candidate), fullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
