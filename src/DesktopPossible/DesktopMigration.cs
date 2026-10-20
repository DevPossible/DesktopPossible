using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Background migration from the folder-backed frames (items stored in the frame store or
    /// a profile's Shortcuts folder) to "Keep framed items on the Desktop": each stored item
    /// becomes a real file on the Desktop, stays in its frame at the same position, and its
    /// desktop icon is parked by <see cref="DesktopIconParker"/>. Covers EVERY profile: desktop
    /// icons are shared by all profiles and virtual desktops, and the parker hides items framed
    /// in any of them.
    ///
    /// Runs when the option is on: after startup, after the option is switched on, and after
    /// a profile switch. The order is what makes it safe:
    ///  1. Plan (UI thread) — nothing changes yet.
    ///  2. Preconditions — the desktop view must be able to hide icons (Explorer running,
    ///     "Auto arrange icons" off), or the migrated files would pile up visibly: postpone.
    ///  3. Backup — settings and frame data (every profile's frames.json) before the first
    ///     change. The files themselves are never at risk: they are moved by rename on the same
    ///     drive, or copied and verified before the source is deleted.
    ///  4. Journal — every planned step to desktop-migration.json, so an interrupted run
    ///     resumes instead of moving the same file twice.
    ///  5. Re-point items, save — BEFORE any file appears: each file is already framed when it
    ///     lands, so auto-organize skips it (also ignored explicitly) and the parker hides its
    ///     icon at once. GridCol/GridRow/DisplayOrder and every other field are kept.
    ///  6. Move (same drive: an atomic rename) or copy + verify + delete the source.
    ///  7. A failed step is reverted — the item points back at its source, which is intact —
    ///     and retried next launch.
    ///  8. Tidy — empty frame folders removed, frames refreshed, the steps kept beside the
    ///     backup as the record of what moved where, one toast.
    /// </summary>
    public static class DesktopMigration
    {
        private static string JournalPath => Path.Combine(AppPaths.DataRoot, "desktop-migration.json");
        private static bool _running;
        private static bool _postponeNotified;

        /// <summary>Starts a background migration pass when the option is on. Call on the UI thread.</summary>
        public static void StartIfNeeded()
        {
            if (_running || !SettingsManager.KeepFramedItemsOnDesktop) return;
            _running = true;
            _ = RunAsync();
        }

        private static async Task RunAsync()
        {
            try
            {
                // ---- 1. Plan ----
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var profiles = AllProfileFrames.Load();
                var legacyFolders = profiles.SelectMany(p => new[] { Path.Combine(p.Dir, "Shortcuts"), Path.Combine(p.Dir, "Temp Shortcuts") }).ToList();
                bool IsLegacy(string path) => FrameStore.IsInsideAnyFrameFolder(path) || legacyFolders.Any(f => DesktopMigrationPlanner.IsInside(path, f));

                var journal = LoadJournal();
                var plan = DesktopMigrationPlanner.Plan(
                    profiles.SelectMany(p => AllProfileFrames.Items(p).Select(i => i.FullPath)).ToList(),
                    IsLegacy,
                    p => File.Exists(p) || Directory.Exists(p),
                    p => FilePathUtilities.GetShortcutTargetUnicodeSafe(p),
                    DesktopIconParker.IsDesktopItem,
                    desktopDir,
                    Directory.EnumerateFileSystemEntries(desktopDir).Select(Path.GetFileName).OfType<string>(),
                    journal,
                    SameFileContent);
                if (plan.Count == 0) { if (journal.Count > 0) SaveJournal(plan); return; }

                // ---- 2. Preconditions ----
                if (DesktopFolderView.IsAutoArrange() != false)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        $"Desktop migration postponed ({plan.Count} item(s)): desktop view unavailable or 'Auto arrange icons' on.");
                    if (!_postponeNotified)
                    {
                        _postponeNotified = true;
                        SmartToast.Show("Frame items not moved yet",
                            "Turn off \"Auto arrange icons\" (right-click the desktop → View) so framed items can live on the Desktop.");
                    }
                    return;
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Desktop migration: {plan.Count} item(s) across {profiles.Count} profile(s) to move out of frame folders.");

                // Overlay the active profile's affected frames for the whole run.
                var forward = plan.ToDictionary(e => e.Source, e => e.Dest, StringComparer.OrdinalIgnoreCase);
                var resumed = plan.GroupBy(e => e.Dest, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Key, StringComparer.OrdinalIgnoreCase);
                foreach (string id in Repoint(profiles, forward, null).Union(Repoint(profiles, resumed, null)))
                    FrameBusyOverlay.Show(id, "Migrating…");

                // ---- 3. Backup (skipped when resuming: the first pass already made it) ----
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
                if (journal.Count == 0)
                {
                    string name = $"Before desktop migration {stamp}";
                    await Task.Run(() => BackupManager.CreateBackup(name, silent: true, includeStore: false));
                    if (!File.Exists(Path.Combine(BackupManager.GetBackupsFolderPath(), name + ".zip")))
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            "Desktop migration aborted: the safety backup could not be created.");
                        return;
                    }
                }

                // ---- 4. Journal ----
                SaveJournal(plan);

                // ---- 5. Re-point items first, then let the files appear ----
                AutoOrganizeManager.Ignore(plan.Select(e => e.Dest), TimeSpan.FromMinutes(10));
                var changes = new List<(ProfileFrameSet Set, JObject Item, string OldFilename, string Source)>();
                var touched = Repoint(profiles, forward, changes);

                // ---- 6 + 7. Move / copy, revert what failed ----
                var failed = await Task.Run(() => Transfer(plan, desktopDir));
                if (failed.Count > 0) Revert(profiles, changes, failed);

                // ---- 8. Tidy ----
                FrameBusyOverlay.HideAll();
                RemoveEmptyFrameFolders(profiles);
                foreach (string id in touched) Framemanager.RefreshFrameById(id);
                KeepRecord(plan.Except(failed).ToList(), stamp);
                SaveJournal(new List<DesktopMigrationEntry>());

                int done = plan.Count - failed.Count;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Desktop migration finished: {done} moved, {failed.Count} failed (kept in place, retried next launch).");
                if (done > 0)
                    SmartToast.Show("Frame items moved to the Desktop",
                        $"{done} item(s) now live on the Desktop and stay in their frames; their desktop icons are hidden while DesktopPossible runs.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Desktop migration failed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                FrameBusyOverlay.HideAll();
                _running = false;
            }
        }

        #region File transfer (background thread)

        /// <summary>Performs every step; returns the ones that did not complete.</summary>
        private static List<DesktopMigrationEntry> Transfer(List<DesktopMigrationEntry> plan, string desktopDir)
        {
            var failed = new List<DesktopMigrationEntry>();
            bool SameDrive(string p) => string.Equals(Path.GetPathRoot(Path.GetFullPath(p)), Path.GetPathRoot(desktopDir), StringComparison.OrdinalIgnoreCase);

            foreach (var entry in plan)
            {
                try
                {
                    bool sourceIsDir = Directory.Exists(entry.Source);
                    bool sourceExists = sourceIsDir || File.Exists(entry.Source);
                    bool destExists = File.Exists(entry.Dest) || Directory.Exists(entry.Dest);

                    if (!entry.CopyFile)
                    {
                        // The item now points at a desktop item that already exists; the source
                        // is a wrapper shortcut or an identical duplicate.
                        if (!destExists) throw new IOException("desktop target missing");
                        if (sourceExists) Delete(entry.Source, sourceIsDir);
                        continue;
                    }

                    if (!sourceExists)
                    {
                        // Resumed step whose move already happened.
                        if (!destExists) throw new IOException("source and destination both missing");
                        continue;
                    }
                    if (destExists && !IsCompleteCopy(entry.Source, entry.Dest, sourceIsDir))
                        throw new IOException("destination already exists and differs");

                    if (!destExists && SameDrive(entry.Source))
                    {
                        // Same drive: an atomic rename — the file is either here or there.
                        if (sourceIsDir) Directory.Move(entry.Source, entry.Dest);
                        else File.Move(entry.Source, entry.Dest);
                        continue;
                    }

                    if (!destExists)
                    {
                        if (sourceIsDir) BackupManager.CopyDirectory(entry.Source, entry.Dest);
                        else File.Copy(entry.Source, entry.Dest);
                    }
                    if (!IsCompleteCopy(entry.Source, entry.Dest, sourceIsDir)) throw new IOException("copy could not be verified");
                    Delete(entry.Source, sourceIsDir);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        $"Desktop migration: '{entry.Source}' -> '{entry.Dest}' failed: {ex.Message}");
                    failed.Add(entry);

                    // The item points back at its source. Drop our (possibly partial) copy — but
                    // ONLY while the source still exists: after a completed move the destination
                    // is the only copy. And never a non-copy Dest: that is the user's own item.
                    if (entry.CopyFile && (File.Exists(entry.Source) || Directory.Exists(entry.Source)))
                    {
                        try
                        {
                            if (Directory.Exists(entry.Dest)) Directory.Delete(entry.Dest, true);
                            else if (File.Exists(entry.Dest)) File.Delete(entry.Dest);
                        }
                        catch { }
                    }
                }
            }
            return failed;
        }

        private static void Delete(string path, bool isDir)
        {
            if (isDir) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }

        /// <summary>Same file size, or for a folder the same file count and total size.</summary>
        private static bool IsCompleteCopy(string source, string dest, bool isDir)
        {
            if (!isDir) return File.Exists(dest) && new FileInfo(source).Length == new FileInfo(dest).Length;
            if (!Directory.Exists(dest)) return false;
            static (int, long) Measure(string d)
            {
                var files = new DirectoryInfo(d).GetFiles("*", SearchOption.AllDirectories);
                return (files.Length, files.Sum(f => f.Length));
            }
            return Measure(source) == Measure(dest);
        }

        /// <summary>Byte-identical small files (two profiles' copies of one shortcut share one desktop file).</summary>
        private static bool SameFileContent(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a); var fb = new FileInfo(b);
                if (!fa.Exists || !fb.Exists || fa.Length != fb.Length || fa.Length > 4 * 1024 * 1024) return false;
                return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
            }
            catch { return false; }
        }

        #endregion

        #region Frame data (UI thread)

        /// <summary>
        /// Rewrites matching item Filenames in every profile (all frames, all tabs), recording each
        /// change in <paramref name="changes"/>, and saves the changed profiles — or with
        /// <paramref name="changes"/> null only finds them. Returns the Ids of the ACTIVE
        /// profile's frames holding a match (the ones on screen).
        /// </summary>
        private static HashSet<string> Repoint(List<ProfileFrameSet> profiles, Dictionary<string, string> map,
            List<(ProfileFrameSet Set, JObject Item, string OldFilename, string Source)>? changes)
        {
            var touched = new HashSet<string>();
            foreach (var set in profiles)
            {
                bool changed = false;
                foreach (var (item, full, frame) in AllProfileFrames.Items(set).ToList())
                {
                    if (!map.TryGetValue(full, out var dest)) continue;
                    if (set.IsActive) touched.Add((string)frame.Id.ToString());
                    if (changes == null || string.Equals(full, dest, StringComparison.OrdinalIgnoreCase)) continue;
                    changes.Add((set, item, item["Filename"]!.ToString(), full));
                    item["Filename"] = dest;
                    changed = true;
                }
                if (changed) set.Save();
            }
            return touched;
        }

        /// <summary>Points every item of a failed step back at exactly what it held before, and saves.</summary>
        private static void Revert(List<ProfileFrameSet> profiles,
            List<(ProfileFrameSet Set, JObject Item, string OldFilename, string Source)> changes,
            List<DesktopMigrationEntry> failed)
        {
            var failedSources = new HashSet<string>(failed.Select(e => e.Source), StringComparer.OrdinalIgnoreCase);
            foreach (var group in changes.Where(c => failedSources.Contains(c.Source)).GroupBy(c => c.Set))
            {
                foreach (var c in group) c.Item["Filename"] = c.OldFilename;
                group.Key.Save();
            }

            // Steps resumed from the journal were re-pointed by an earlier run (no record here):
            // a CopyFile Dest is a unique name of ours, so items on a missing one go back to its source.
            var orphaned = failed.Where(e => e.CopyFile && !File.Exists(e.Dest) && !Directory.Exists(e.Dest))
                .ToDictionary(e => e.Dest, e => e.Source, StringComparer.OrdinalIgnoreCase);
            if (orphaned.Count == 0) return;
            foreach (var set in profiles)
            {
                bool changed = false;
                foreach (var (item, full, _) in AllProfileFrames.Items(set).ToList())
                    if (orphaned.TryGetValue(full, out var source)) { item["Filename"] = source; changed = true; }
                if (changed) set.Save();
            }
        }

        private static void RemoveEmptyFrameFolders(List<ProfileFrameSet> profiles)
        {
            foreach (var set in profiles)
                foreach (dynamic frame in set.Frames)
                {
                    try { FrameFileOperations.RemoveFrameFolder(Path.GetFileName(set.Dir), frame.Id?.ToString()); } catch { }
                }
        }

        #endregion

        #region Journal

        private static List<DesktopMigrationEntry> LoadJournal()
        {
            try
            {
                if (!File.Exists(JournalPath)) return new();
                return JArray.Parse(File.ReadAllText(JournalPath)).OfType<JObject>()
                    .Select(o => new DesktopMigrationEntry(o["Source"]?.ToString() ?? "", o["Dest"]?.ToString() ?? "", (bool?)o["CopyFile"] ?? true))
                    .Where(e => e.Source.Length > 0 && e.Dest.Length > 0)
                    .ToList();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"desktop-migration.json unreadable: {ex.Message}");
                return new();
            }
        }

        private static string Serialize(List<DesktopMigrationEntry> entries) =>
            new JArray(entries.Select(e => new JObject { ["Source"] = e.Source, ["Dest"] = e.Dest, ["CopyFile"] = e.CopyFile }))
                .ToString(Formatting.Indented);

        private static void SaveJournal(List<DesktopMigrationEntry> plan)
        {
            if (plan.Count == 0) { try { File.Delete(JournalPath); } catch { } return; }
            AtomicFile.WriteAllText(JournalPath, Serialize(plan));
        }

        /// <summary>Keeps what moved where beside the backup, so a migration can be traced or undone by hand.</summary>
        private static void KeepRecord(List<DesktopMigrationEntry> done, string stamp)
        {
            if (done.Count == 0) return;
            try { AtomicFile.WriteAllText(Path.Combine(BackupManager.GetBackupsFolderPath(), $"Desktop migration {stamp}.json"), Serialize(done)); }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Desktop migration record not written: {ex.Message}"); }
        }

        #endregion
    }
}
