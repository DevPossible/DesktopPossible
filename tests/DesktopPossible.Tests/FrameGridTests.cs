using System;
using System.Collections.Generic;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the frame grid (FrameGrid): unit metrics from icon settings,
/// size snapping to chrome + whole rows/columns, position snapping to grid points,
/// and the App-Categorize placement gap / grid alignment built on top of it.
/// </summary>
public class FrameGridTests
{
    // ---------------------------------------------------------------
    // Unit metrics
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(5, 80)]   // default: 60 + 4*5
    [InlineData(0, 60)]
    [InlineData(10, 100)]
    [InlineData(-3, 60)]  // negative spacing is clamped to 0
    public void UnitWidthFor_IsIconPanelPlusFourSpacings(int spacing, double expected)
    {
        FrameGrid.UnitWidthFor(spacing).ShouldBe(expected);
    }

    [Theory]
    [InlineData(32, 5, 70)]   // Medium: 32 + 2*14 + 2*5
    [InlineData(16, 5, 54)]   // Tiny
    [InlineData(64, 10, 112)] // Huge, wide spacing
    public void UnitHeightFor_IsIconPlusLabelLinesPlusSpacing(int iconPx, int spacing, double expected)
    {
        FrameGrid.UnitHeightFor(iconPx, spacing).ShouldBe(expected);
    }

    [Fact]
    public void DefaultUnits_MatchDefaultFrameSettings()
    {
        FrameGrid.UnitWidth.ShouldBe(80);
        FrameGrid.UnitHeight.ShouldBe(70);
        FrameGrid.ChromeWidth.ShouldBe(30);
        FrameGrid.ChromeHeight.ShouldBe(32);
    }

    // ---------------------------------------------------------------
    // SnapSize
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(350, 102, 350, 102)]   // already on the grid: 30+4*80, 32+1*70
    [InlineData(360, 120, 350, 102)]   // rounds down to nearest
    [InlineData(400, 140, 430, 172)]   // rounds up: 4.6 cols -> 5, 1.54 rows -> 2
    [InlineData(230, 130, 190, 102)]   // CreateNewFrame defaults: 2.5 cols -> 2 (round-to-even), 1.4 rows -> 1
    public void SnapSize_RoundsToChromePlusWholeUnits(double w, double h, double expectedW, double expectedH)
    {
        var (sw, sh) = FrameGrid.SnapSize(w, h);
        ((sw - FrameGrid.ChromeWidth) % FrameGrid.UnitWidth).ShouldBe(0);
        ((sh - FrameGrid.ChromeHeight) % FrameGrid.UnitHeight).ShouldBe(0);
        sw.ShouldBe(expectedW);
        sh.ShouldBe(expectedH);
    }

    [Fact]
    public void SnapSize_NeverGoesBelowOneByOne()
    {
        var (w, h) = FrameGrid.SnapSize(10, 10);
        w.ShouldBe(FrameGrid.ChromeWidth + FrameGrid.UnitWidth);
        h.ShouldBe(FrameGrid.ChromeHeight + FrameGrid.UnitHeight);

        (w, h) = FrameGrid.SnapSize(-500, 0);
        w.ShouldBe(FrameGrid.ChromeWidth + FrameGrid.UnitWidth);
        h.ShouldBe(FrameGrid.ChromeHeight + FrameGrid.UnitHeight);
    }

    [Fact]
    public void SnapSize_WithExplicitUnits_UsesThem()
    {
        var (w, h) = FrameGrid.SnapSize(330, 232, unitWidth: 100, unitHeight: 50);
        w.ShouldBe(FrameGrid.ChromeWidth + 3 * 100);
        h.ShouldBe(FrameGrid.ChromeHeight + 4 * 50);
    }

    // ---------------------------------------------------------------
    // SnapPosition
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(79, 34, 80, 0)]      // nearest in each axis independently
    [InlineData(121, 106, 160, 140)] // 1.51 -> 2 cols, 1.51 -> 2 rows
    [InlineData(119, 104, 80, 70)]   // 1.49 -> 1, 1.49 -> 1
    public void SnapPosition_NearestGridPointFromOrigin(double x, double y, double ex, double ey)
    {
        var (sx, sy) = FrameGrid.SnapPosition(x, y, 0, 0);
        sx.ShouldBe(ex);
        sy.ShouldBe(ey);
    }

    [Fact]
    public void SnapPosition_RespectsOffsetOrigin()
    {
        // Taskbar on the left/top: work area starts at (64, 40).
        var (sx, sy) = FrameGrid.SnapPosition(150, 120, 64, 40);
        sx.ShouldBe(64 + 80);   // (150-64)/80 = 1.075 -> 1
        sy.ShouldBe(40 + 70);   // (120-40)/70 = 1.14 -> 1

        // Exactly on the origin stays put.
        FrameGrid.SnapPosition(64, 40, 64, 40).ShouldBe((64, 40));
    }

    [Fact]
    public void SnapPosition_HandlesNegativeCoordinates()
    {
        // Secondary monitor to the left of the primary: negative X, origin at -1920.
        var (sx, sy) = FrameGrid.SnapPosition(-1800, -30, -1920, 0);
        sx.ShouldBe(-1920 + 2 * 80);  // 120/80 = 1.5 -> 2 (MidpointRounding.ToEven: 2)
        sy.ShouldBe(0);               // -30/70 = -0.43 -> 0

        // Negative relative to a zero origin snaps to negative multiples.
        var (nx, ny) = FrameGrid.SnapPosition(-90, -140, 0, 0);
        nx.ShouldBe(-80);
        ny.ShouldBe(-140);
    }

    [Fact]
    public void SnapPosition_WithExplicitUnits_UsesThem()
    {
        var (sx, sy) = FrameGrid.SnapPosition(105, 26, 0, 0, unitWidth: 100, unitHeight: 50);
        sx.ShouldBe(100);
        sy.ShouldBe(50);
    }

    // ---------------------------------------------------------------
    // App-Categorize placement: gap + grid alignment
    // ---------------------------------------------------------------

    private static readonly (double, double, double, double) WorkArea = (0, 0, 1920, 1040);

    [Fact]
    public void FindFreePosition_RejectsCandidateAdjacentToOccupiedRect()
    {
        // An occupied frame spanning x 0..300 on the top band. With a 16px gap an
        // adjacent candidate at x=300 must be rejected; the first acceptable X is >= 316.
        var occupied = new List<(double X, double Y, double W, double H)> { (0, 0, 300, 200) };

        var pos = AppCategorizer.FindFreePosition(200, 200, occupied, WorkArea, step: 1, gap: 16);

        pos.ShouldNotBeNull();
        pos!.Value.Y.ShouldBe(0);
        pos.Value.X.ShouldBeGreaterThanOrEqualTo(316);

        // Without a gap the adjacent cell is fine.
        var tight = AppCategorizer.FindFreePosition(200, 200, occupied, WorkArea, step: 1, gap: 0);
        tight!.Value.X.ShouldBe(300);
    }

    [Fact]
    public void FindFreePosition_GapAppliesVertically()
    {
        var occupied = new List<(double X, double Y, double W, double H)> { (0, 0, 1920, 300) };

        var pos = AppCategorizer.FindFreePosition(300, 200, occupied, WorkArea, step: 1, gap: 16);

        pos.ShouldNotBeNull();
        pos!.Value.Y.ShouldBe(316);
    }

    [Fact]
    public void FindFreePosition_CandidatesLandOnGridMultiples()
    {
        // A frame occupies the top-left; placements walk the (80, 70) grid from the
        // work-area origin, so the result is on a grid point AND clear of the gap.
        var occupied = new List<(double X, double Y, double W, double H)> { (0, 0, 350, 172) };

        var pos = AppCategorizer.FindFreePosition(350, 172, occupied, WorkArea,
            gap: FrameGrid.UnitWidth, gridW: FrameGrid.UnitWidth, gridH: FrameGrid.UnitHeight);

        pos.ShouldNotBeNull();
        (pos!.Value.X % FrameGrid.UnitWidth).ShouldBe(0);
        (pos.Value.Y % FrameGrid.UnitHeight).ShouldBe(0);
        // 350 + 80 gap = 430 -> next grid column at 480.
        pos.Value.X.ShouldBe(480);
        pos.Value.Y.ShouldBe(0);
    }

    [Fact]
    public void FindFreePosition_GridAlignsToOffsetWorkAreaOrigin()
    {
        var pos = AppCategorizer.FindFreePosition(300, 200,
            new List<(double, double, double, double)> { (64, 40, 300, 200) },
            (64, 40, 1856, 1000),
            gap: FrameGrid.UnitWidth, gridW: FrameGrid.UnitWidth, gridH: FrameGrid.UnitHeight);

        pos.ShouldNotBeNull();
        ((pos!.Value.X - 64) % FrameGrid.UnitWidth).ShouldBe(0);
        ((pos.Value.Y - 40) % FrameGrid.UnitHeight).ShouldBe(0);
        pos.Value.X.ShouldBeGreaterThanOrEqualTo(64 + 300 + FrameGrid.UnitWidth);
    }

    [Fact]
    public void ComputeFrameSize_AlwaysChromePlusWholeUnits()
    {
        foreach (int count in new[] { 0, 1, 4, 5, 13, 200 })
        {
            var (w, h) = AppCategorizer.ComputeFrameSize(count, 1000);
            ((w - FrameGrid.ChromeWidth) % FrameGrid.UnitWidth).ShouldBe(0);
            ((h - FrameGrid.ChromeHeight) % FrameGrid.UnitHeight).ShouldBe(0);
            h.ShouldBeGreaterThanOrEqualTo(FrameGrid.ChromeHeight + FrameGrid.UnitHeight);
        }
    }
}
