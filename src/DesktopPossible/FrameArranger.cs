using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// "Arrange Frames" (Options > Smart Desktop): sizes every visible frame of the current
    /// profile to its content and lays them all out on the primary work area, left to
    /// right in shelves, in the order they currently read on screen (top-left first).
    /// The layout math is pure and headless-testable; <see cref="ArrangeAllFrames"/>
    /// writes the geometry into the frame data and reloads the frames.
    /// </summary>
    public static class FrameArranger
    {
        /// <summary>Gap kept between neighbouring frames and from the work-area edges.</summary>
        public const double Gap = FrameGrid.FallbackGap;

        private const int MinFlowColumns = 2;
        private const int MaxFlowColumns = 6;

        #region Pure layout math

        /// <summary>
        /// Outer size of a Data frame from its content, in icon units plus chrome.
        /// Free-arrange frames keep their grid shape (max occupied column/row + 1);
        /// flow frames get a roughly square block of ceil(sqrt(n)) columns (2..6).
        /// Height is clamped to ~60% of the work area (the frame scrolls past that).
        /// </summary>
        public static (double Width, double Height) ContentSize(int itemCount, int gridColumns, int gridRows,
            double unitWidth, double unitHeight, double workAreaHeight)
        {
            int cols, rows;
            if (gridColumns > 0 && gridRows > 0)
            {
                cols = gridColumns;
                rows = gridRows;
            }
            else
            {
                cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, itemCount))), MinFlowColumns, MaxFlowColumns);
                rows = Math.Max(1, (int)Math.Ceiling(itemCount / (double)cols));
            }

            int maxRows = Math.Max(1, (int)Math.Floor((workAreaHeight * 0.6 - FrameGrid.ChromeHeight) / unitHeight));
            rows = Math.Min(rows, maxRows);
            return (FrameGrid.ChromeWidth + cols * unitWidth, FrameGrid.ChromeHeight + rows * unitHeight);
        }

        /// <summary>
        /// Shelf-packs frames (in the given order) into the work area: left to right, a new
        /// shelf when a frame would cross the right edge, each shelf as tall as its tallest
        /// frame. Frames that do not fit below the last shelf cascade from the top-left
        /// (overlap allowed only then). A frame wider than the area is placed at the left edge.
        /// </summary>
        public static List<(string Id, double X, double Y)> Pack(
            IReadOnlyList<(string Id, double W, double H)> frames,
            (double X, double Y, double W, double H) workArea, double gap = Gap)
        {
            var result = new List<(string, double, double)>(frames.Count);
            double x = workArea.X + gap, y = workArea.Y + gap, shelfHeight = 0;
            int overflow = 0;

            foreach (var f in frames)
            {
                if (x > workArea.X + gap && x + f.W > workArea.X + workArea.W - gap)
                {
                    x = workArea.X + gap;
                    y += shelfHeight + gap;
                    shelfHeight = 0;
                }

                if (y + f.H > workArea.Y + workArea.H - gap && y > workArea.Y + gap)
                {
                    // Out of vertical room: cascade so every frame stays reachable.
                    result.Add((f.Id, workArea.X + 40 + overflow * 30, workArea.Y + 40 + overflow * 30));
                    overflow++;
                    continue;
                }

                result.Add((f.Id, x, y));
                x += f.W + gap;
                if (f.H > shelfHeight) shelfHeight = f.H;
            }

            return result;
        }

        /// <summary>Items in the main list plus every tab (0 for Portal/Image frames).</summary>
        public static int CountItems(JObject frame)
        {
            int n = frame["Items"] is JArray main ? main.Count : 0;
            if (frame["Tabs"] is JArray tabs)
                foreach (var tab in tabs.OfType<JObject>())
                    if (tab["Items"] is JArray items) n += items.Count;
            return n;
        }

        /// <summary>Occupied grid shape (max col + 1, max row + 1) of a free-arrange frame; (0, 0) when no item has a cell.</summary>
        public static (int Columns, int Rows) GridShape(JObject frame)
        {
            int maxCol = -1, maxRow = -1;
            IEnumerable<JArray> lists = frame["Items"] is JArray main ? new[] { main } : Array.Empty<JArray>();
            if (frame["Tabs"] is JArray tabs)
                lists = lists.Concat(tabs.OfType<JObject>().Select(t => t["Items"]).OfType<JArray>());

            foreach (var item in lists.SelectMany(l => l.OfType<JObject>()))
            {
                if (!GridLayout.TryGetCell(item, out var cell)) continue;
                if (cell.Col > maxCol) maxCol = cell.Col;
                if (cell.Row > maxRow) maxRow = cell.Row;
            }
            return maxCol < 0 ? (0, 0) : (maxCol + 1, maxRow + 1);
        }

        #endregion

        /// <summary>
        /// Sizes and lays out every visible frame of the current profile on the primary
        /// work area, persists, and reloads the frames. Returns the number arranged.
        /// Must run on the UI thread.
        /// </summary>
        public static int ArrangeAllFrames()
        {
            var wa = System.Windows.SystemParameters.WorkArea;
            var workArea = (wa.X, wa.Y, wa.Width, wa.Height);

            var candidates = new List<(dynamic Frame, JObject Record, double X, double Y, double W, double H)>();
            foreach (dynamic frame in FrameDataManager.FrameData)
            {
                JObject record;
                try { record = JObject.FromObject(frame); } catch { continue; }
                string id = record["Id"]?.ToString() ?? "";
                if (id.Length == 0) continue;
                if (string.Equals(record["IsHidden"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)) continue;

                double x = ReadDouble(record["X"], 100), y = ReadDouble(record["Y"], 100);
                double w = ReadDouble(record["Width"], 230), h = ReadDouble(record["Height"], 130);
                bool rolled = string.Equals(record["IsRolled"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

                if (record["ItemsType"]?.ToString() == "Data")
                {
                    int spacing = 5;
                    try { spacing = Convert.ToInt32(record["IconSpacing"]?.ToString() ?? "5"); } catch { }
                    double unitW = FrameGrid.UnitWidthFor(spacing);
                    double unitH = FrameGrid.UnitHeightFor(CoreUtilities.GetIconSizePixels(record["IconSize"]?.ToString() ?? "Medium"), spacing);
                    var shape = Framemanager.IsFreeArrange(frame) ? GridShape(record) : (0, 0);
                    var (cw, ch) = ContentSize(CountItems(record), shape.Item1, shape.Item2, unitW, unitH, wa.Height);
                    w = cw;
                    // A rolled-up frame keeps its collapsed height on screen; the new height waits in UnrolledHeight.
                    if (!rolled) h = ch;
                    SetValue(frame, "UnrolledHeight", ch);
                }

                candidates.Add((frame, record, x, y, w, h));
            }

            if (candidates.Count == 0) return 0;

            // Reading order of the current layout, so related frames stay near each other.
            var ordered = candidates.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            var placed = Pack(ordered.Select(c => (c.Record["Id"]!.ToString(), c.W, c.H)).ToList(), workArea);

            for (int i = 0; i < ordered.Count; i++)
            {
                var c = ordered[i];
                SetValue(c.Frame, "X", placed[i].X);
                SetValue(c.Frame, "Y", placed[i].Y);
                SetValue(c.Frame, "Width", c.W);
                SetValue(c.Frame, "Height", c.H);
            }

            FrameDataManager.SaveFrameData();
            Framemanager.ReloadFrames(silent: true);
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Arrange Frames: laid out {ordered.Count} frame(s) on the primary work area.");
            return ordered.Count;
        }

        private static void SetValue(dynamic frame, string key, double value)
        {
            if (frame is IDictionary<string, object> dict) dict[key] = value;
            else if (frame is JObject jo) jo[key] = value;
        }

        private static double ReadDouble(JToken? token, double fallback)
        {
            if (token == null) return fallback;
            return double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }
    }
}
