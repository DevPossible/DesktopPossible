using System;
using System.Collections.Generic;
using System.Linq;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for planning the frame-folder → Desktop migration (DesktopMigrationPlanner).
/// </summary>
public class DesktopMigrationPlannerTests
{
    private const string Store = @"C:\Store\Profiles\Default\Frames\f1";
    private const string Desktop = @"C:\Users\u\Desktop";

    private static List<DesktopMigrationEntry> Plan(IEnumerable<string> items,
        IEnumerable<string>? taken = null,
        IEnumerable<DesktopMigrationEntry>? journal = null,
        Func<string, string?>? target = null,
        Func<string, bool>? exists = null) =>
        DesktopMigrationPlanner.Plan(items,
            p => DesktopMigrationPlanner.IsInside(p, @"C:\Store"),
            exists ?? (_ => true),
            target ?? (_ => null),
            p => IconParking.IsOnDesktop(p, Desktop),
            Desktop,
            taken ?? Array.Empty<string>(),
            journal ?? Array.Empty<DesktopMigrationEntry>());

    [Fact]
    public void StoredFile_IsCopiedToTheDesktopUnderItsOwnName()
    {
        Plan(new[] { $@"{Store}\report.pdf" }).ShouldBe(new[]
        {
            new DesktopMigrationEntry($@"{Store}\report.pdf", $@"{Desktop}\report.pdf", true)
        });
    }

    [Fact]
    public void NameClash_GetsTheNextFreeSuffix_IncludingNamesPlannedInThisRun()
    {
        var plan = Plan(new[] { $@"{Store}\a.lnk", $@"C:\Store\Profiles\Default\Frames\f2\a.lnk" },
            taken: new[] { "A.lnk" });

        plan.Select(e => e.Dest).ShouldBe(new[] { $@"{Desktop}\a (1).lnk", $@"{Desktop}\a (2).lnk" });
    }

    [Fact]
    public void WrapperShortcutToADesktopItem_PointsStraightAtTheItem()
    {
        var plan = Plan(new[] { $@"{Store}\Projects.lnk" }, target: _ => $@"{Desktop}\Projects");

        plan.ShouldBe(new[] { new DesktopMigrationEntry($@"{Store}\Projects.lnk", $@"{Desktop}\Projects", false) });
    }

    [Fact]
    public void ShortcutToSomethingElsewhere_IsCopiedAsAShortcut()
    {
        var plan = Plan(new[] { $@"{Store}\Tool.lnk" }, target: _ => @"C:\Program Files\Tool\tool.exe");
        plan.Single().CopyFile.ShouldBeTrue();
        plan.Single().Dest.ShouldBe($@"{Desktop}\Tool.lnk");
    }

    [Fact]
    public void ItemsOutsideLegacyFolders_AndMissingFiles_AreSkipped()
    {
        Plan(new[] { $@"{Desktop}\already.txt", @"C:\Other\x.txt" }).ShouldBeEmpty();
        Plan(new[] { $@"{Store}\gone.txt" }, exists: _ => false).ShouldBeEmpty();
    }

    [Fact]
    public void TheSameFileListedTwice_IsPlannedOnce()
    {
        // Tab 0 mirrors the main item list.
        Plan(new[] { $@"{Store}\a.txt", $@"{Store}\A.TXT" }).Count.ShouldBe(1);
    }

    [Fact]
    public void JournalEntries_AreKeptAndTheirNamesReserved()
    {
        var leftover = new DesktopMigrationEntry($@"{Store}\old.txt", $@"{Desktop}\b.txt", true);
        var plan = Plan(new[] { $@"{Store}\old.txt", $@"{Store}\b.txt" }, journal: new[] { leftover });

        plan.ShouldBe(new[]
        {
            leftover,
            new DesktopMigrationEntry($@"{Store}\b.txt", $@"{Desktop}\b (1).txt", true),
        });
    }

    [Fact]
    public void IdenticalFileFromAnotherProfile_SharesTheFirstCopy()
    {
        const string other = @"C:\Store\Profiles\Personal\Frames\f9\Tool.lnk";
        var plan = DesktopMigrationPlanner.Plan(new[] { $@"{Store}\Tool.lnk", other },
            p => DesktopMigrationPlanner.IsInside(p, @"C:\Store"), _ => true, _ => null,
            p => IconParking.IsOnDesktop(p, Desktop), Desktop, Array.Empty<string>(),
            Array.Empty<DesktopMigrationEntry>(), sameContent: (_, _) => true);

        plan.ShouldBe(new[]
        {
            new DesktopMigrationEntry($@"{Store}\Tool.lnk", $@"{Desktop}\Tool.lnk", true),
            new DesktopMigrationEntry(other, $@"{Desktop}\Tool.lnk", false),
        });
    }

    [Fact]
    public void SameNameButDifferentContent_GetsItsOwnCopy()
    {
        var plan = DesktopMigrationPlanner.Plan(new[] { $@"{Store}\a.txt", @"C:\Store\f2\a.txt" },
            p => DesktopMigrationPlanner.IsInside(p, @"C:\Store"), _ => true, _ => null,
            p => IconParking.IsOnDesktop(p, Desktop), Desktop, Array.Empty<string>(),
            Array.Empty<DesktopMigrationEntry>(), sameContent: (_, _) => false);

        plan.Select(e => (e.Dest, e.CopyFile)).ShouldBe(new[] { ($@"{Desktop}\a.txt", true), ($@"{Desktop}\a (1).txt", true) });
    }

    [Theory]
    [InlineData(@"C:\Store\x\a.txt", @"C:\Store", true)]
    [InlineData(@"c:\store\x", @"C:\Store\", true)]
    [InlineData(@"C:\StoreOther\a.txt", @"C:\Store", false)]
    [InlineData(@"C:\Store", @"C:\Store", false)]
    public void IsInside_RequiresAPathBelowTheFolder(string path, string folder, bool expected)
    {
        DesktopMigrationPlanner.IsInside(path, folder).ShouldBe(expected);
    }
}
