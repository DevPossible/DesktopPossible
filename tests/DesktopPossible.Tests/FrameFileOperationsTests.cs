using System;
using System.IO;
using System.Linq;
using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the file-safety operations on folder-backed frames. Every test
/// runs against a throwaway temp root (store) and temp "desktop" — the real AppData
/// store and the user's Desktop are never touched.
/// </summary>
public class FrameFileOperationsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _root;
    private readonly string _desktop;

    private const string Profile = "Work";

    public FrameFileOperationsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfp-fileops-tests-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_tempDir, "store");
        _desktop = Path.Combine(_tempDir, "desktop");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_desktop);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string FrameFolder(string frameId) => FrameStore.BuildFrameFolderPath(_root, Profile, frameId);

    private string CreateFrameFile(string frameId, string name, string content = "x")
    {
        string folder = FrameFolder(frameId);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------------------------------------------------------------
    // Enumeration
    // ---------------------------------------------------------------

    [Fact]
    public void EnumerateFrameFiles_MissingFolder_ReturnsEmpty()
    {
        FrameFileOperations.EnumerateFrameFiles(_root, Profile, "nope").ShouldBeEmpty();
    }

    [Fact]
    public void EnumerateFrameFiles_ListsFilesRecursively()
    {
        CreateFrameFile("f1", "a.lnk");
        CreateFrameFile("f1", "b.txt");
        string sub = Path.Combine(FrameFolder("f1"), "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "c.txt"), "x");

        var files = FrameFileOperations.EnumerateFrameFiles(_root, Profile, "f1");
        files.Count.ShouldBe(3);
        files.Select(Path.GetFileName).ShouldBe(new[] { "a.lnk", "b.txt", "c.txt" }, ignoreOrder: true);
    }

    [Fact]
    public void EnumerateProfileFrameIds_ComesFromDirectory_NotFramesJson()
    {
        CreateFrameFile("orphan-a", "a.txt");
        CreateFrameFile("orphan-b", "b.txt");
        Directory.CreateDirectory(FrameFolder("empty-c"));

        FrameFileOperations.EnumerateProfileFrameIds(_root, Profile)
            .ShouldBe(new[] { "empty-c", "orphan-a", "orphan-b" });
        FrameFileOperations.CountProfileFiles(_root, Profile).ShouldBe(2);
    }

    [Fact]
    public void EnumerateProfileFrameIds_UnknownProfile_ReturnsEmpty()
    {
        FrameFileOperations.EnumerateProfileFrameIds(_root, "Ghost").ShouldBeEmpty();
        FrameFileOperations.CountProfileFiles(_root, "Ghost").ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Move to desktop
    // ---------------------------------------------------------------

    [Fact]
    public void MoveFrameFilesToDesktop_MovesEveryFile_AndRemovesEmptyFolder()
    {
        CreateFrameFile("f1", "a.lnk", "A");
        CreateFrameFile("f1", "b.txt", "B");

        int moved = FrameFileOperations.MoveFrameFilesToDesktop(_root, _desktop, Profile, "f1");

        moved.ShouldBe(2);
        File.ReadAllText(Path.Combine(_desktop, "a.lnk")).ShouldBe("A");
        File.ReadAllText(Path.Combine(_desktop, "b.txt")).ShouldBe("B");
        Directory.Exists(FrameFolder("f1")).ShouldBeFalse();
    }

    [Fact]
    public void MoveFrameFilesToDesktop_SuffixesOnCollision_AndKeepsExistingDesktopFile()
    {
        File.WriteAllText(Path.Combine(_desktop, "notes.txt"), "desktop-original");
        File.WriteAllText(Path.Combine(_desktop, "notes (1).txt"), "desktop-second");
        CreateFrameFile("f1", "notes.txt", "from-frame");

        int moved = FrameFileOperations.MoveFrameFilesToDesktop(_root, _desktop, Profile, "f1");

        moved.ShouldBe(1);
        File.ReadAllText(Path.Combine(_desktop, "notes.txt")).ShouldBe("desktop-original");
        File.ReadAllText(Path.Combine(_desktop, "notes (1).txt")).ShouldBe("desktop-second");
        File.ReadAllText(Path.Combine(_desktop, "notes (2).txt")).ShouldBe("from-frame");
    }

    [Fact]
    public void MoveFrameFilesToDesktop_NoFolder_ReturnsZero()
    {
        FrameFileOperations.MoveFrameFilesToDesktop(_root, _desktop, Profile, "missing").ShouldBe(0);
        Directory.GetFiles(_desktop).ShouldBeEmpty();
    }

    [Fact]
    public void MoveProfileFilesToDesktop_MovesAllFrames_AndRemovesFramesDir()
    {
        CreateFrameFile("f1", "a.txt");
        CreateFrameFile("f2", "b.txt");
        CreateFrameFile("f2", "c.txt");

        int moved = FrameFileOperations.MoveProfileFilesToDesktop(_root, _desktop, Profile);

        moved.ShouldBe(3);
        Directory.GetFiles(_desktop).Length.ShouldBe(3);
        Directory.Exists(FrameFileOperations.ProfileFramesDir(_root, Profile)).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Delete
    // ---------------------------------------------------------------

    [Fact]
    public void DeleteFrameFiles_RemovesFiles_AndFolder()
    {
        CreateFrameFile("f1", "a.lnk");
        CreateFrameFile("f1", "b.txt");

        int deleted = FrameFileOperations.DeleteFrameFiles(_root, Profile, "f1");

        deleted.ShouldBe(2);
        Directory.Exists(FrameFolder("f1")).ShouldBeFalse();
        Directory.GetFiles(_desktop).ShouldBeEmpty();
    }

    [Fact]
    public void DeleteProfileFiles_RemovesEveryFrameFolder_AndFramesDir()
    {
        CreateFrameFile("f1", "a.txt");
        CreateFrameFile("f2", "b.txt");
        Directory.CreateDirectory(FrameFolder("f3")); // empty folder is cleaned too

        int deleted = FrameFileOperations.DeleteProfileFiles(_root, Profile);

        deleted.ShouldBe(2);
        Directory.Exists(FrameFileOperations.ProfileFramesDir(_root, Profile)).ShouldBeFalse();
    }

    [Fact]
    public void DeleteFrameFiles_OnlyTouchesThatFrame()
    {
        CreateFrameFile("f1", "a.txt");
        string other = CreateFrameFile("f2", "b.txt");

        FrameFileOperations.DeleteFrameFiles(_root, Profile, "f1");

        File.Exists(other).ShouldBeTrue();
    }

    [Fact]
    public void RemoveFrameFolder_KeepsFolderHoldingFiles()
    {
        string file = CreateFrameFile("f1", "a.txt");
        FrameFileOperations.RemoveFrameFolder(_root, Profile, "f1");
        File.Exists(file).ShouldBeTrue();

        File.Delete(file);
        FrameFileOperations.RemoveFrameFolder(_root, Profile, "f1");
        Directory.Exists(FrameFolder("f1")).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // ClearFrameItems — frames.json manipulation
    // ---------------------------------------------------------------

    [Fact]
    public void IsInsideFolder_OnlyAcceptsRootedPathsUnderFolder()
    {
        string folder = FrameFolder("f1");
        FrameFileOperations.IsInsideFolder(folder, Path.Combine(folder, "a.lnk")).ShouldBeTrue();
        FrameFileOperations.IsInsideFolder(folder, Path.Combine(folder, "sub", "a.lnk")).ShouldBeTrue();
        FrameFileOperations.IsInsideFolder(folder, Path.Combine(FrameFolder("f1-other"), "a.lnk")).ShouldBeFalse();
        FrameFileOperations.IsInsideFolder(folder, @"Shortcuts\legacy.lnk").ShouldBeFalse();
        FrameFileOperations.IsInsideFolder(folder, @"C:\Users\me\Desktop\file.txt").ShouldBeFalse();
        FrameFileOperations.IsInsideFolder(folder, null).ShouldBeFalse();
        FrameFileOperations.IsInsideFolder(folder, "").ShouldBeFalse();
    }

    [Fact]
    public void ClearFrameItemsInJson_RemovesStoredItems_KeepsLegacy_AcrossMainAndTabs()
    {
        string folder = FrameFolder("f1");
        string json = Path.Combine(_tempDir, "frames.json");
        var frames = new JArray
        {
            new JObject
            {
                ["Id"] = "f1",
                ["Title"] = "Games",
                ["ItemsType"] = "Data",
                ["TabsEnabled"] = "true",
                ["Items"] = new JArray
                {
                    new JObject { ["Filename"] = Path.Combine(folder, "steam.lnk"), ["DisplayName"] = "Steam" },
                    new JObject { ["Filename"] = @"Shortcuts\legacy.lnk", ["DisplayName"] = "Legacy" },
                    new JObject { ["Filename"] = @"C:\Users\me\Desktop\elsewhere.txt", ["DisplayName"] = "Elsewhere" }
                },
                ["Tabs"] = new JArray
                {
                    new JObject
                    {
                        ["TabName"] = "Tab A",
                        ["Items"] = new JArray
                        {
                            new JObject { ["Filename"] = Path.Combine(folder, "doc.txt") },
                            new JObject { ["Filename"] = @"D:\keep\me.lnk" }
                        }
                    },
                    new JObject
                    {
                        ["TabName"] = "Tab B",
                        ["Items"] = new JArray { new JObject { ["Filename"] = Path.Combine(folder, "sub", "nested.lnk") } }
                    }
                }
            },
            new JObject
            {
                ["Id"] = "f2",
                ["Title"] = "Other",
                ["Items"] = new JArray { new JObject { ["Filename"] = Path.Combine(folder, "looks-like-f1.lnk") } }
            }
        };
        File.WriteAllText(json, frames.ToString());

        int removed = FrameFileOperations.ClearFrameItemsInJson(json, "f1", folder);

        removed.ShouldBe(3);
        var saved = JArray.Parse(File.ReadAllText(json));
        saved.Count.ShouldBe(2);

        var f1 = saved.OfType<JObject>().First(f => f["Id"]?.ToString() == "f1");
        f1["Title"]?.ToString().ShouldBe("Games");
        var mainNames = ((JArray)f1["Items"]!).Select(i => i["DisplayName"]?.ToString()).ToList();
        mainNames.ShouldBe(new[] { "Legacy", "Elsewhere" });
        ((JArray)f1["Tabs"]![0]!["Items"]!).Select(i => i["Filename"]?.ToString()).ShouldBe(new[] { @"D:\keep\me.lnk" });
        ((JArray)f1["Tabs"]![1]!["Items"]!).ShouldBeEmpty();

        // Other frames are untouched even when their items point into f1's folder.
        var f2 = saved.OfType<JObject>().First(f => f["Id"]?.ToString() == "f2");
        ((JArray)f2["Items"]!).Count.ShouldBe(1);

        File.Exists(json + ".tmp").ShouldBeFalse(); // atomic write left no temp file behind
    }

    [Fact]
    public void ClearFrameItemsInJson_NothingToRemove_LeavesFileUntouched()
    {
        string json = Path.Combine(_tempDir, "frames.json");
        string original = new JArray
        {
            new JObject { ["Id"] = "f1", ["Items"] = new JArray { new JObject { ["Filename"] = @"Shortcuts\legacy.lnk" } } }
        }.ToString();
        File.WriteAllText(json, original);
        var stamp = File.GetLastWriteTimeUtc(json);

        FrameFileOperations.ClearFrameItemsInJson(json, "f1", FrameFolder("f1")).ShouldBe(0);

        File.ReadAllText(json).ShouldBe(original);
        File.GetLastWriteTimeUtc(json).ShouldBe(stamp);
    }

    [Fact]
    public void ClearFrameItemsInJson_MissingFileOrFrame_ReturnsZero()
    {
        FrameFileOperations.ClearFrameItemsInJson(Path.Combine(_tempDir, "nope.json"), "f1", FrameFolder("f1")).ShouldBe(0);

        string json = Path.Combine(_tempDir, "frames.json");
        File.WriteAllText(json, "[]");
        FrameFileOperations.ClearFrameItemsInJson(json, "f1", FrameFolder("f1")).ShouldBe(0);
    }

    [Fact]
    public void RemoveItemsInsideFolder_HandlesNullArrays()
    {
        FrameFileOperations.RemoveItemsInsideFolder(null, null, FrameFolder("f1")).ShouldBe(0);

        var items = new JArray { new JObject { ["Filename"] = Path.Combine(FrameFolder("f1"), "a.lnk") } };
        FrameFileOperations.RemoveItemsInsideFolder(items, null, FrameFolder("f1")).ShouldBe(1);
        items.ShouldBeEmpty();
    }
}
