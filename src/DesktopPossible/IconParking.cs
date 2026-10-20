using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames
{
    /// <summary>
    /// Pure rules for "parking" framed desktop icons. A framed item that lives on the Desktop
    /// stays a real file there; while the app runs its desktop icon is moved far off-screen
    /// (parked) so it only shows inside its frame, and its original position is remembered so
    /// it can be put back exactly when it leaves the frame or the app exits.
    ///
    /// Positions are desktop list-view coordinates as IFolderView reports them. Explorer clamps
    /// off-screen icons back into view on a refresh, so a parked icon can turn up in a screen
    /// corner; it is still framed, so the next reconcile simply parks it again (and its saved
    /// original position is never overwritten by that clamped spot).
    /// No I/O and no COM here: DesktopIconParker feeds in the live state and applies the plan.
    /// </summary>
    public static class IconParking
    {
        /// <summary>Where parked icons go: far beyond any real monitor layout.</summary>
        public const int ParkX = 30000, ParkY = 30000;

        /// <summary>An icon at or beyond this on both axes is treated as parked.</summary>
        public const int ParkedThreshold = 20000;

        public static bool IsParked((int X, int Y) pos) => pos.X >= ParkedThreshold && pos.Y >= ParkedThreshold;

        public sealed class Plan
        {
            /// <summary>Framed desktop items to move off-screen now.</summary>
            public List<string> Park { get; } = new();

            /// <summary>Unframed items to put back at their remembered position.</summary>
            public List<(string Path, int X, int Y)> Restore { get; } = new();

            /// <summary>The remembered original positions after this plan is applied.</summary>
            public Dictionary<string, (int X, int Y)> State { get; } = new(StringComparer.OrdinalIgnoreCase);

            public bool StateChanged { get; set; }
        }

        /// <summary>
        /// Computes what to park and restore.
        /// </summary>
        /// <param name="framed">Full paths of every item in every frame of the active profile.</param>
        /// <param name="current">Every file-system item currently in the desktop view, with its position.</param>
        /// <param name="saved">Remembered original positions of the icons parked so far.</param>
        /// <param name="exists">Whether a path still exists on disk (lets stale entries be dropped).</param>
        public static Plan Compute(ISet<string> framed,
            IReadOnlyDictionary<string, (int X, int Y)> current,
            IReadOnlyDictionary<string, (int X, int Y)> saved,
            Func<string, bool> exists)
        {
            var plan = new Plan();
            foreach (var kv in saved) plan.State[kv.Key] = kv.Value;

            // Framed desktop items: remember where they were (first time only), then park.
            foreach (var kv in current)
            {
                if (!framed.Contains(kv.Key) || IsParked(kv.Value)) continue;
                if (!plan.State.ContainsKey(kv.Key))
                {
                    plan.State[kv.Key] = kv.Value;
                    plan.StateChanged = true;
                }
                plan.Park.Add(kv.Key);
            }

            // Remembered items that are no longer framed.
            foreach (var kv in saved)
            {
                if (framed.Contains(kv.Key)) continue;

                if (current.TryGetValue(kv.Key, out var pos))
                {
                    // Still parked: put it back. Anywhere visible means the user (or a drop onto
                    // the desktop) already placed it — leave it there.
                    if (IsParked(pos)) plan.Restore.Add((kv.Key, kv.Value.X, kv.Value.Y));
                    plan.State.Remove(kv.Key);
                    plan.StateChanged = true;
                }
                else if (!exists(kv.Key))
                {
                    // Deleted or renamed away: nothing left to restore.
                    plan.State.Remove(kv.Key);
                    plan.StateChanged = true;
                }
                // Otherwise the desktop view hasn't listed it yet: keep the record for next time.
            }
            return plan;
        }

        /// <summary>
        /// Positions to put back on exit (or after a crash): every remembered icon still on the
        /// desktop. Remembered icons are all framed, so one found in view was only clamped there
        /// by Explorer and goes back to its original spot too.
        /// </summary>
        public static List<(string Path, int X, int Y)> RestoreAll(
            IReadOnlyDictionary<string, (int X, int Y)> current,
            IReadOnlyDictionary<string, (int X, int Y)> saved)
        {
            var moves = new List<(string Path, int X, int Y)>();
            foreach (var kv in saved)
                if (current.ContainsKey(kv.Key)) moves.Add((kv.Key, kv.Value.X, kv.Value.Y));
            return moves;
        }

        /// <summary>True when <paramref name="path"/> sits directly in one of the desktop folders.</summary>
        public static bool IsOnDesktop(string? path, params string?[] desktopFolders)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string? parent;
            try { parent = Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd('\\', '/')); }
            catch { return false; }
            if (parent == null) return false;
            foreach (var folder in desktopFolders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                try
                {
                    if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\', '/'), parent.TrimEnd('\\', '/'),
                            StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
            return false;
        }
    }
}
