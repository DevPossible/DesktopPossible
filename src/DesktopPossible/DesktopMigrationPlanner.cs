using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// One planned step of the frame-folder → Desktop migration: the item that points at
    /// <see cref="Source"/> will point at <see cref="Dest"/>. With <see cref="CopyFile"/> the
    /// source is copied to Dest (a free name on the Desktop) before the source is deleted;
    /// without it, Source is a wrapper shortcut whose target already lives on the Desktop, so
    /// the item points straight at that target and the wrapper is just deleted.
    /// </summary>
    public sealed record DesktopMigrationEntry(string Source, string Dest, bool CopyFile);

    /// <summary>
    /// Pure planning for <see cref="DesktopMigration"/>: which frame items still live in a
    /// legacy folder (the frame store or a profile's Shortcuts folder) and where each one goes.
    /// No I/O: the runner injects the file-system questions.
    /// </summary>
    public static class DesktopMigrationPlanner
    {
        /// <param name="itemPaths">Full paths of every item in the active profile's Data frames.</param>
        /// <param name="isLegacy">Whether a path is inside a legacy folder.</param>
        /// <param name="exists">Whether a file or folder exists.</param>
        /// <param name="shortcutTarget">The target of a .lnk, or null.</param>
        /// <param name="isDesktopItem">Whether a path sits directly on a desktop folder.</param>
        /// <param name="desktopDir">The user's Desktop folder (copies land here).</param>
        /// <param name="takenNames">Names already on the Desktop (case-insensitive).</param>
        /// <param name="journal">Steps left over by an interrupted run: kept as they are.</param>
        /// <param name="sameContent">
        /// Whether two files are identical: a file identical to one already planned under the
        /// same name (e.g. the same shortcut framed in two profiles) shares that desktop file.
        /// </param>
        public static List<DesktopMigrationEntry> Plan(
            IEnumerable<string> itemPaths,
            Func<string, bool> isLegacy,
            Func<string, bool> exists,
            Func<string, string?> shortcutTarget,
            Func<string, bool> isDesktopItem,
            string desktopDir,
            IEnumerable<string> takenNames,
            IEnumerable<DesktopMigrationEntry> journal,
            Func<string, string, bool>? sameContent = null)
        {
            var plan = new List<DesktopMigrationEntry>(journal);
            var taken = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
            var planned = new HashSet<string>(plan.Select(e => e.Source), StringComparer.OrdinalIgnoreCase);
            foreach (var e in plan) taken.Add(Path.GetFileName(e.Dest));

            foreach (string path in itemPaths)
            {
                // Tab 0 mirrors the main list, so the same file is usually listed twice.
                if (planned.Contains(path) || !isLegacy(path) || !exists(path)) continue;
                planned.Add(path);

                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string? target = shortcutTarget(path);
                    if (!string.IsNullOrEmpty(target) && isDesktopItem(target) && exists(target))
                    {
                        plan.Add(new DesktopMigrationEntry(path, target, CopyFile: false));
                        continue;
                    }
                }

                if (sameContent != null)
                {
                    var twin = plan.FirstOrDefault(e => e.CopyFile &&
                        string.Equals(Path.GetFileName(e.Source), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) &&
                        sameContent(e.Source, path));
                    if (twin != null)
                    {
                        plan.Add(new DesktopMigrationEntry(path, twin.Dest, CopyFile: false));
                        continue;
                    }
                }

                string name = UniqueName(Path.GetFileName(path), taken);
                taken.Add(name);
                plan.Add(new DesktopMigrationEntry(path, Path.Combine(desktopDir, name), CopyFile: true));
            }
            return plan;
        }

        /// <summary>
        /// <paramref name="fileName"/>, or "name (n).ext" with the lowest free n — the same
        /// convention as FrameStore.UniqueDestinationPath.
        /// </summary>
        public static string UniqueName(string fileName, ISet<string> taken)
        {
            if (!taken.Contains(fileName)) return fileName;
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            for (int n = 1; ; n++)
            {
                string candidate = $"{stem} ({n}){ext}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>True when <paramref name="path"/> is inside (below) <paramref name="folder"/>.</summary>
        public static bool IsInside(string? path, string? folder)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(f, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
