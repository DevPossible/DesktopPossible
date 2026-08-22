using System.Linq;
using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

public class SendToProfileTests
{
    private static readonly (double X, double Y, double W, double H) WorkArea = (0, 0, 1920, 1040);

    [Fact]
    public void ChoosePosition_KeepsOriginalSpot_WhenFreeInTarget()
    {
        var frame = (X: 400.0, Y: 300.0, W: 230.0, H: 130.0);
        var occupied = new[] { (0.0, 0.0, 230.0, 130.0) };

        SendToProfileManager.ChoosePosition(frame, occupied, WorkArea).ShouldBe((400.0, 300.0));
    }

    [Fact]
    public void ChoosePosition_KeepsOriginalSpot_WhenTargetIsEmpty()
    {
        var frame = (X: 1200.0, Y: 700.0, W: 230.0, H: 130.0);

        SendToProfileManager.ChoosePosition(frame, [], WorkArea).ShouldBe((1200.0, 700.0));
    }

    [Fact]
    public void ChoosePosition_Relocates_WhenOriginalSpotOverlapsATargetFrame()
    {
        var frame = (X: 100.0, Y: 100.0, W: 230.0, H: 130.0);
        var occupied = new[] { (150.0, 150.0, 300.0, 200.0) };

        var (x, y) = SendToProfileManager.ChoosePosition(frame, occupied, WorkArea);

        (x, y).ShouldNotBe((100.0, 100.0));
        // The new rect must not touch the occupied one.
        bool overlaps = x < 150 + 300 && 150 < x + 230 && y < 150 + 200 && 150 < y + 130;
        overlaps.ShouldBeFalse();
        x.ShouldBeGreaterThanOrEqualTo(WorkArea.X);
        y.ShouldBeGreaterThanOrEqualTo(WorkArea.Y);
    }

    [Fact]
    public void ChoosePosition_Relocates_WhenOriginalSpotIsOutsideTheWorkArea()
    {
        var frame = (X: 1800.0, Y: 1000.0, W: 230.0, H: 130.0); // hangs off the bottom-right

        var (x, y) = SendToProfileManager.ChoosePosition(frame, [], WorkArea);

        (x + 230).ShouldBeLessThanOrEqualTo(WorkArea.W);
        (y + 130).ShouldBeLessThanOrEqualTo(WorkArea.H);
    }

    [Fact]
    public void ChoosePosition_Cascades_WhenNothingFits()
    {
        var frame = (X: 0.0, Y: 0.0, W: 500.0, H: 500.0);
        var tiny = (X: 0.0, Y: 0.0, W: 600.0, H: 600.0);
        var occupied = new[] { (0.0, 0.0, 600.0, 600.0) };

        var (x, y) = SendToProfileManager.ChoosePosition(frame, occupied, tiny);

        x.ShouldBe(70);
        y.ShouldBe(70);
    }

    [Fact]
    public void AllItemArrays_YieldsMainListAndEveryTabList()
    {
        var record = JObject.Parse("""
            {
              "Items": [ { "Filename": "a" } ],
              "Tabs": [
                { "Name": "t1", "Items": [ { "Filename": "b" }, { "Filename": "c" } ] },
                { "Name": "t2", "Items": [] }
              ]
            }
            """);

        var arrays = SendToProfileManager.AllItemArrays(record).ToList();

        arrays.Count.ShouldBe(3);
        arrays.Sum(a => a.Count).ShouldBe(3);
    }

    [Fact]
    public void AllItemArrays_IsEmpty_ForPortalFrames()
    {
        var record = JObject.Parse("""{ "Items": "C:\\Some\\Folder" }""");

        SendToProfileManager.AllItemArrays(record).ShouldBeEmpty();
    }
}
