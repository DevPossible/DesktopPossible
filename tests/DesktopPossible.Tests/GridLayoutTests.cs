using System.Collections.Generic;
using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the free-arrange grid math (GridLayout).
/// Cells are logical (col, row) pairs; pixel mapping is owned by the UI layer.
/// </summary>
public class GridLayoutTests
{
    private static HashSet<(int Col, int Row)> Cells(params (int, int)[] cells) => new(cells);

    #region ColumnsForWidth

    [Theory]
    [InlineData(240, 80, 3)]
    [InlineData(239.9, 80, 2)]
    [InlineData(80, 80, 1)]
    [InlineData(400, 100, 4)]
    public void ColumnsForWidth_FloorsAvailableWidthOverCellWidth(double width, double cell, int expected)
    {
        GridLayout.ColumnsForWidth(width, cell).ShouldBe(expected);
    }

    [Theory]
    [InlineData(50, 80)]    // narrower than one cell
    [InlineData(0, 80)]     // zero width
    [InlineData(-10, 80)]   // negative width
    [InlineData(240, 0)]    // invalid cell width
    [InlineData(240, -5)]   // negative cell width
    [InlineData(double.PositiveInfinity, 80)] // unconstrained measure
    [InlineData(double.NaN, 80)]
    public void ColumnsForWidth_NeverReturnsLessThanOneColumn(double width, double cell)
    {
        GridLayout.ColumnsForWidth(width, cell).ShouldBe(1);
    }

    #endregion

