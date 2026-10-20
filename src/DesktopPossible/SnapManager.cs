using System;
using System.Collections.Generic;

namespace Desktop_Frames
{
    /// <summary>
    /// Guide-line snapping for frame move/resize gestures. Every other frame contributes
    /// infinite vertical guides at its left/right edges and horizontal guides at its top/bottom
    /// edges; every monitor's work area contributes its four edges. While dragging, whichever
    /// of the frame's edges is nearest a guide (within <see cref="ThresholdDip"/>) sticks to it;
    /// while resizing, only the edges being pulled snap. Frames side by side (overlapping on the
    /// other axis) snap to each other with a <see cref="GapDip"/> gap instead of touching.
    ///
    /// Pure geometry in device pixels: rects come from GetWindowRect and the WM_MOVING/WM_SIZING
    /// proposal, so mixed-DPI monitors need no DIP conversion. The window plumbing lives in
    /// Framemanager.SnapGestureRect.
    /// </summary>
    public static class SnapManager
    {
        /// <summary>How close (DIPs) an edge must come to a guide to stick to it.</summary>
        public const double ThresholdDip = 10;

        /// <summary>Gap (DIPs) kept between neighbouring frames snapped edge to edge.</summary>
        public const double GapDip = 10;

        /// <summary>A resize snap never shrinks the frame below this many device pixels.</summary>
        private const int MinSnappedSize = 40;

        /// <summary>
        /// Snaps a raw gesture rect to the guides of <paramref name="others"/> and
        /// <paramref name="workAreas"/>. <paramref name="edge"/> is FrameGrid.GestureMove for a
        /// move, otherwise the WM_SIZING edge code; a move keeps the size, a resize moves only
        /// the pulled edges. Returns the rect unchanged when no guide is within threshold.
        /// </summary>
        public static (int Left, int Top, int Right, int Bottom) SnapToGuides(
            (int Left, int Top, int Right, int Bottom) rect, int edge,
            IReadOnlyList<(int Left, int Top, int Right, int Bottom)> others,
            IReadOnlyList<(int Left, int Top, int Right, int Bottom)> workAreas,
            int threshold, int gap)
        {
            bool move = edge == FrameGrid.GestureMove;
            bool left = move || edge is FrameGrid.GestureLeft or FrameGrid.GestureTopLeft or FrameGrid.GestureBottomLeft;
            bool right = move || edge is FrameGrid.GestureRight or FrameGrid.GestureTopRight or FrameGrid.GestureBottomRight;
            bool top = move || edge is FrameGrid.GestureTop or FrameGrid.GestureTopLeft or FrameGrid.GestureTopRight;
            bool bottom = move || edge is FrameGrid.GestureBottom or FrameGrid.GestureBottomLeft or FrameGrid.GestureBottomRight;

            var (l, t, r, b) = rect;

            // Horizontal axis: neighbours are frames overlapping vertically.
            var xs = new List<(int Lo, int Hi, bool Neighbour)>();
            foreach (var o in others) xs.Add((o.Left, o.Right, t < o.Bottom && o.Top < b));
            var (dl, dr) = BestAxisSnap(l, r, left, right, xs, workAreas, wa => (wa.Left, wa.Right), threshold, gap);

            // Vertical axis: neighbours are frames overlapping horizontally.
            var ys = new List<(int Lo, int Hi, bool Neighbour)>();
            foreach (var o in others) ys.Add((o.Top, o.Bottom, l < o.Right && o.Left < r));
            var (dt, db) = BestAxisSnap(t, b, top, bottom, ys, workAreas, wa => (wa.Top, wa.Bottom), threshold, gap);

            if (move) return (l + dl, t + dt, r + dl, b + dt);

            int nl = l + dl, nr = r + dr, nt = t + dt, nb = b + db;
            if (nr - nl < MinSnappedSize) { nl = l; nr = r; }
            if (nb - nt < MinSnappedSize) { nt = t; nb = b; }
            return (nl, nt, nr, nb);
        }

        /// <summary>
        /// Finds the smallest snap on one axis. Returns the delta for the low and high edge:
        /// both equal for a move (the whole rect shifts), or each edge's own delta for a resize.
        /// </summary>
        private static (int DLo, int DHi) BestAxisSnap(int lo, int hi, bool snapLo, bool snapHi,
            List<(int Lo, int Hi, bool Neighbour)> others,
            IReadOnlyList<(int Left, int Top, int Right, int Bottom)> workAreas,
            Func<(int Left, int Top, int Right, int Bottom), (int Lo, int Hi)> axis,
            int threshold, int gap)
        {
            int? bestLo = null, bestHi = null;

            void Try(ref int? best, int from, int to)
            {
                int d = to - from;
                if (Math.Abs(d) <= threshold && (best == null || Math.Abs(d) < Math.Abs(best.Value))) best = d;
            }

            foreach (var o in others)
            {
                // Same-side edges align; opposite edges abut, with a gap when side by side.
                int abutGap = o.Neighbour ? gap : 0;
                if (snapLo) { Try(ref bestLo, lo, o.Lo); Try(ref bestLo, lo, o.Hi + abutGap); }
                if (snapHi) { Try(ref bestHi, hi, o.Hi); Try(ref bestHi, hi, o.Lo - abutGap); }
            }
            foreach (var wa in workAreas)
            {
                var (waLo, waHi) = axis(wa);
                if (snapLo) Try(ref bestLo, lo, waLo);
                if (snapHi) Try(ref bestHi, hi, waHi);
            }

            if (snapLo && snapHi)
            {
                // A move: shift by whichever edge is closer to its guide.
                int? d = bestLo == null ? bestHi
                       : bestHi == null ? bestLo
                       : Math.Abs(bestLo.Value) <= Math.Abs(bestHi.Value) ? bestLo : bestHi;
                return (d ?? 0, d ?? 0);
            }
            return (bestLo ?? 0, bestHi ?? 0);
        }
    }
}
