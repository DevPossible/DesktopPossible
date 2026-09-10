using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the display-configuration layer: fingerprinting a monitor set, matching
/// an old set to a new one, remapping a frame between them, and the layouts.json sidecar.
/// Pure math and temp-file IO only — no registry, COM, network, or app profile data.
/// </summary>
public class DisplayConfigTests : IDisposable
{
    private readonly string _tempDir;

    public DisplayConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfp-displayconfig-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static MonitorInfo Mon(string id, int x, int y, int w, int h,
        double scale = 1.0, bool primary = false, int taskbar = 40)
        => new MonitorInfo
        {
            Id = id,
            Bounds = new PxRect(x, y, w, h),
            WorkArea = new PxRect(x, y, w, h - taskbar),
            Scale = scale,
            Primary = primary
        };

    private static FrameRect Rect(double x, double y, double w = 230, double h = 130, double unrolled = 0)
        => new FrameRect { X = x, Y = y, W = w, H = h, UnrolledHeight = unrolled };

    /// <summary>Laptop screen only: 1920x1080 at 100%.</summary>
    private static List<MonitorInfo> Laptop()
        => new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1920, 1080, 1.0, primary: true) };

    /// <summary>Docked: laptop on the left, a 2560x1440 external on the right.</summary>
    private static List<MonitorInfo> Docked()
        => new List<MonitorInfo>
        {
            Mon("MONITOR\\LEN1234", 0, 0, 1920, 1080, 1.0, primary: true),
            Mon("MONITOR\\DEL4099", 1920, 0, 2560, 1440)
        };

    // ---------------------------------------------------------------
    // Fingerprint
    // ---------------------------------------------------------------

    [Fact]
    public void Fingerprint_IsIndependentOfEnumerationOrder()
    {
        var forward = Docked();
        var reversed = Enumerable.Reverse(Docked()).ToList();

        DisplayConfig.Fingerprint(forward).ShouldBe(DisplayConfig.Fingerprint(reversed));
    }

    [Fact]
    public void Fingerprint_IgnoresWorkAreaChanges()
    {
        // The taskbar auto-hiding, moving, or changing height must NOT mint a new display
        // configuration — otherwise the whole desktop reflows over a few pixels of edge.
        var withTaskbar = Laptop();
        var taskbarHidden = Laptop();
        taskbarHidden[0].WorkArea = new PxRect(0, 0, 1920, 1080);

        DisplayConfig.Fingerprint(withTaskbar).ShouldBe(DisplayConfig.Fingerprint(taskbarHidden));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAMonitorIsAdded()
    {
        DisplayConfig.Fingerprint(Laptop()).ShouldNotBe(DisplayConfig.Fingerprint(Docked()));
    }

    [Fact]
    public void Fingerprint_ChangesWithResolutionScaleIdentityAndPrimary()
    {
        string baseline = DisplayConfig.Fingerprint(Laptop());

        var resized = Laptop();
        resized[0].Bounds = new PxRect(0, 0, 1280, 720);
        DisplayConfig.Fingerprint(resized).ShouldNotBe(baseline);

        var rescaled = Laptop();
        rescaled[0].Scale = 1.5;
        DisplayConfig.Fingerprint(rescaled).ShouldNotBe(baseline);

        var swapped = Laptop();
        swapped[0].Id = "MONITOR\\DEL4099";
        DisplayConfig.Fingerprint(swapped).ShouldNotBe(baseline);

        var demoted = Laptop();
        demoted[0].Primary = false;
        DisplayConfig.Fingerprint(demoted).ShouldNotBe(baseline);
    }

    [Fact]
    public void Fingerprint_OfNothingIsEmpty()
    {
        DisplayConfig.Fingerprint(null).ShouldBe("");
        DisplayConfig.Fingerprint(new List<MonitorInfo>()).ShouldBe("");
    }

    [Fact]
    public void Fingerprint_IsStableAcrossCalls()
    {
        DisplayConfig.Fingerprint(Docked()).ShouldBe(DisplayConfig.Fingerprint(Docked()));
        DisplayConfig.Fingerprint(Docked()).Length.ShouldBe(16);
    }

    // ---------------------------------------------------------------
    // MonitorForRect
    // ---------------------------------------------------------------

    [Fact]
    public void MonitorForRect_PicksTheMonitorTheFrameOverlapsMost()
    {
        var monitors = Docked();

        DisplayConfig.MonitorForRect(Rect(100, 100), monitors).ShouldBe(0);
        DisplayConfig.MonitorForRect(Rect(2200, 300), monitors).ShouldBe(1);
    }

    [Fact]
    public void MonitorForRect_FallsBackToTheNearestWhenFullyOffScreen()
    {
        var monitors = Docked();

        // Way off to the right of everything: the external is nearer than the laptop.
        DisplayConfig.MonitorForRect(Rect(9000, 400), monitors).ShouldBe(1);
    }

    [Fact]
    public void MonitorForRect_ReturnsMinusOneForNoMonitors()
    {
        DisplayConfig.MonitorForRect(Rect(0, 0), new List<MonitorInfo>()).ShouldBe(-1);
    }

    // ---------------------------------------------------------------
    // MatchMonitors
    // ---------------------------------------------------------------

    [Fact]
    public void MatchMonitors_FollowsHardwareIdentityWhenMonitorsSwapPlaces()
    {
        var before = Docked();
        var after = new List<MonitorInfo>
        {
            Mon("MONITOR\\DEL4099", 0, 0, 2560, 1440),
            Mon("MONITOR\\LEN1234", 2560, 0, 1920, 1080, 1.0, primary: true)
        };

        var map = DisplayConfig.MatchMonitors(before, after);

        map[0].ShouldBe(1); // the laptop panel, now on the right
        map[1].ShouldBe(0); // the Dell, now on the left
    }

    [Fact]
    public void MatchMonitors_FallsBackToPositionWhenIdentityIsUnavailable()
    {
        var before = new List<MonitorInfo>
        {
            Mon("", 0, 0, 1920, 1080, 1.0, primary: true),
            Mon("", 1920, 0, 1920, 1080)
        };
        var after = new List<MonitorInfo>
        {
            Mon("", 0, 0, 2560, 1440, 1.0, primary: true),
            Mon("", 2560, 0, 2560, 1440)
        };

        var map = DisplayConfig.MatchMonitors(before, after);

        map[0].ShouldBe(0);
        map[1].ShouldBe(1);
    }

    [Fact]
    public void MatchMonitors_SendsAVanishedMonitorToThePrimary()
    {
        var map = DisplayConfig.MatchMonitors(Docked(), Laptop());

        map[0].ShouldBe(0); // the laptop panel is still here
        map[1].ShouldBe(0); // the external is gone: its frames land on the primary
    }

    [Fact]
    public void MatchMonitors_NeverAssignsTwoOldMonitorsToOneNewOneWhileAnotherIsFree()
    {
        var map = DisplayConfig.MatchMonitors(Docked(), Docked());

        map.ShouldBe(new[] { 0, 1 });
    }

    // ---------------------------------------------------------------
    // Remap — the identity case
    // ---------------------------------------------------------------

    [Fact]
    public void Remap_ToTheSameConfigurationChangesNothing()
    {
        var monitors = Docked();

        foreach (var original in new[] { Rect(20, 20), Rect(1500, 800), Rect(2100, 300), Rect(4000, 1200) })
        {
            var remapped = DisplayConfig.Remap(original, monitors, monitors);

            remapped.X.ShouldBe(original.X);
            remapped.Y.ShouldBe(original.Y);
            remapped.W.ShouldBe(original.W);
            remapped.H.ShouldBe(original.H);
        }
    }

    // ---------------------------------------------------------------
    // Remap — edge anchoring
    // ---------------------------------------------------------------

    [Fact]
    public void Remap_KeepsAFrameFlushWithTheLeftAndTopEdges()
    {
        var from = Laptop();
        var to = new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1280, 720, 1.0, primary: true) };

        var remapped = DisplayConfig.Remap(Rect(0, 0), from, to);

        remapped.X.ShouldBe(0);
        remapped.Y.ShouldBe(0);
    }

    [Fact]
    public void Remap_KeepsAFrameFlushWithTheRightAndBottomEdges()
    {
        var from = Laptop();                         // work area 1920 x 1040
        var to = new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1280, 720, 1.0, primary: true) };
        var (nx, ny, nw, nh) = DisplayConfig.WorkAreaDiu(to[0]);

        // Pinned to the bottom-right corner of the old work area.
        var remapped = DisplayConfig.Remap(Rect(1920 - 230, 1040 - 130), from, to);

        remapped.X.ShouldBe(nx + nw - remapped.W);
        remapped.Y.ShouldBe(ny + nh - remapped.H);
    }

    [Fact]
    public void Remap_KeepsACentredFrameCentred()
    {
        var from = Laptop();
        var to = new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1280, 720, 1.0, primary: true) };
        var (nx, _, nw, _) = DisplayConfig.WorkAreaDiu(to[0]);

        var remapped = DisplayConfig.Remap(Rect((1920 - 230) / 2.0, 100), from, to);

        remapped.X.ShouldBe(Math.Round(nx + (nw - 230) / 2.0), 1.0);
    }

    // ---------------------------------------------------------------
    // Remap — bounds, sizing and DPI
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1690, 910)]
    [InlineData(900, 500)]
    [InlineData(4000, 1300)]     // was on the external monitor
    [InlineData(-500, -200)]     // already off-screen before the change
    public void Remap_AlwaysLandsInsideTheNewWorkArea(double x, double y)
    {
        var to = Laptop();
        var remapped = DisplayConfig.Remap(Rect(x, y), Docked(), to);
        var (wx, wy, ww, wh) = DisplayConfig.WorkAreaDiu(to[0]);

        remapped.X.ShouldBeGreaterThanOrEqualTo(wx);
        remapped.Y.ShouldBeGreaterThanOrEqualTo(wy);
        (remapped.X + remapped.W).ShouldBeLessThanOrEqualTo(wx + ww);
        (remapped.Y + remapped.H).ShouldBeLessThanOrEqualTo(wy + wh);
    }

    [Fact]
    public void Remap_ShrinksAFrameTooBigForTheNewWorkArea()
    {
        var from = Laptop();
        var to = new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1024, 768, 1.0, primary: true) };
        var (_, _, nw, nh) = DisplayConfig.WorkAreaDiu(to[0]);

        var remapped = DisplayConfig.Remap(Rect(0, 0, 1800, 1000), from, to);

        remapped.W.ShouldBe(nw);
        remapped.H.ShouldBe(nh);
    }

    [Fact]
    public void Remap_KeepsTheFrameSizeInDeviceIndependentUnitsAcrossAScalingChange()
    {
        // Same panel, scaling raised from 100% to 150%: the work area shrinks in DIU but the
        // frame keeps the size the user chose, so its icons and text stay the same size.
        var from = Laptop();
        var to = new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1920, 1080, 1.5, primary: true) };

        var remapped = DisplayConfig.Remap(Rect(100, 100, 230, 130), from, to);

        remapped.W.ShouldBe(230);
        remapped.H.ShouldBe(130);
    }

    [Fact]
    public void Remap_MovesFramesOffAVanishedMonitorOntoTheSurvivor()
    {
        var to = Laptop();
        var (wx, wy, ww, wh) = DisplayConfig.WorkAreaDiu(to[0]);

        // A frame that lived in the middle of the external monitor.
        var remapped = DisplayConfig.Remap(Rect(3000, 600), Docked(), to);

        remapped.X.ShouldBeInRange(wx, wx + ww);
        remapped.Y.ShouldBeInRange(wy, wy + wh);
    }

    [Fact]
    public void Remap_PreservesTheRelativeOrderOfNeighbouringFrames()
    {
        var from = Docked();
        var to = Laptop();

        var left = DisplayConfig.Remap(Rect(100, 100), from, to);
        var right = DisplayConfig.Remap(Rect(900, 100), from, to);

        left.X.ShouldBeLessThan(right.X);
    }

    [Fact]
    public void Remap_LeavesARolledFrameRolledAndClampsItsRestoreHeight()
    {
        var from = Laptop();
        var to = new List<MonitorInfo> { Mon("MONITOR\\LEN1234", 0, 0, 1024, 400, 1.0, primary: true) };
        var (_, _, _, nh) = DisplayConfig.WorkAreaDiu(to[0]);

        var remapped = DisplayConfig.Remap(Rect(100, 100, 230, 28, unrolled: 900), from, to);

        remapped.H.ShouldBe(28);                 // still rolled up
        remapped.UnrolledHeight.ShouldBe(nh);    // but it will not unroll off the screen
    }

    [Fact]
    public void Remap_LeavesUnrolledHeightAloneWhenItFits()
    {
        var remapped = DisplayConfig.Remap(Rect(100, 100, 230, 28, unrolled: 400), Laptop(), Laptop());

        remapped.UnrolledHeight.ShouldBe(400);
    }

    [Fact]
    public void Remap_WithNoTargetMonitorsReturnsTheFrameUnchanged()
    {
        var original = Rect(100, 200);
        var remapped = DisplayConfig.Remap(original, Laptop(), new List<MonitorInfo>());

        remapped.X.ShouldBe(original.X);
        remapped.Y.ShouldBe(original.Y);
    }

    [Fact]
    public void Remap_WithNoSourceMonitorsPlacesTheFrameOnThePrimary()
    {
        var to = Docked();
        var remapped = DisplayConfig.Remap(Rect(50, 50), new List<MonitorInfo>(), to);
        var (wx, wy, ww, wh) = DisplayConfig.WorkAreaDiu(to[DisplayConfig.PrimaryIndex(to)]);

        remapped.X.ShouldBeInRange(wx, wx + ww);
        remapped.Y.ShouldBeInRange(wy, wy + wh);
    }

    // ---------------------------------------------------------------
    // Describe
    // ---------------------------------------------------------------

    [Fact]
    public void Describe_SummarisesTheMonitorSet()
    {
        DisplayConfig.Describe(Laptop()).ShouldBe("1 monitor: 1920x1080 @100%");
        DisplayConfig.Describe(Docked()).ShouldBe("2 monitors: 1920x1080 @100%, 2560x1440 @100%");
        DisplayConfig.Describe(new List<MonitorInfo>()).ShouldBe("no monitors");
    }

    // ---------------------------------------------------------------
    // FrameRect
    // ---------------------------------------------------------------

    [Fact]
    public void Matches_ToleratesSubPixelDriftButNotRealMoves()
    {
        Rect(100, 100).Matches(Rect(100.2, 99.8)).ShouldBeTrue();
        Rect(100, 100).Matches(Rect(101, 100)).ShouldBeFalse();
        Rect(100, 100).Matches(null).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // LayoutStore
    // ---------------------------------------------------------------

    [Fact]
    public void Store_RoundTripsThroughDisk()
    {
        string path = Path.Combine(_tempDir, "layouts.json");
        var monitors = Docked();
        string fingerprint = DisplayConfig.Fingerprint(monitors);

        var store = new LayoutStore { ActiveFingerprint = fingerprint };
        var config = store.GetOrAdd(fingerprint, monitors);
        config.Frames["frame-a"] = Rect(10, 20, 300, 200, unrolled: 400);
        store.Save(path);

        var reloaded = LayoutStore.Load(path);

        reloaded.ActiveFingerprint.ShouldBe(fingerprint);
        reloaded.Configs.Count.ShouldBe(1);
        reloaded.Configs[0].Monitors.Count.ShouldBe(2);
        reloaded.Configs[0].Monitors[1].Id.ShouldBe("MONITOR\\DEL4099");
        reloaded.Configs[0].Frames["frame-a"].Matches(Rect(10, 20, 300, 200, unrolled: 400)).ShouldBeTrue();
    }

    [Fact]
    public void Store_LoadOfAMissingOrCorruptFileGivesAnEmptyStore()
    {
        LayoutStore.Load(Path.Combine(_tempDir, "nope.json")).Configs.ShouldBeEmpty();

        string corrupt = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(corrupt, "{ this is not json");
        LayoutStore.Load(corrupt).Configs.ShouldBeEmpty();

        string empty = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(empty, "   ");
        LayoutStore.Load(empty).Configs.ShouldBeEmpty();
    }

    [Fact]
    public void Store_SaveSkipsTheDiskWriteWhenNothingChanged()
    {
        string path = Path.Combine(_tempDir, "layouts.json");
        var store = new LayoutStore { ActiveFingerprint = "abc" };
        store.GetOrAdd("abc", Laptop()).Frames["f"] = Rect(1, 2);

        store.Save(path);
        var firstWrite = File.GetLastWriteTimeUtc(path);

        store.Save(path);

        File.GetLastWriteTimeUtc(path).ShouldBe(firstWrite);
    }

    [Fact]
    public void Store_PruneDropsGeometryForFramesThatNoLongerExist()
    {
        var store = new LayoutStore { ActiveFingerprint = "abc" };
        var config = store.GetOrAdd("abc", Laptop());
        config.Frames["alive"] = Rect(1, 2);
        config.Frames["deleted"] = Rect(3, 4);

        store.Prune(new[] { "alive" }, 16);

        config.Frames.Keys.ShouldBe(new[] { "alive" });
    }

    [Fact]
    public void Store_PruneKeepsTheActiveConfigurationEvenWhenItIsTheOldest()
    {
        var store = new LayoutStore { ActiveFingerprint = "oldest" };
        var oldest = store.GetOrAdd("oldest", Laptop());
        oldest.LastSeenUtc = DateTime.UtcNow.AddYears(-1);

        for (int i = 0; i < 5; i++)
        {
            store.GetOrAdd("cfg" + i, Laptop()).LastSeenUtc = DateTime.UtcNow.AddMinutes(-i);
        }

        store.Prune(Array.Empty<string>(), maxConfigs: 3);

        store.Configs.Count.ShouldBe(3);
        store.Find("oldest").ShouldNotBeNull();
    }

    [Fact]
    public void Store_MostRecentOtherPrefersTheActiveConfiguration()
    {
        var store = new LayoutStore { ActiveFingerprint = "active" };
        store.GetOrAdd("active", Laptop()).LastSeenUtc = DateTime.UtcNow.AddDays(-30);
        store.GetOrAdd("stale", Docked()).LastSeenUtc = DateTime.UtcNow;

        store.MostRecentOther("brand-new")!.Fingerprint.ShouldBe("active");
    }

    [Fact]
    public void Store_MostRecentOtherNeverReturnsTheConfigurationBeingBuilt()
    {
        // The "rebuild this layout" path deletes the active configuration and re-derives it;
        // it must not be handed itself as the source.
        var store = new LayoutStore { ActiveFingerprint = "active" };
        store.GetOrAdd("active", Laptop()).LastSeenUtc = DateTime.UtcNow;
        store.GetOrAdd("other", Docked()).LastSeenUtc = DateTime.UtcNow.AddHours(-1);

        store.MostRecentOther("active")!.Fingerprint.ShouldBe("other");
    }

    [Fact]
    public void Store_MostRecentOtherIsNullWhenThereIsNothingElse()
    {
        var store = new LayoutStore { ActiveFingerprint = "only" };
        store.GetOrAdd("only", Laptop());

        store.MostRecentOther("only").ShouldBeNull();
    }

    [Fact]
    public void Store_GetOrAddReturnsTheExistingConfiguration()
    {
        var store = new LayoutStore();
        var first = store.GetOrAdd("abc", Laptop());
        var second = store.GetOrAdd("abc", Docked());

        second.ShouldBeSameAs(first);
        store.Configs.Count.ShouldBe(1);
    }
}
