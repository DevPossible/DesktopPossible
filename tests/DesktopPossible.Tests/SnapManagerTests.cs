using System.Collections.Generic;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for guide-line snapping (SnapManager.SnapToGuides): frame edges stick to
/// other frames' edges and work-area edges within the threshold, neighbours keep a gap, a
/// move keeps the size, and a resize only moves the pulled edges.
/// </summary>
public class SnapManagerTests
{
    private const int Threshold = 10;
    private const int Gap = 10;

    private static readonly List<(int, int, int, int)> NoWorkAreas = new();
    private static readonly List<(int, int, int, int)> WorkArea = new() { (0, 0, 1920, 1040) };

    // A frame far from the screen edges that the tests snap against.
    private static readonly (int, int, int, int) Other = (500, 300, 800, 600);

    private static (int Left, int Top, int Right, int Bottom) Snap((int, int, int, int) rect, int edge,
        params (int, int, int, int)[] others) =>
        SnapManager.SnapToGuides(rect, edge, others, NoWorkAreas, Threshold, Gap);

    // ---------------------------------------------------------------
    // Move
    // ---------------------------------------------------------------

    [Fact]
    public void Move_NoGuideWithinThreshold_LeavesRectUnchanged()
    {
        Snap((100, 100, 200, 200), FrameGrid.GestureMove, Other).ShouldBe((100, 100, 200, 200));
    }

    [Fact]
    public void Move_AlignsLeftEdgeWithOtherFramesLeftEdge()
    {
        // Far above the other frame (no vertical overlap), left edge 6px off its left guide.
        Snap((506, 0, 606, 100), FrameGrid.GestureMove, Other).ShouldBe((500, 0, 600, 100));
    }

    [Fact]
    public void Move_AlignsTopEdgeWithOtherFramesTop_EvenFarAway()
    {
        // Guides are infinite lines: alignment works however far apart the frames are.
        Snap((1500, 295, 1600, 395), FrameGrid.GestureMove, Other).ShouldBe((1500, 300, 1600, 400));
    }

    [Fact]
    public void Move_KeepsSize()
    {
        var (l, t, r, b) = Snap((505, 297, 655, 497), FrameGrid.GestureMove, Other);
        (r - l).ShouldBe(150);
        (b - t).ShouldBe(200);
    }

    [Fact]
    public void Move_SideBySideNeighbour_SnapsWithGap()
    {
        // Overlaps the other frame vertically and sits just left of it: right edge goes to
        // other.Left - gap, not flush against it.
        Snap((285, 350, 485, 450), FrameGrid.GestureMove, Other).ShouldBe((290, 350, 490, 450));
    }

    [Fact]
    public void Move_NonNeighbour_OppositeEdgeAlignsFlush()
    {
        // No vertical overlap: the other frame's left line is a plain alignment guide.
        Snap((303, 0, 497, 100), FrameGrid.GestureMove, Other).ShouldBe((306, 0, 500, 100));
    }

    [Fact]
    public void Move_StackedBelowNeighbour_SnapsWithGap()
    {
        // Overlaps horizontally, just below: top goes to other.Bottom + gap.
        Snap((550, 615, 700, 715), FrameGrid.GestureMove, Other).ShouldBe((550, 610, 700, 710));
    }

    [Fact]
    public void Move_PicksTheClosestGuide()
    {
        var a = (500, 300, 800, 600);
        var b = (1003, 0, 1100, 50);
        // Left edge is 4 from a's left guide; right edge is 2 from b's left guide: the 2 wins.
        Snap((504, 1000, 1001, 1020), FrameGrid.GestureMove, a, b).ShouldBe((506, 1000, 1003, 1020));
    }

    [Fact]
    public void Move_SnapsToWorkAreaEdges()
    {
        SnapManager.SnapToGuides((7, 1034, 107, 1134), FrameGrid.GestureMove,
                new List<(int, int, int, int)>(), WorkArea, Threshold, Gap)
            .ShouldBe((0, 1034, 100, 1134)); // bottom 1134 is 94 past the work area: no Y snap

        SnapManager.SnapToGuides((1815, 935, 1915, 1035), FrameGrid.GestureMove,
                new List<(int, int, int, int)>(), WorkArea, Threshold, Gap)
            .ShouldBe((1820, 940, 1920, 1040));
    }

    // ---------------------------------------------------------------
    // Resize
    // ---------------------------------------------------------------

    [Fact]
    public void Resize_Right_MovesOnlyRightEdge()
    {
        // Pulling the right edge near the other frame's right guide (no vertical overlap).
        Snap((100, 0, 795, 100), FrameGrid.GestureRight, Other).ShouldBe((100, 0, 800, 100));
    }

    [Fact]
    public void Resize_Right_IgnoresGuidesForTheLeftEdge()
    {
        // The left edge (503) is near a guide, but only the pulled right edge may snap.
        Snap((503, 0, 700, 100), FrameGrid.GestureRight, Other).ShouldBe((503, 0, 700, 100));
    }

    [Fact]
    public void Resize_BottomLeft_SnapsBothPulledEdges()
    {
        Snap((496, 0, 1200, 596), FrameGrid.GestureBottomLeft, Other).ShouldBe((500, 0, 1200, 600));
    }

    [Fact]
    public void Resize_NeverSnapsBelowMinimumSize()
    {
        // Snapping the right edge to 500 would leave a 5px-wide frame: keep the raw edge.
        Snap((495, 0, 508, 100), FrameGrid.GestureRight, Other).ShouldBe((495, 0, 508, 100));
    }
}
