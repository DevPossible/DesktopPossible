using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Pure-math grid placement for "Free arrange" Data frames.
    /// Cells are logical (column, row) coordinates persisted per item as GridCol/GridRow;
    /// pixel mapping (cell size) is owned by the UI layer (FreeGridPanel).
    ///
    /// This class is intentionally free of WPF types so it can be unit-tested headless.
    /// It is the single placement authority: every path that adds an item to a
    /// free-arrange frame must route through PlaceInFirstFreeCell / EnsureCells
    /// (see Framemanager.PlaceItemInFreeGrid) so no item can ever land on an occupied cell.
    /// </summary>
    public static class GridLayout
    {
        /// <summary>Per-item JSON key for the persisted grid column.</summary>
        public const string ColKey = "GridCol";

        /// <summary>Per-item JSON key for the persisted grid row.</summary>
        public const string RowKey = "GridRow";

        #region Pure cell math

        /// <summary>
        /// Number of grid columns that fit in the given width. Never less than 1.
        /// </summary>
        public static int ColumnsForWidth(double availableWidth, double cellWidth)
        {
            if (cellWidth <= 0 || double.IsNaN(cellWidth)) return 1;
            if (availableWidth <= 0 || double.IsNaN(availableWidth) || double.IsInfinity(availableWidth)) return 1;
            return Math.Max(1, (int)Math.Floor(availableWidth / cellWidth));
        }

        /// <summary>
        /// Render-only clamp of a persisted column into the currently visible column range.
        /// The persisted value is never changed by this (no reflow on frame resize).
        /// </summary>
        public static int ClampColumn(int col, int columns)
        {
            if (columns < 1) columns = 1;
            return Math.Max(0, Math.Min(col, columns - 1));
        }

        /// <summary>
        /// Maps a point (panel coordinates) to the grid cell under it.
        /// Column is clamped into [0, columns-1]; row is clamped to >= 0.
        /// </summary>
        public static (int Col, int Row) CellFromPoint(double x, double y, double cellWidth, double cellHeight, int columns)
        {
            int col = cellWidth > 0 ? (int)Math.Floor(x / cellWidth) : 0;
            int row = cellHeight > 0 ? (int)Math.Floor(y / cellHeight) : 0;
            return (ClampColumn(col, columns), Math.Max(0, row));
        }

        /// <summary>
        /// First free cell scanning row-major (left-to-right, top-to-bottom).
        /// Occupied cells outside the visible column range never block the scan.
        /// </summary>
        public static (int Col, int Row) FirstFreeCell(ISet<(int Col, int Row)> occupied, int columns)
        {
            if (columns < 1) columns = 1;
            occupied ??= new HashSet<(int, int)>();

            // A free cell always exists at row (maxOccupiedRow + 1) or earlier.
            int maxRow = -1;
            foreach (var cell in occupied)
                if (cell.Row > maxRow) maxRow = cell.Row;

            for (int row = 0; row <= maxRow + 1; row++)
                for (int col = 0; col < columns; col++)
                    if (!occupied.Contains((col, row)))
                        return (col, row);

            return (0, maxRow + 1); // unreachable, kept for safety
        }

        /// <summary>
        /// The full drag-preview layout for a free-arrange grid: the dragged item lands on
        /// the hover cell and any occupant is pushed forward row-major (chain push) to the
        /// next free cell — wrapping past a row end onto the next row and growing the grid
        /// downward when full. The dragged item's origin is vacated, so displaced items may
        /// flow into it. Items whose cells don't conflict keep their exact persisted cell.
        ///
        /// Pure function: input is the current placement (dragged item excluded), output is
        /// the complete key -> cell map (including the dragged key at the hover cell).
        /// The UI renders the map live during the drag; commit = persist the map; cancel =
        /// restore the pre-drag placement.
        /// </summary>
        /// <param name="others">All items except the dragged one, at their current cells.</param>
        /// <param name="draggedKey">Key (Filename) of the dragged item.</param>
        /// <param name="hoverCell">The cell under the cursor.</param>
        /// <param name="columns">Visible column count (min 1).</param>
        public static Dictionary<string, (int Col, int Row)> PreviewDisplacement(
            IEnumerable<(string Key, int Col, int Row)> others,
            string draggedKey,
            (int Col, int Row) hoverCell,
            int columns)
        {
            if (columns < 1) columns = 1;
            int cols = columns;

            long Linear(int col, int row) => (long)row * cols + ClampColumn(col, cols);
            (int Col, int Row) FromLinear(long index) => ((int)(index % cols), (int)(index / cols));

            var result = new Dictionary<string, (int Col, int Row)>();

            var hover = (Col: ClampColumn(hoverCell.Col, cols), Row: Math.Max(0, hoverCell.Row));
            var taken = new HashSet<long> { Linear(hover.Col, hover.Row) };
            if (draggedKey != null) result[draggedKey] = hover;

            var ordered = (others ?? Enumerable.Empty<(string, int, int)>())
                .OrderBy(o => Linear(o.Col, o.Row))
                .ThenBy(o => o.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var item in ordered)
            {
                long index = Linear(item.Col, item.Row);
                if (taken.Add(index))
                {
                    result[item.Key] = (item.Col, item.Row); // no conflict: keep the exact cell
                    continue;
                }

                // Chain push: forward row-major to the next free linear slot.
                long next = index + 1;
                while (taken.Contains(next)) next++;
                taken.Add(next);
                result[item.Key] = FromLinear(next);
            }

            return result;
        }

        /// <summary>
        /// Total pixel height of the grid: (max occupied row + 1) * cell height.
        /// Returns 0 when nothing is occupied (maxOccupiedRow &lt; 0).
        /// </summary>
        public static double ExtentHeight(int maxOccupiedRow, double cellHeight)
        {
            if (maxOccupiedRow < 0 || cellHeight <= 0) return 0;
            return (maxOccupiedRow + 1) * cellHeight;
        }

        #endregion

        #region Item-list (JSON) helpers

        /// <summary>
        /// Reads a valid persisted cell from an item. Returns false when either key is
        /// missing, non-numeric, or negative.
        /// </summary>
        public static bool TryGetCell(JObject item, out (int Col, int Row) cell)
        {
            cell = default;
            if (item == null) return false;

            var colTok = item[ColKey];
            var rowTok = item[RowKey];
            if (colTok == null || rowTok == null) return false;
            if (colTok.Type == JTokenType.Null || rowTok.Type == JTokenType.Null) return false;

            if (!int.TryParse(colTok.ToString(), out int col)) return false;
            if (!int.TryParse(rowTok.ToString(), out int row)) return false;
            if (col < 0 || row < 0) return false;

            cell = (col, row);
            return true;
        }

        /// <summary>
        /// The set of cells currently claimed by items in the list (optionally excluding one item).
        /// </summary>
        public static HashSet<(int Col, int Row)> OccupiedCells(JArray items, JObject exclude = null)
        {
            var occupied = new HashSet<(int, int)>();
            if (items == null) return occupied;

            foreach (var item in items.OfType<JObject>())
            {
                if (exclude != null && ReferenceEquals(item, exclude)) continue;
                if (TryGetCell(item, out var cell)) occupied.Add(cell);
            }
            return occupied;
        }

        /// <summary>
        /// Places an item at EXACTLY the target cell, chain-pushing any occupants
        /// forward row-major (same semantics as the internal drag's displacement
        /// preview). Used for external drops: the item lands where the user pointed —
        /// never in a far-away "first free" cell. The new item may be a JObject
        /// already in the list or a not-yet-added item (JObject or IDictionary);
        /// identity is the "Filename" key.
        /// </summary>
        public static void PlaceWithDisplacement(JArray items, object newItem, (int Col, int Row) target, int columns)
        {
            if (items == null || newItem == null) return;
            if (columns < 1) columns = 1;
            target = (ClampColumn(Math.Max(0, target.Col), columns), Math.Max(0, target.Row));

            string newKey =
                (newItem as JObject)?["Filename"]?.ToString() ??
                (newItem is IDictionary<string, object> d && d.TryGetValue("Filename", out var f) ? f?.ToString() : null) ??
                string.Empty;

            var others = new List<(string Key, int Col, int Row)>();
            foreach (var item in items.OfType<JObject>())
            {
                if (ReferenceEquals(item, newItem)) continue;
                string key = item["Filename"]?.ToString();
                if (key == null || key == newKey) continue;
                if (TryGetCell(item, out var cell)) others.Add((key, cell.Col, cell.Row));
            }

            var map = PreviewDisplacement(others, newKey, target, columns);

            foreach (var item in items.OfType<JObject>())
            {
                if (ReferenceEquals(item, newItem)) continue;
                string key = item["Filename"]?.ToString();
                if (key != null && map.TryGetValue(key, out var cell))
                {
                    item[ColKey] = cell.Col;
                    item[RowKey] = cell.Row;
                }
            }

            if (newItem is JObject jNew)
            {
                jNew[ColKey] = target.Col;
                jNew[RowKey] = target.Row;
            }
            else if (newItem is IDictionary<string, object> dictNew)
            {
                dictNew[ColKey] = target.Col;
                dictNew[RowKey] = target.Row;
            }
        }

        /// <summary>
        /// Tries to stamp an item with a specific cell (clamped to the column count).
        /// Returns false when the cell is occupied by another item — caller falls back
        /// to PlaceInFirstFreeCell. The item may be a JObject already in the list
        /// (its own stale cell never blocks) or a not-yet-added IDictionary.
        /// </summary>
        public static bool TryPlaceAt(JArray items, object item, (int Col, int Row) cell, int columns)
        {
            var jItem = item as JObject;
            var target = (ClampColumn(Math.Max(0, cell.Col), columns), Math.Max(0, cell.Row));
            if (OccupiedCells(items, jItem).Contains(target)) return false;

            if (jItem != null)
            {
                jItem[ColKey] = target.Item1;
                jItem[RowKey] = target.Item2;
                return true;
            }
            if (item is IDictionary<string, object> dict)
            {
                dict[ColKey] = target.Item1;
                dict[RowKey] = target.Item2;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Stamps a NEW item (not yet added to the list) with the first free cell.
        /// </summary>
        public static (int Col, int Row) PlaceInFirstFreeCell(JArray items, IDictionary<string, object> newItem, int columns)
        {
            var cell = FirstFreeCell(OccupiedCells(items), columns);
            if (newItem != null)
            {
                newItem[ColKey] = cell.Col;
                newItem[RowKey] = cell.Row;
            }
            return cell;
        }

        /// <summary>
        /// Stamps an item (already in the list, e.g. pasted or moved in) with the first
        /// free cell. The item's own (possibly stale) cell never counts as occupied.
        /// </summary>
        public static (int Col, int Row) PlaceInFirstFreeCell(JArray items, JObject item, int columns)
        {
            var cell = FirstFreeCell(OccupiedCells(items, item), columns);
            if (item != null)
            {
                item[ColKey] = cell.Col;
                item[RowKey] = cell.Row;
            }
            return cell;
        }

        /// <summary>
        /// Ensures every item in the list has a valid, unique cell.
        /// Existing valid cells are kept (claimed in DisplayOrder, first claim wins);
        /// items with missing, invalid, or duplicate cells get the first free cell
        /// row-major, in DisplayOrder. Returns true when anything changed (caller persists).
        /// </summary>
        public static bool EnsureCells(JArray items, int columns)
        {
            if (items == null) return false;

            var ordered = items.OfType<JObject>()
                .OrderBy(i => { int.TryParse(i["DisplayOrder"]?.ToString(), out int o); return o; })
                .ToList();

            // Pass 1: claim existing valid cells so a later item's persisted cell is never stolen.
            var occupied = new HashSet<(int, int)>();
            var claimed = new HashSet<JObject>();
            foreach (var item in ordered)
            {
                if (TryGetCell(item, out var cell) && occupied.Add(cell))
                    claimed.Add(item);
            }

            // Pass 2: give everything else the first free cell, row-major.
            bool changed = false;
            foreach (var item in ordered)
            {
                if (claimed.Contains(item)) continue;
                var free = FirstFreeCell(occupied, columns);
                item[ColKey] = free.Col;
                item[RowKey] = free.Row;
                occupied.Add(free);
                changed = true;
            }

            return changed;
        }

        #endregion
    }
}
