using System;
using System.Collections.Generic;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the desktop-icon parking rules (IconParking): which framed desktop
/// icons get parked off-screen, which unframed ones go back, and what is remembered.
/// </summary>
public class IconParkingTests
{
    private const string A = @"C:\Users\u\Desktop\a.lnk";
    private const string B = @"C:\Users\u\Desktop\b.txt";
    private static readonly (int X, int Y) Parked = (IconParking.ParkX, IconParking.ParkY);

    private static HashSet<string> Set(params string[] paths) => new(paths, StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, (int X, int Y)> Map(params (string Path, int X, int Y)[] entries)
    {
        var d = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) d[e.Path] = (e.X, e.Y);
        return d;
    }
    private static bool Exists(string _) => true;
    private static bool Gone(string _) => false;

    [Fact]
    public void NewlyFramedDesktopIcon_IsRememberedThenParked()
    {
        var plan = IconParking.Compute(Set(A), Map((A, 100, 200), (B, 300, 0)), Map(), Exists);

        plan.Park.ShouldBe(new[] { A });
        plan.State[A].ShouldBe((100, 200));
        plan.State.ContainsKey(B).ShouldBeFalse(); // unframed icons are never touched
        plan.StateChanged.ShouldBeTrue();
        plan.Restore.ShouldBeEmpty();
    }

    [Fact]
    public void AlreadyParkedFramedIcon_NeedsNothing()
    {
        var plan = IconParking.Compute(Set(A), Map((A, Parked.X, Parked.Y)), Map((A, 100, 200)), Exists);

        plan.Park.ShouldBeEmpty();
        plan.Restore.ShouldBeEmpty();
        plan.StateChanged.ShouldBeFalse();
    }

    [Fact]
    public void IconClampedIntoViewByExplorer_IsReparked_AndKeepsItsOriginalPosition()
    {
        // F5 pulled the parked icon into a screen corner: park it again, but the corner is
        // not where it belongs.
        var plan = IconParking.Compute(Set(A), Map((A, 5938, 1774)), Map((A, 100, 200)), Exists);

        plan.Park.ShouldBe(new[] { A });
        plan.State[A].ShouldBe((100, 200));
        plan.StateChanged.ShouldBeFalse();
    }

    [Fact]
    public void UnframedParkedIcon_IsRestoredAndForgotten()
    {
        var plan = IconParking.Compute(Set(), Map((A, Parked.X, Parked.Y)), Map((A, 100, 200)), Exists);

        plan.Restore.ShouldBe(new[] { (A, 100, 200) });
        plan.State.ShouldBeEmpty();
        plan.StateChanged.ShouldBeTrue();
    }

    [Fact]
    public void UnframedIconAlreadyVisible_IsLeftWhereItWasDropped()
    {
        // Dragged out of its frame onto the desktop: PlaceAt showed it at the drop point.
        var plan = IconParking.Compute(Set(), Map((A, 640, 480)), Map((A, 100, 200)), Exists);

        plan.Restore.ShouldBeEmpty();
        plan.State.ShouldBeEmpty();
    }

    [Fact]
    public void RememberedIconMissingFromView_IsKeptWhileTheFileExists()
    {
        // The desktop view hasn't listed it yet (Explorer still loading): keep the record.
        var plan = IconParking.Compute(Set(), Map(), Map((A, 100, 200)), Exists);

        plan.State[A].ShouldBe((100, 200));
        plan.StateChanged.ShouldBeFalse();
    }

    [Fact]
    public void RememberedIconWhoseFileIsGone_IsForgotten()
    {
        var plan = IconParking.Compute(Set(), Map(), Map((A, 100, 200)), Gone);

        plan.State.ShouldBeEmpty();
        plan.StateChanged.ShouldBeTrue();
    }

    [Fact]
    public void FramedItemNotOnTheDesktop_IsIgnored()
    {
        // Store-backed items are framed too, but never appear in the desktop view.
        var plan = IconParking.Compute(Set(@"C:\Store\Frames\x\c.lnk"), Map((A, 1, 1)), Map(), Exists);

        plan.Park.ShouldBeEmpty();
        plan.State.ShouldBeEmpty();
    }

    [Fact]
    public void PathsCompareCaseInsensitively()
    {
        var plan = IconParking.Compute(Set(A.ToUpperInvariant()), Map((A, 100, 200)), Map(), Exists);
        plan.Park.Count.ShouldBe(1);
    }

    [Fact]
    public void RestoreAll_ReturnsEveryRememberedIconStillOnTheDesktop()
    {
        var moves = IconParking.RestoreAll(
            Map((A, Parked.X, Parked.Y), (B, 5938, 1774)),
            Map((A, 100, 200), (B, 300, 0), (@"C:\Users\u\Desktop\gone.txt", 5, 5)));

        moves.ShouldBe(new[] { (A, 100, 200), (B, 300, 0) }, ignoreOrder: true);
    }

    [Theory]
    [InlineData(@"C:\Users\u\Desktop\a.lnk", true)]
    [InlineData(@"c:\users\U\desktop\Folder", true)]
    [InlineData(@"C:\Users\Public\Desktop\b.lnk", true)]
    [InlineData(@"C:\Users\u\Desktop\Sub\c.txt", false)]
    [InlineData(@"C:\Users\u\Downloads\a.lnk", false)]
    [InlineData("", false)]
    public void IsOnDesktop_MatchesOnlyDirectChildrenOfTheDesktopFolders(string path, bool expected)
    {
        IconParking.IsOnDesktop(path, @"C:\Users\u\Desktop\", @"C:\Users\Public\Desktop").ShouldBe(expected);
    }

    [Fact]
    public void IsParked_RequiresBothAxesBeyondThreshold()
    {
        IconParking.IsParked(Parked).ShouldBeTrue();
        IconParking.IsParked((5938, 1774)).ShouldBeFalse();
        IconParking.IsParked((30000, 10)).ShouldBeFalse();
    }
}
