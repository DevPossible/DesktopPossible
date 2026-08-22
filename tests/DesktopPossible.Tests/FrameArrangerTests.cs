using System.Collections.Generic;
using System.Linq;
using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

public class FrameArrangerTests
{
    private const double UnitW = 80, UnitH = 70, WorkH = 1040;
    private static readonly (double X, double Y, double W, double H) WorkArea = (0, 0, 1920, 1040);

    // ---------------- ContentSize ----------------

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(9, 3, 3)]
    [InlineData(10, 4, 3)]
    [InlineData(30, 6, 5)]
    public void ContentSize_FlowFrames_GetRoughlySquareBlocks(int items, int expectedCols, int expectedRows)
    {
        var (w, h) = FrameArranger.ContentSize(items, 0, 0, UnitW, UnitH, WorkH);

        w.ShouldBe(FrameGrid.ChromeWidth + expectedCols * UnitW);
        h.ShouldBe(FrameGrid.ChromeHeight + expectedRows * UnitH);
    }

    [Fact]
    public void ContentSize_FreeArrangeFrames_KeepTheirGridShape()
    {
        var (w, h) = FrameArranger.ContentSize(3, 5, 2, UnitW, UnitH, WorkH);

        w.ShouldBe(FrameGrid.ChromeWidth + 5 * UnitW);
        h.ShouldBe(FrameGrid.ChromeHeight + 2 * UnitH);
    }

    [Fact]
    public void ContentSize_ClampsHeightToSixtyPercentOfWorkArea()
    {
        var (_, h) = FrameArranger.ContentSize(600, 0, 0, UnitW, UnitH, WorkH);

        h.ShouldBeLessThanOrEqualTo(WorkH * 0.6);
        ((h - FrameGrid.ChromeHeight) % UnitH).ShouldBe(0); // still whole rows
    }

    // ---------------- Pack ----------------

    [Fact]
    public void Pack_TilesLeftToRightThenWrapsToANewShelf()
    {
        var frames = new List<(string, double, double)>
        {
            ("a", 900, 200), ("b", 900, 300), ("c", 400, 100)
        };

        var placed = FrameArranger.Pack(frames, WorkArea, gap: 16);

        placed[0].ShouldBe(("a", 16.0, 16.0));
        placed[1].ShouldBe(("b", 16 + 900 + 16.0, 16.0));
        // c would cross the right edge (1848 + 400 > 1904) -> next shelf, below the tallest (b)
        placed[2].ShouldBe(("c", 16.0, 16 + 300 + 16.0));
    }

    [Fact]
    public void Pack_NeverOverlapsWhileThereIsRoom()
    {
        var frames = Enumerable.Range(0, 12).Select(i => ($"f{i}", 300.0 + (i % 3) * 50, 150.0 + (i % 4) * 40)).ToList();

        var placed = FrameArranger.Pack(frames, WorkArea);
        var rects = placed.Select(p => (p.X, p.Y, frames.First(f => f.Item1 == p.Id).Item2, frames.First(f => f.Item1 == p.Id).Item3)).ToList();

        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i]; var b = rects[j];
                bool overlap = a.Item1 < b.Item1 + b.Item3 && b.Item1 < a.Item1 + a.Item3
                            && a.Item2 < b.Item2 + b.Item4 && b.Item2 < a.Item2 + a.Item4;
                overlap.ShouldBeFalse($"{placed[i].Id} overlaps {placed[j].Id}");
            }
        rects.All(r => r.Item1 + r.Item3 <= WorkArea.W && r.Item2 + r.Item4 <= WorkArea.H).ShouldBeTrue();
    }

    [Fact]
    public void Pack_CascadesFromTopLeft_WhenVerticalRoomRunsOut()
    {
        var frames = Enumerable.Range(0, 4).Select(i => ($"f{i}", 1800.0, 400.0)).ToList();

        var placed = FrameArranger.Pack(frames, WorkArea, gap: 16);

        placed[0].Y.ShouldBe(16);
        placed[1].Y.ShouldBe(16 + 400 + 16);     // 432 + 400 still fits above 1024
        placed[2].ShouldBe(("f2", 40.0, 40.0));   // no room: cascade
        placed[3].ShouldBe(("f3", 70.0, 70.0));
    }

    // ---------------- Record readers ----------------

    [Fact]
    public void CountItems_SumsMainListAndTabs()
    {
        var frame = JObject.Parse("""{ "Items": [ {}, {} ], "Tabs": [ { "Items": [ {} ] }, { "Items": [ {}, {}, {} ] } ] }""");

        FrameArranger.CountItems(frame).ShouldBe(6);
    }

    [Fact]
    public void CountItems_IsZero_ForPortalFrames()
    {
        FrameArranger.CountItems(JObject.Parse("""{ "Items": "C:\\Folder" }""")).ShouldBe(0);
    }

    [Fact]
    public void GridShape_IsMaxOccupiedCellPlusOne()
    {
        var frame = JObject.Parse("""{ "Items": [ { "GridCol": 0, "GridRow": 0 }, { "GridCol": 4, "GridRow": 1 }, { "NoCell": true } ] }""");

        FrameArranger.GridShape(frame).ShouldBe((5, 2));
    }

    [Fact]
    public void GridShape_IsZero_WhenNoItemHasACell()
    {
        FrameArranger.GridShape(JObject.Parse("""{ "Items": [ {}, {} ] }""")).ShouldBe((0, 0));
    }
}
