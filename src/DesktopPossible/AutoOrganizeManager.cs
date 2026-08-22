using System;
using System.IO;
using System.Threading.Tasks;

namespace Desktop_Frames
{
    /// <summary>
    /// Watches the desktop and, when Auto-Organize is enabled, automatically
    /// categorizes NEW desktop arrivals (app shortcuts, executables, documents and
    /// images) into their fixed category frame via AppCategorizer. The old user-defined rules engine
    /// (auto_organize.json + Smart Desktop Rules dialog) was replaced by the fixed
    /// app-categorization engine; unclassifiable items are left alone.
    /// </summary>
    public static class AutoOrganizeManager
    {
        private static FileSystemWatcher _watcher;
        private static readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        public static void Initialize()
        {
            if (SettingsManager.EnableAutoOrganize) Start();
        }

        /// <summary>Re-syncs the engine after a profile switch: starts/stops the watcher
        /// per the new profile's EnableAutoOrganize setting. Respects an active UI pause
        /// (the Options form paused the watcher and will Resume() it on close — a profile
        /// switch must not silently un-pause it underneath it).</summary>
        public static void ReinitializeForProfile()
        {
            try
            {
                if (SettingsManager.EnableAutoOrganize)
                {
                    bool pausedByUi = OptionsFormManager.IsOpen;

                    if (_watcher == null)
                    {
                        Start();
                        if (pausedByUi) Pause();
                    }
                    else if (!pausedByUi)
                    {
                        _watcher.EnableRaisingEvents = true;
                    }
                }
                else
                {
                    Stop();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Auto-Organize reinitialize for profile failed: {ex.Message}");
            }
        }

        public static void Start()
        {
            if (_watcher != null) return;

            try
            {
                _watcher = new FileSystemWatcher(_desktopPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                // --- THE BROWSER SHIELD ---
                // Delay exactly 3 seconds before processing. This prevents browsers/installers
                // from panicking when we act on a file before they finish writing it.
                _watcher.Created += (s, e) => Task.Run(async () => { await Task.Delay(3000); await ProcessFileAsync(e.FullPath); });
                _watcher.Renamed += (s, e) => Task.Run(async () => { await Task.Delay(3000); await ProcessFileAsync(e.FullPath); });

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Organize engine started (app categorization).");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to start Auto-Organize: {ex.Message}");
            }
        }

        public static void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Organize engine stopped.");
            }
        }

        /// <summary>
        /// Handles one new desktop arrival: only app-like items (.lnk/.url/.exe) are
        /// considered; the classifier decides the category, and unclassified items are
        /// left alone. Active temp downloads never match the extension filter.
        /// </summary>
        private static async Task ProcessFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || !SettingsManager.EnableAutoOrganize) return;

                // App-like items (.lnk/.url/.exe) plus documents and images by extension.
                if (!AppCategorizer.IsCandidateFile(filePath)) return;

                // --- THE DOWNLOAD WAITER ---
                // Wait up to 60 seconds for the writer to release the file lock.
                if (!await WaitForFileUnlockAsync(filePath, 60000))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        $"Auto-Organize skipped {Path.GetFileName(filePath)}: File was locked.");
                    return;
                }

                string displayName = Path.GetFileNameWithoutExtension(filePath);
                var (moved, _) = await AppCategorizer.SortDesktopAsync(new[] { filePath });

                if (moved > 0 && SettingsManager.EnableAutoOrganizeNotifications)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SmartToast.Show("Auto-Categorize", $"Moved '{displayName}' into its category frame");
                    }));
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Auto-Organize failed for {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        private static async Task<bool> WaitForFileUnlockAsync(string filePath, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        return true; // Lock acquired successfully, file is ready
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(500); // Wait half a second and try again
                }
                catch (UnauthorizedAccessException)
                {
                    // Read-only/ACL-guarded file (e.g. AV scanner holding it): retry like a
                    // lock instead of letting the exception kill the watcher's task.
                    await Task.Delay(500);
                }
            }
            return false; // Timed out
        }

        public static void Pause()
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
        }

        public static void Resume()
        {
            if (!SettingsManager.EnableAutoOrganize) return;

            if (_watcher == null)
            {
                Start();
            }
            else
            {
                _watcher.EnableRaisingEvents = true;
            }
        }
    }
}