    #region ClampColumn

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(5, 3, 2)]   // out-of-range column clamps to last visible column
    [InlineData(-1, 3, 0)]  // negative clamps to 0
    [InlineData(4, 0, 0)]   // degenerate column count treated as 1
    public void ClampColumn_ClampsIntoVisibleRange(int col, int columns, int expected)
    {
        GridLayout.ClampColumn(col, columns).ShouldBe(expected);
    }

    #endregion

    #region CellFromPoint

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(79.9, 99.9, 0, 0)]
    [InlineData(80, 100, 1, 1)]
    [InlineData(165, 210, 2, 2)]
    public void CellFromPoint_MapsPointToCell(double x, double y, int expCol, int expRow)
    {
        GridLayout.CellFromPoint(x, y, 80, 100, 4).ShouldBe((expCol, expRow));
    }

    [Fact]
    public void CellFromPoint_ClampsColumnToVisibleRange()
    {
        // x lands in column 5 but only 3 columns are visible
        GridLayout.CellFromPoint(450, 0, 80, 100, 3).ShouldBe((2, 0));
    }

    [Fact]
    public void CellFromPoint_NegativeCoordinatesClampToZero()
    {
        GridLayout.CellFromPoint(-30, -200, 80, 100, 3).ShouldBe((0, 0));
    }

    #endregion

    #region FirstFreeCell

    [Fact]
    public void FirstFreeCell_EmptyGrid_ReturnsOrigin()
    {
        GridLayout.FirstFreeCell(Cells(), 3).ShouldBe((0, 0));
    }

    [Fact]
    public void FirstFreeCell_NullOccupiedSet_ReturnsOrigin()
    {
        GridLayout.FirstFreeCell(null, 3).ShouldBe((0, 0));
    }

    [Fact]
    public void FirstFreeCell_ScansRowMajorAndFillsGaps()
    {
        // Row 0: (0,0) and (2,0) taken -> gap at (1,0) wins over anything on row 1.
        GridLayout.FirstFreeCell(Cells((0, 0), (2, 0), (0, 1)), 3).ShouldBe((1, 0));
    }

    [Fact]
    public void FirstFreeCell_FullFirstRow_MovesToNextRow()
    {
        GridLayout.FirstFreeCell(Cells((0, 0), (1, 0), (2, 0)), 3).ShouldBe((0, 1));
    }

    [Fact]
    public void FirstFreeCell_OccupiedCellsBeyondVisibleColumnsDoNotBlockScan()
    {
        // (5,0) is outside a 3-column grid — the scan never visits it.
        GridLayout.FirstFreeCell(Cells((0, 0), (5, 0)), 3).ShouldBe((1, 0));
    }

    [Fact]
    public void FirstFreeCell_SingleColumnGrid_StacksDownward()
    {
        GridLayout.FirstFreeCell(Cells((0, 0), (0, 1)), 1).ShouldBe((0, 2));
    }

    #endregion

    #region PreviewDisplacement (live drag preview / drop commit layout)

    private static List<(string Key, int Col, int Row)> Others(params (string, int, int)[] items) => new(items);

    [Fact]
    public void PreviewDisplacement_HoverOverEmptyCell_NoDisplacement()
    {
        var others = Others(("a", 0, 0), ("b", 1, 0));

        var map = GridLayout.PreviewDisplacement(others, "drag", (2, 0), 3);

        map["drag"].ShouldBe((2, 0));
        map["a"].ShouldBe((0, 0));
        map["b"].ShouldBe((1, 0));
    }

    [Fact]
    public void PreviewDisplacement_HoverOverOccupiedCell_ChainPushesRowMajor()
    {
        // a,b,c fill row 0; hovering a's cell pushes the whole chain one slot forward.
        var others = Others(("a", 0, 0), ("b", 1, 0), ("c", 2, 0));

        var map = GridLayout.PreviewDisplacement(others, "drag", (0, 0), 3);

        map["drag"].ShouldBe((0, 0));
        map["a"].ShouldBe((1, 0));
        map["b"].ShouldBe((2, 0));
        map["c"].ShouldBe((0, 1)); // pushed past the row end -> wraps to next row
    }

    [Fact]
    public void PreviewDisplacement_ChainStopsAtFirstGap()
    {
        // Gap at (1,0): only "a" is displaced into it; "c" is untouched.
        var others = Others(("a", 0, 0), ("c", 2, 0));

        var map = GridLayout.PreviewDisplacement(others, "drag", (0, 0), 3);

        map["drag"].ShouldBe((0, 0));
        map["a"].ShouldBe((1, 0));
        map["c"].ShouldBe((2, 0));
    }

    [Fact]
    public void PreviewDisplacement_DisplacedItemFlowsIntoDraggedItemsVacatedOrigin()
    {
        // Dragged item came from (1,0) — not in `others`, so its origin is free.
        // Hovering (0,0) pushes "b" forward into that vacated origin (a swap).
        var others = Others(("b", 0, 0), ("c", 2, 0));

        var map = GridLayout.PreviewDisplacement(others, "drag", (0, 0), 3);

        map["drag"].ShouldBe((0, 0));
        map["b"].ShouldBe((1, 0));
        map["c"].ShouldBe((2, 0));
    }

    [Fact]
    public void PreviewDisplacement_PushPastRowEndWrapsToNextRow()
    {
        var others = Others(("a", 1, 0)); // last column of a 2-column grid

        var map = GridLayout.PreviewDisplacement(others, "drag", (1, 0), 2);

        map["drag"].ShouldBe((1, 0));
        map["a"].ShouldBe((0, 1));
    }

    [Fact]
    public void PreviewDisplacement_FullGrid_GrowsANewRow()
    {
        // 2x2 grid completely full; hovering (0,0) shifts everyone by one slot and
        // the last item spills onto a brand-new row (extent grows).
        var others = Others(("a", 0, 0), ("b", 1, 0), ("c", 0, 1), ("d", 1, 1));

        var map = GridLayout.PreviewDisplacement(others, "drag", (0, 0), 2);

        map["drag"].ShouldBe((0, 0));
        map["a"].ShouldBe((1, 0));
        map["b"].ShouldBe((0, 1));
        map["c"].ShouldBe((1, 1));
        map["d"].ShouldBe((0, 2));
    }

    [Fact]
    public void PreviewDisplacement_HoverCellOutOfRange_IsClampedBeforePlacement()
    {
        var map = GridLayout.PreviewDisplacement(Others(), "drag", (9, -3), 3);

        map["drag"].ShouldBe((2, 0));
    }

    [Fact]
    public void PreviewDisplacement_NoOthers_JustPlacesDraggedItem()
    {
        var map = GridLayout.PreviewDisplacement(null, "drag", (1, 1), 3);

        map.Count.ShouldBe(1);
        map["drag"].ShouldBe((1, 1));
    }

    #endregion

    #region ExtentHeight

    [Theory]
    [InlineData(-1, 100, 0)]   // empty grid -> no extent
    [InlineData(0, 100, 100)]  // one row
    [InlineData(2, 80, 240)]   // (maxRow + 1) * cellHeight
    public void ExtentHeight_IsMaxOccupiedRowPlusOneTimesCellHeight(int maxRow, double cellHeight, double expected)
    {
        GridLayout.ExtentHeight(maxRow, cellHeight).ShouldBe(expected);
    }

    [Fact]
    public void ExtentHeight_InvalidCellHeight_ReturnsZero()
    {
        GridLayout.ExtentHeight(3, 0).ShouldBe(0);
    }

    #endregion

    #region TryGetCell / OccupiedCells

    [Fact]
    public void TryGetCell_ValidCell_ReturnsTrue()
    {
        var item = new JObject { ["GridCol"] = 2, ["GridRow"] = 3 };
        GridLayout.TryGetCell(item, out var cell).ShouldBeTrue();
        cell.ShouldBe((2, 3));
    }

    [Theory]
    [InlineData("{}")]                                  // both keys missing
    [InlineData("{\"GridCol\": 1}")]                    // row missing
    [InlineData("{\"GridCol\": -1, \"GridRow\": 0}")]   // negative col
    [InlineData("{\"GridCol\": 0, \"GridRow\": -2}")]   // negative row
    [InlineData("{\"GridCol\": \"x\", \"GridRow\": 0}")]// non-numeric
    [InlineData("{\"GridCol\": null, \"GridRow\": 0}")] // null token
    public void TryGetCell_MissingOrInvalid_ReturnsFalse(string json)
    {
        GridLayout.TryGetCell(JObject.Parse(json), out _).ShouldBeFalse();
    }

    [Fact]
    public void OccupiedCells_CollectsValidCellsAndSkipsExcludedItem()
    {
        var a = new JObject { ["Filename"] = "a", ["GridCol"] = 0, ["GridRow"] = 0 };
        var b = new JObject { ["Filename"] = "b", ["GridCol"] = 1, ["GridRow"] = 0 };
        var c = new JObject { ["Filename"] = "c" }; // no cell yet
        var items = new JArray(a, b, c);

        var occupied = GridLayout.OccupiedCells(items, exclude: b);

        occupied.ShouldBe(Cells((0, 0)));
    }

    #endregion

    #region PlaceInFirstFreeCell

    [Fact]
    public void PlaceInFirstFreeCell_NewDictionaryItem_GetsFirstFreeCellStamped()
    {
        var items = new JArray(
            new JObject { ["GridCol"] = 0, ["GridRow"] = 0 },
            new JObject { ["GridCol"] = 1, ["GridRow"] = 0 });
        var newItem = new Dictionary<string, object> { ["Filename"] = "new" };

        var cell = GridLayout.PlaceInFirstFreeCell(items, newItem, 3);

        cell.ShouldBe((2, 0));
        newItem["GridCol"].ShouldBe(2);
        newItem["GridRow"].ShouldBe(0);
    }

    [Fact]
    public void PlaceInFirstFreeCell_ExistingJObjectItem_IgnoresItsOwnStaleCell()
    {
        // Pasted/moved item carries a stale cell (0,0) from its source frame; the only
        // other occupant sits at (1,0) — so (0,0) is genuinely free and must be reused.
        var mover = new JObject { ["Filename"] = "m", ["GridCol"] = 0, ["GridRow"] = 0 };
        var other = new JObject { ["Filename"] = "o", ["GridCol"] = 1, ["GridRow"] = 0 };
        var items = new JArray(other, mover);

        var cell = GridLayout.PlaceInFirstFreeCell(items, mover, 3);

        cell.ShouldBe((0, 0));
        ((int)mover["GridCol"]).ShouldBe(0);
        ((int)mover["GridRow"]).ShouldBe(0);
    }

    [Fact]
    public void PlaceInFirstFreeCell_TargetCellTaken_MovesToNextFreeCell()
    {
        var mover = new JObject { ["Filename"] = "m", ["GridCol"] = 0, ["GridRow"] = 0 };
        var blocker = new JObject { ["Filename"] = "b", ["GridCol"] = 0, ["GridRow"] = 0 };
        var items = new JArray(blocker, mover);

        var cell = GridLayout.PlaceInFirstFreeCell(items, mover, 3);

        cell.ShouldBe((1, 0)); // blocker keeps (0,0); mover is re-stamped past it
    }

    #endregion

    #region EnsureCells

    [Fact]
    public void EnsureCells_AllItemsMissingCells_AssignsRowMajorInDisplayOrder()
    {
        var items = new JArray(
            new JObject { ["Filename"] = "b", ["DisplayOrder"] = 1 },
            new JObject { ["Filename"] = "a", ["DisplayOrder"] = 0 },
            new JObject { ["Filename"] = "c", ["DisplayOrder"] = 2 });

        GridLayout.EnsureCells(items, 2).ShouldBeTrue();

        // DisplayOrder 0 -> (0,0), 1 -> (1,0), 2 -> (0,1) with 2 columns.
        var a = (JObject)items[1];
        var b = (JObject)items[0];
        var c = (JObject)items[2];
        ((int)a["GridCol"], (int)a["GridRow"]).ShouldBe((0, 0));
        ((int)b["GridCol"], (int)b["GridRow"]).ShouldBe((1, 0));
        ((int)c["GridCol"], (int)c["GridRow"]).ShouldBe((0, 1));
    }

    [Fact]
    public void EnsureCells_ExistingValidCells_AreKeptUntouched()
    {
        var items = new JArray(
            new JObject { ["Filename"] = "a", ["DisplayOrder"] = 0, ["GridCol"] = 2, ["GridRow"] = 4 });

        GridLayout.EnsureCells(items, 3).ShouldBeFalse();

        ((int)items[0]["GridCol"], (int)items[0]["GridRow"]).ShouldBe((2, 4));
    }

    [Fact]
    public void EnsureCells_DuplicateCells_FirstClaimWinsOthersReassigned()
    {
        var first = new JObject { ["Filename"] = "a", ["DisplayOrder"] = 0, ["GridCol"] = 0, ["GridRow"] = 0 };
        var dupe = new JObject { ["Filename"] = "b", ["DisplayOrder"] = 1, ["GridCol"] = 0, ["GridRow"] = 0 };
        var items = new JArray(first, dupe);

        GridLayout.EnsureCells(items, 3).ShouldBeTrue();

        ((int)first["GridCol"], (int)first["GridRow"]).ShouldBe((0, 0));
        ((int)dupe["GridCol"], (int)dupe["GridRow"]).ShouldBe((1, 0));
    }

    [Fact]
    public void EnsureCells_ItemWithoutCellNeverStealsALaterItemsPersistedCell()
    {
        // "a" (no cell, order 0) must NOT take (0,0), which "b" (order 1) already owns.
        var a = new JObject { ["Filename"] = "a", ["DisplayOrder"] = 0 };
        var b = new JObject { ["Filename"] = "b", ["DisplayOrder"] = 1, ["GridCol"] = 0, ["GridRow"] = 0 };
        var items = new JArray(a, b);

        GridLayout.EnsureCells(items, 3).ShouldBeTrue();

        ((int)b["GridCol"], (int)b["GridRow"]).ShouldBe((0, 0));
        ((int)a["GridCol"], (int)a["GridRow"]).ShouldBe((1, 0));
    }

    [Fact]
    public void EnsureCells_CellsBeyondVisibleColumns_ArePreservedNotReflowed()
    {
        // Frame shrank: column 5 no longer visible. The persisted value must survive
        // (clamping happens at render only, so re-widening restores the layout).
        var wide = new JObject { ["Filename"] = "w", ["DisplayOrder"] = 0, ["GridCol"] = 5, ["GridRow"] = 0 };
        var items = new JArray(wide);

        GridLayout.EnsureCells(items, 2).ShouldBeFalse();

        ((int)wide["GridCol"]).ShouldBe(5);
    }

    [Fact]
    public void EnsureCells_EmptyOrNullList_NoChanges()
    {
        GridLayout.EnsureCells(new JArray(), 3).ShouldBeFalse();
        GridLayout.EnsureCells(null, 3).ShouldBeFalse();
    }

    #endregion
}
