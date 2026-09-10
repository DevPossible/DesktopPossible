using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Desktop_Frames
{
    /// <summary>An integer pixel rectangle (device space), as persisted in layouts.json.</summary>
    public sealed class PxRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }

        public PxRect() { }
        public PxRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    }

    /// <summary>
    /// One monitor of a display configuration: identity, geometry (physical pixels) and
    /// scale factor. Bounds/WorkArea are in device pixels exactly as Windows reports them;
    /// the layout math converts to device-independent units with <see cref="Scale"/>.
    /// </summary>
    public sealed class MonitorInfo
    {
        /// <summary>
        /// Stable hardware identity, e.g. "MONITOR\DEL4099" (EDID vendor + product, from
        /// EnumDisplayDevices). Deliberately trimmed of the per-instance suffix: two identical
        /// panels then share an Id and are told apart by position, which is all the layout math
        /// needs. Empty when the identity could not be read — the fingerprint stays stable
        /// because every monitor degrades the same way.
        /// </summary>
        public string Id { get; set; } = "";
        public PxRect Bounds { get; set; } = new PxRect();
        public PxRect WorkArea { get; set; } = new PxRect();
        public double Scale { get; set; } = 1.0;
        public bool Primary { get; set; }
    }

    /// <summary>A frame's geometry in WPF device-independent units, as stored per display config.</summary>
    public sealed class FrameRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }

        /// <summary>Restore height for rolled-up frames; 0 when the frame has never been rolled.</summary>
        public double UnrolledHeight { get; set; }

        public FrameRect Clone() => new FrameRect { X = X, Y = Y, W = W, H = H, UnrolledHeight = UnrolledHeight };

        /// <summary>True when both rects describe the same geometry to within half a unit.</summary>
        public bool Matches(FrameRect? other) =>
            other != null &&
            Math.Abs(X - other.X) < 0.5 && Math.Abs(Y - other.Y) < 0.5 &&
            Math.Abs(W - other.W) < 0.5 && Math.Abs(H - other.H) < 0.5 &&
            Math.Abs(UnrolledHeight - other.UnrolledHeight) < 0.5;
    }

    /// <summary>
    /// Pure display-configuration math: fingerprinting a monitor set, matching an old set to a
    /// new one, and remapping a frame between the two. No IO and no Win32 — every method here
    /// is headless-testable (see DisplayConfigTests).
    /// </summary>
    public static class DisplayConfig
    {
        /// <summary>
        /// Short stable hash of a monitor set: identity + bounds + scale + primary flag,
        /// ordered by position so enumeration order cannot change it.
        ///
        /// The WORK AREA is deliberately excluded. Including it would mint a brand new
        /// "display configuration" every time the taskbar auto-hides, moves, or changes
        /// height, reflowing the whole desktop for something that only shifts the usable edge
        /// by a few pixels. Work area drives the layout math, not the identity.
        /// </summary>
        public static string Fingerprint(IReadOnlyList<MonitorInfo>? monitors)
        {
            if (monitors == null || monitors.Count == 0) return "";

            var lines = monitors
                .OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
                .ThenBy(m => m.Id ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(m => string.Format(CultureInfo.InvariantCulture, "{0}|{1},{2},{3},{4}|{5:0.###}|{6}",
                    (m.Id ?? "").ToUpperInvariant(),
                    m.Bounds.X, m.Bounds.Y, m.Bounds.W, m.Bounds.H,
                    m.Scale, m.Primary ? "P" : "-"));

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        /// <summary>Human-readable summary for logs and the Options page.</summary>
        public static string Describe(IReadOnlyList<MonitorInfo>? monitors)
        {
            if (monitors == null || monitors.Count == 0) return "no monitors";

            var parts = monitors
                .OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
                .Select(m => string.Format(CultureInfo.InvariantCulture, "{0}x{1} @{2:0}%",
                    m.Bounds.W, m.Bounds.H, m.Scale * 100));

            return $"{monitors.Count} monitor{(monitors.Count == 1 ? "" : "s")}: {string.Join(", ", parts)}";
        }

        /// <summary>Work area of a monitor in device-independent units.</summary>
        public static (double X, double Y, double W, double H) WorkAreaDiu(MonitorInfo m)
        {
            double s = m.Scale > 0.01 ? m.Scale : 1.0;
            return (m.WorkArea.X / s, m.WorkArea.Y / s, m.WorkArea.W / s, m.WorkArea.H / s);
        }

        /// <summary>Full bounds of a monitor in device-independent units.</summary>
        public static (double X, double Y, double W, double H) BoundsDiu(MonitorInfo m)
        {
            double s = m.Scale > 0.01 ? m.Scale : 1.0;
            return (m.Bounds.X / s, m.Bounds.Y / s, m.Bounds.W / s, m.Bounds.H / s);
        }

        /// <summary>Index of the primary monitor, or 0 when none is flagged.</summary>
        public static int PrimaryIndex(IReadOnlyList<MonitorInfo> monitors)
        {
            for (int i = 0; i < monitors.Count; i++) if (monitors[i].Primary) return i;
            return 0;
        }

        /// <summary>
        /// The monitor a frame belongs to: the one it overlaps most, falling back to the one
        /// whose centre is nearest when the frame lies entirely off every screen. Returns -1
        /// for an empty monitor set.
        /// </summary>
        public static int MonitorForRect(FrameRect rect, IReadOnlyList<MonitorInfo>? monitors)
        {
            if (monitors == null || monitors.Count == 0) return -1;

            int best = -1;
            double bestArea = 0;
            for (int i = 0; i < monitors.Count; i++)
            {
                var (x, y, w, h) = BoundsDiu(monitors[i]);
                double ox = Math.Max(0, Math.Min(rect.X + rect.W, x + w) - Math.Max(rect.X, x));
                double oy = Math.Max(0, Math.Min(rect.Y + rect.H, y + h) - Math.Max(rect.Y, y));
                double area = ox * oy;
                if (area > bestArea) { bestArea = area; best = i; }
            }
            if (best >= 0) return best;

            double cx = rect.X + rect.W / 2, cy = rect.Y + rect.H / 2, bestDist = double.MaxValue;
            for (int i = 0; i < monitors.Count; i++)
            {
                var (x, y, w, h) = BoundsDiu(monitors[i]);
                double dx = (x + w / 2) - cx, dy = (y + h / 2) - cy;
                double dist = dx * dx + dy * dy;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Maps every monitor of <paramref name="from"/> onto one of <paramref name="to"/>:
        /// hardware Id first (a monitor that comes back on a different port keeps its frames),
        /// then unmatched pairs by nearest normalised position (a 1080p replaced by a 4K in the
        /// same spot), and finally anything left over onto the new primary (a monitor that is
        /// simply gone). Returns an array indexed like <paramref name="from"/>.
        /// </summary>
        public static int[] MatchMonitors(IReadOnlyList<MonitorInfo> from, IReadOnlyList<MonitorInfo> to)
        {
            var result = new int[from.Count];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            if (to.Count == 0) return result;

            var taken = new bool[to.Count];

            // 1. Exact hardware identity.
            for (int i = 0; i < from.Count; i++)
            {
                string id = from[i].Id ?? "";
                if (id.Length == 0) continue;
                for (int j = 0; j < to.Count; j++)
                {
                    if (taken[j] || !string.Equals(id, to[j].Id ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    result[i] = j;
                    taken[j] = true;
                    break;
                }
            }

            // 2. Nearest normalised centre, globally greedy (the closest pair claims first).
            var fromBox = Envelope(from);
            var toBox = Envelope(to);
            var pairs = new List<(double Dist, int From, int To)>();
            for (int i = 0; i < from.Count; i++)
            {
                if (result[i] >= 0) continue;
                var (fx, fy) = NormalisedCentre(from[i], fromBox);
                for (int j = 0; j < to.Count; j++)
                {
                    if (taken[j]) continue;
                    var (tx, ty) = NormalisedCentre(to[j], toBox);
                    pairs.Add(((fx - tx) * (fx - tx) + (fy - ty) * (fy - ty), i, j));
                }
            }
            foreach (var (_, i, j) in pairs.OrderBy(p => p.Dist))
            {
                if (result[i] >= 0 || taken[j]) continue;
                result[i] = j;
                taken[j] = true;
            }

            // 3. Monitors with no counterpart at all land on the primary.
            int primary = PrimaryIndex(to);
            for (int i = 0; i < result.Length; i++) if (result[i] < 0) result[i] = primary;
            return result;
        }

        /// <summary>
        /// Moves a frame from one display configuration to another, keeping it as close to
        /// where the user put it as the new geometry allows.
        ///
        /// Position is preserved as a fraction of the FREE SPACE in the work area rather than
        /// of the work area itself: a frame hugging the left edge stays hugging the left edge,
        /// one pinned to the right stays pinned right, a centred one stays centred — and the
        /// result lands inside the work area by construction, so no clamping pass is needed.
        /// Size stays in device-independent units (the frame keeps the size the user chose)
        /// and only shrinks when the new work area is smaller than the frame itself.
        /// </summary>
        public static FrameRect Remap(FrameRect rect, IReadOnlyList<MonitorInfo>? from, IReadOnlyList<MonitorInfo>? to)
        {
            ArgumentNullException.ThrowIfNull(rect);
            if (to == null || to.Count == 0) return rect.Clone();

            int fromIndex = (from != null && from.Count > 0) ? MonitorForRect(rect, from) : -1;

            int toIndex;
            if (fromIndex < 0 || from == null)
            {
                toIndex = PrimaryIndex(to);
            }
            else
            {
                toIndex = MatchMonitors(from, to)[fromIndex];
                if (toIndex < 0 || toIndex >= to.Count) toIndex = PrimaryIndex(to);
            }

            var (nx, ny, nw, nh) = WorkAreaDiu(to[toIndex]);
            var (ox, oy, ow, oh) = fromIndex >= 0 && from != null
                ? WorkAreaDiu(from[fromIndex])
                : (nx, ny, nw, nh);

            double w = Math.Min(rect.W, nw);
            double h = Math.Min(rect.H, nh);

            double fx = FreeSpaceFraction(rect.X - ox, ow - rect.W);
            double fy = FreeSpaceFraction(rect.Y - oy, oh - rect.H);

            return new FrameRect
            {
                X = Math.Round(nx + fx * (nw - w)),
                Y = Math.Round(ny + fy * (nh - h)),
                W = Math.Round(w),
                H = Math.Round(h),
                UnrolledHeight = rect.UnrolledHeight > 0
                    ? Math.Round(Math.Min(rect.UnrolledHeight, nh))
                    : rect.UnrolledHeight
            };
        }

        /// <summary>Where the frame sat in the old free space: 0 = flush left/top, 1 = flush right/bottom.</summary>
        private static double FreeSpaceFraction(double offset, double free)
            => free <= 0.5 ? 0 : Math.Clamp(offset / free, 0, 1);

        /// <summary>Bounding box of every monitor's pixel bounds.</summary>
        private static PxRect Envelope(IReadOnlyList<MonitorInfo> monitors)
        {
            if (monitors.Count == 0) return new PxRect(0, 0, 1, 1);
            int left = monitors.Min(m => m.Bounds.X);
            int top = monitors.Min(m => m.Bounds.Y);
            int right = monitors.Max(m => m.Bounds.X + m.Bounds.W);
            int bottom = monitors.Max(m => m.Bounds.Y + m.Bounds.H);
            return new PxRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private static (double X, double Y) NormalisedCentre(MonitorInfo m, PxRect box)
            => ((m.Bounds.X + m.Bounds.W / 2.0 - box.X) / box.W,
                (m.Bounds.Y + m.Bounds.H / 2.0 - box.Y) / box.H);
    }
}
