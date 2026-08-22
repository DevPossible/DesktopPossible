using System;
using System.IO;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the per-frame file store: folder path building, profile-name
/// sanitization, collision suffixing, and move/copy semantics. Everything runs in a
/// throwaway temp directory — no registry, COM, network, or app profile data.
/// </summary>
public class FrameStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FrameStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfp-framestore-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateFile(string dir, string name, string content = "x")
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------------------------------------------------------------
    // Path builder + profile-name sanitization
    // ---------------------------------------------------------------

    [Fact]
    public void RootDir_IsUnderLocalAppData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FrameStore.RootDir.ShouldBe(Path.Combine(local, "DesktopPossible", "Profiles"));
    }

    [Fact]
    public void LegacyRootDir_IsUnderLocalAppData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FrameStore.LegacyRootDir.ShouldBe(Path.Combine(local, "DesktopFramesPossible", "Profiles"));
    }

    [Theory]
    [InlineData(@"C:\old\Profiles\Default\Frames\abc\x.lnk", @"C:\new\Profiles\Default\Frames\abc\x.lnk")]
    [InlineData(@"c:\OLD\profiles\Work\Frames\id\y.txt", @"C:\new\Profiles\Work\Frames\id\y.txt")]
    public void RemapLegacyPath_RewritesPathsUnderLegacyRoot(string input, string expected)
    {
        FrameStore.RemapLegacyPath(@"C:\old\Profiles", @"C:\new\Profiles", input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"C:\elsewhere\x.lnk")]
    [InlineData(@"C:\old\ProfilesX\Default\x.lnk")]
    [InlineData(@"Shortcuts\x.lnk")]
    [InlineData("")]
    [InlineData(null)]
    public void RemapLegacyPath_LeavesOtherPathsUnchanged(string? input)
    {
        FrameStore.RemapLegacyPath(@"C:\old\Profiles", @"C:\new\Profiles", input).ShouldBe(input);
    }

    [Fact]
    public void BuildFrameFolderPath_UsesRootProfileFramesAndId()
    {
        string id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        string path = FrameStore.BuildFrameFolderPath(@"C:\root", "Default", id);
        path.ShouldBe(Path.Combine(@"C:\root", "Default", "Frames", id));
    }

    [Theory]
    [InlineData("Work", "Work")]
    [InlineData("  Work  ", "Work")]
    [InlineData("Home/Office", "Home_Office")]
    [InlineData("A:B*C?D", "A_B_C_D")]
    [InlineData("dots...", "dots")]
    [InlineData("", "Default")]
    [InlineData("   ", "Default")]
    [InlineData(null, "Default")]
    public void SanitizeProfileName_ReplacesInvalidChars_AndFallsBackToDefault(string? input, string expected)
    {
        FrameStore.SanitizeProfileName(input).ShouldBe(expected);
    }

    [Fact]
    public void BuildFrameFolderPath_SanitizesProfileName()
    {
        string path = FrameStore.BuildFrameFolderPath(@"C:\root", "Gam|ing", "id1");
        path.ShouldBe(Path.Combine(@"C:\root", "Gam_ing", "Frames", "id1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildFrameFolderPath_RejectsMissingFrameId(string id)
    {
        Should.Throw<ArgumentException>(() => FrameStore.BuildFrameFolderPath(@"C:\root", "Default", id));
    }

    [Fact]
    public void IsInsideAnyFrameFolder_MatchesOnlyFrameFolderFiles()
    {
        string root = Path.Combine(_tempDir, "store");
        string inside = Path.Combine(root, "Default", "Frames", "id1", "file.pdf");
        string profileLevel = Path.Combine(root, "Default", "file.pdf");
        string outside = Path.Combine(_tempDir, "elsewhere", "file.pdf");

        FrameStore.IsInsideAnyFrameFolder(root, inside).ShouldBeTrue();
        FrameStore.IsInsideAnyFrameFolder(root, inside.ToUpperInvariant()).ShouldBeTrue();
        FrameStore.IsInsideAnyFrameFolder(root, profileLevel).ShouldBeFalse();
        FrameStore.IsInsideAnyFrameFolder(root, outside).ShouldBeFalse();
        FrameStore.IsInsideAnyFrameFolder(root, @"Shortcuts\legacy.lnk").ShouldBeFalse();
        FrameStore.IsInsideAnyFrameFolder(root, "").ShouldBeFalse();
        FrameStore.IsInsideAnyFrameFolder(root, null).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Collision suffixing
    // ---------------------------------------------------------------

    [Fact]
    public void UniqueDestinationPath_ReturnsPlainName_WhenFree()
    {
        FrameStore.UniqueDestinationPath(_tempDir, "a.pdf").ShouldBe(Path.Combine(_tempDir, "a.pdf"));
    }

    [Fact]
    public void UniqueDestinationPath_AppendsCounter_OnCollision()
    {
        CreateFile(_tempDir, "a.pdf");
        CreateFile(_tempDir, "a (1).pdf");

        FrameStore.UniqueDestinationPath(_tempDir, "a.pdf").ShouldBe(Path.Combine(_tempDir, "a (2).pdf"));
    }

    // ---------------------------------------------------------------
    // Move / copy semantics
    // ---------------------------------------------------------------

    [Fact]
    public void MoveIntoFolder_Move_RemovesSource_AndReturnsAbsoluteDestination()
    {
        string src = CreateFile(Path.Combine(_tempDir, "src"), "doc.pdf", "hello");
        string dest = Path.Combine(_tempDir, "frame");

        string result = FrameStore.MoveIntoFolder(dest, src, copy: false);

        result.ShouldBe(Path.Combine(dest, "doc.pdf"));
        Path.IsPathRooted(result).ShouldBeTrue();
        File.Exists(src).ShouldBeFalse();
        File.ReadAllText(result).ShouldBe("hello");
    }

    [Fact]
    public void MoveIntoFolder_Copy_KeepsSource()
    {
        string src = CreateFile(Path.Combine(_tempDir, "src"), "pic.png", "img");
        string dest = Path.Combine(_tempDir, "frame");

        string result = FrameStore.MoveIntoFolder(dest, src, copy: true);

        result.ShouldBe(Path.Combine(dest, "pic.png"));
        File.Exists(src).ShouldBeTrue();
        File.ReadAllText(result).ShouldBe("img");
    }

    [Fact]
    public void MoveIntoFolder_SuffixesName_WhenDestinationExists()
    {
        string dest = Path.Combine(_tempDir, "frame");
        CreateFile(dest, "doc.pdf", "old");
        string src = CreateFile(Path.Combine(_tempDir, "src"), "doc.pdf", "new");

        string result = FrameStore.MoveIntoFolder(dest, src, copy: false);

        result.ShouldBe(Path.Combine(dest, "doc (1).pdf"));
        File.ReadAllText(Path.Combine(dest, "doc.pdf")).ShouldBe("old");
        File.ReadAllText(result).ShouldBe("new");
        File.Exists(src).ShouldBeFalse();
    }

    [Fact]
    public void MoveIntoFolder_CreatesDestinationFolder()
    {
        string src = CreateFile(Path.Combine(_tempDir, "src"), "a.txt");
        string dest = Path.Combine(_tempDir, "deep", "nested", "frame");

        FrameStore.MoveIntoFolder(dest, src, copy: false);

        Directory.Exists(dest).ShouldBeTrue();
    }

    [Fact]
    public void MoveIntoFolder_MissingSource_Throws_AndCreatesNothing()
    {
        string dest = Path.Combine(_tempDir, "frame");

        Should.Throw<FileNotFoundException>(() =>
            FrameStore.MoveIntoFolder(dest, Path.Combine(_tempDir, "nope.pdf"), copy: false));

        Directory.Exists(dest).ShouldBeFalse();
    }

    [Fact]
    public void MoveIntoFolder_FailedMove_LeavesSourceIntact()
    {
        string src = CreateFile(Path.Combine(_tempDir, "src"), "locked.pdf", "keep");
        string dest = Path.Combine(_tempDir, "frame");
        Directory.CreateDirectory(dest);

        // A source held open with no sharing defeats both File.Move and the Copy+Delete
        // fallback: the operation fails and the source must survive untouched.
        using (File.Open(src, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Should.Throw<Exception>(() => FrameStore.MoveIntoFolder(dest, src, copy: false));
        }

        File.Exists(src).ShouldBeTrue();
        File.ReadAllText(src).ShouldBe("keep");
    }
}
