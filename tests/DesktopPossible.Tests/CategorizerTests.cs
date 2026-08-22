using System;
using System.Collections.Generic;
using System.IO;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the pure parts of the app-categorization engine:
/// known-app lookup, path heuristics, tag mapping, and the category-cache
/// round-trip (via AtomicFile). No network, no COM, no registry.
/// </summary>
public class CategorizerTests : IDisposable
{
    private readonly string _tempDir;

    public CategorizerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dfp-categorizer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------
    // Fixed categories
    // ---------------------------------------------------------------

    [Fact]
    public void Categories_AreTheNineFixedOnes_InDisplayOrder()
    {
        AppCategorizer.Categories.ShouldBe(new[]
        {
            "Productivity", "Utilities", "Games", "VR", "Developer Tools", "Security Apps", "Media",
            "Documents", "Images"
        });
    }

    // ---------------------------------------------------------------
    // Tier 1a — known-app list
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Productivity")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "Games")]
    [InlineData(@"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe", "Developer Tools")]
    [InlineData(@"C:\Program Files\VideoLAN\VLC\vlc.exe", "Media")]
    [InlineData(@"C:\Program Files\Bitwarden\Bitwarden.exe", "Security Apps")]
    [InlineData(@"C:\Program Files\7-Zip\7zFM.exe", "Utilities")]
    [InlineData(@"C:\Program Files\Oculus\Support\oculus-client\OculusClient.exe", "VR")]
    [InlineData(@"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe", "Developer Tools")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Spotify\Spotify.exe", "Media")]
    [InlineData(@"C:\Program Files\KeePass Password Safe 2\KeePass.exe", "Security Apps")]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", "Productivity")]
    [InlineData(@"C:\Program Files\Notepad++\notepad++.exe", "Developer Tools")]
    public void ClassifyKnown_RecognizesWellKnownExes(string targetPath, string expected)
    {
        AppCategorizer.ClassifyKnown(targetPath, null).ShouldBe(expected);
    }

    [Fact]
    public void ClassifyKnown_IsCaseInsensitive()
    {
        AppCategorizer.ClassifyKnown(@"C:\APPS\CHROME.EXE", null).ShouldBe("Productivity");
        AppCategorizer.ClassifyKnown(@"c:\apps\ChRoMe.exe", null).ShouldBe("Productivity");
    }

    [Fact]
    public void ClassifyKnown_MatchesDisplayNameWhenExeUnknown()
    {
        AppCategorizer.ClassifyKnown(@"C:\Some\launcher.exe", "Visual Studio Code").ShouldBe("Developer Tools");
        AppCategorizer.ClassifyKnown(null, "Google Chrome").ShouldBe("Productivity");
    }

    [Fact]
    public void ClassifyKnown_UnknownApp_ReturnsNull()
    {
        AppCategorizer.ClassifyKnown(@"C:\Program Files\Contoso\contosoapp.exe", "Contoso App").ShouldBeNull();
        AppCategorizer.ClassifyKnown(null, null).ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Tier 1b — path heuristics
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Portal 2\portal2.exe", null, "Games")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Foo\foo.exe", null, "Games")]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteClient.exe", null, "Games")]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\Games\Witcher\witcher3.exe", null, "Games")]
    [InlineData(@"C:\Riot Games\League of Legends\LeagueClient.exe", null, "Games")]
    [InlineData(@"C:\Program Files\Oculus\Software\app\app.exe", null, "VR")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\vrmonitor.exe", null, "VR")]
    public void ClassifyByPath_MatchesInstallPathFragments(string target, string? args, string expected)
    {
        AppCategorizer.ClassifyByPath(target, args).ShouldBe(expected);
    }

    [Fact]
    public void ClassifyByPath_ChecksArgumentsAndUrls()
    {
        // Steam .url desktop entries carry the protocol in the URL, not a path.
        AppCategorizer.ClassifyByPath(null, "steam://rungameid/620").ShouldBe("Games");
        AppCategorizer.ClassifyByPath(@"C:\Windows\explorer.exe", "steam://rungameid/620").ShouldBe("Games");
        AppCategorizer.ClassifyByPath(null, "com.epicgames.launcher://apps/foo").ShouldBe("Games");
    }

    [Fact]
    public void ClassifyByPath_NoGameStorePath_ReturnsNull()
    {
        AppCategorizer.ClassifyByPath(@"C:\Program Files\Contoso\app.exe", null).ShouldBeNull();
        AppCategorizer.ClassifyByPath(@"C:\Users\u\Documents\report.pdf", "").ShouldBeNull();
        AppCategorizer.ClassifyByPath(null, null).ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Tier 2 — tag mapping
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(new[] { "game" }, "Games")]
    [InlineData(new[] { "fun", "gaming", "launcher" }, "Games")]
    [InlineData(new[] { "antivirus" }, "Security Apps")]
    [InlineData(new[] { "vpn", "networking" }, "Security Apps")]
    [InlineData(new[] { "vr" }, "VR")]
    [InlineData(new[] { "ide", "editor" }, "Developer Tools")]
    [InlineData(new[] { "video player" }, "Media")]
    [InlineData(new[] { "office suite" }, "Productivity")]
    [InlineData(new[] { "system tool" }, "Utilities")]
    public void CategoryFromTags_MapsKeywordsToCategories(string[] tags, string expected)
    {
        AppCategorizer.CategoryFromTags(tags).ShouldBe(expected);
    }

    [Fact]
    public void CategoryFromTags_UnknownTags_ReturnNull()
    {
        AppCategorizer.CategoryFromTags(new[] { "foo", "bar", "baz" }).ShouldBeNull();
        AppCategorizer.CategoryFromTags(Array.Empty<string>()).ShouldBeNull();
        AppCategorizer.CategoryFromTags(null).ShouldBeNull();
    }

    [Fact]
    public void CategoryFromTags_VrBeatsGames_WhenBothPresent()
    {
        // A VR game belongs in the more specific VR frame.
        AppCategorizer.CategoryFromTags(new[] { "game", "vr" }).ShouldBe("VR");
    }

    // ---------------------------------------------------------------
    // Cache round-trip (via AtomicFile)
    // ---------------------------------------------------------------

    [Fact]
    public void CategoryCache_RoundTrips_ThroughAtomicFile()
    {
        string path = Path.Combine(_tempDir, "category_cache.json");
        var cache = new Dictionary<string, CategoryCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = new CategoryCacheEntry { Category = "Productivity", Timestamp = DateTime.UtcNow },
            ["mystery"] = new CategoryCacheEntry { Category = "none", Timestamp = DateTime.UtcNow }
        };

        AppCategorizer.SaveCategoryCache(path, cache);
        File.Exists(path).ShouldBeTrue();

        var loaded = AppCategorizer.LoadCategoryCache(path);
        loaded.Count.ShouldBe(2);
        loaded["chrome"].Category.ShouldBe("Productivity");
        loaded["mystery"].Category.ShouldBe("none");
    }

    [Fact]
    public void LoadCategoryCache_MissingOrCorrupt_YieldsEmptyCache()
    {
        AppCategorizer.LoadCategoryCache(Path.Combine(_tempDir, "does-not-exist.json")).ShouldBeEmpty();

        string corrupt = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(corrupt, "{ not json !!!");
        AppCategorizer.LoadCategoryCache(corrupt).ShouldBeEmpty();
    }

    [Fact]
    public void TryGetCachedCategory_FreshPositive_Hits()
    {
        var now = DateTime.UtcNow;
        var cache = new Dictionary<string, CategoryCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["vlc"] = new CategoryCacheEntry { Category = "Media", Timestamp = now.AddDays(-100) }
        };

        // Positive entries never go stale.
        AppCategorizer.TryGetCachedCategory(cache, "vlc", now, out var category).ShouldBeTrue();
        category.ShouldBe("Media");

        // Key lookup is case-insensitive.
        AppCategorizer.TryGetCachedCategory(cache, "VLC", now, out category).ShouldBeTrue();
        category.ShouldBe("Media");
    }

    [Fact]
    public void TryGetCachedCategory_FreshNegative_AnswersUnclassified()
    {
        var now = DateTime.UtcNow;
        var cache = new Dictionary<string, CategoryCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["mystery"] = new CategoryCacheEntry { Category = "none", Timestamp = now.AddDays(-5) }
        };

        // Fresh "none": the cache answers (no lookup needed) and the item stays unclassified.
        AppCategorizer.TryGetCachedCategory(cache, "mystery", now, out var category).ShouldBeTrue();
        category.ShouldBeNull();
    }

    [Fact]
    public void TryGetCachedCategory_StaleNegative_IsIgnored()
    {
        var now = DateTime.UtcNow;
        var cache = new Dictionary<string, CategoryCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["mystery"] = new CategoryCacheEntry { Category = "none", Timestamp = now.AddDays(-35) }
        };

        // Negative older than 30 days: retry the lookup.
        AppCategorizer.TryGetCachedCategory(cache, "mystery", now, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryGetCachedCategory_MissingKey_Misses()
    {
        var cache = new Dictionary<string, CategoryCacheEntry>(StringComparer.OrdinalIgnoreCase);
        AppCategorizer.TryGetCachedCategory(cache, "nothing", DateTime.UtcNow, out _).ShouldBeFalse();
    }

    [Fact]
    public void CacheKeyFor_PrefersExeName_FallsBackToDisplayName()
    {
        AppCategorizer.CacheKeyFor(@"C:\Apps\Contoso\ContosoApp.exe", "Contoso App").ShouldBe("contosoapp");
        AppCategorizer.CacheKeyFor(null, "Contoso App").ShouldBe("contoso app");
        AppCategorizer.CacheKeyFor("steam://rungameid/620", "My Game").ShouldBe("my game");
    }

    // ---------------------------------------------------------------
    // Package-manager response parsing (offline fixtures — no network)
    // ---------------------------------------------------------------

    [Fact]
    public void CategoryFromWingetJson_UsesTagsOfMatchingPackage()
    {
        const string json = """
        {
          "Packages": [
            { "Id": "Contoso.Widget", "Latest": { "Name": "Contoso Widget", "Tags": [ "game", "fun" ] } }
          ]
        }
        """;
        AppCategorizer.CategoryFromWingetJson(json, "contoso widget").ShouldBe("Games");
    }

    [Fact]
    public void CategoryFromWingetJson_IgnoresNonMatchingPackages()
    {
        const string json = """
        {
          "Packages": [
            { "Id": "Totally.Unrelated", "Latest": { "Name": "Totally Unrelated", "Tags": [ "game" ] } }
          ]
        }
        """;
        AppCategorizer.CategoryFromWingetJson(json, "contosoapp").ShouldBeNull();
        AppCategorizer.CategoryFromWingetJson("not json", "contosoapp").ShouldBeNull();
    }

    [Fact]
    public void CategoryFromChocolateyXml_ReadsTagElements()
    {
        const string xml = """
        <feed xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
          <entry><m:properties xmlns:m="x"><d:Tags>security antivirus scanner</d:Tags></m:properties></entry>
        </feed>
        """;
        AppCategorizer.CategoryFromChocolateyXml(xml).ShouldBe("Security Apps");
        AppCategorizer.CategoryFromChocolateyXml("<feed></feed>").ShouldBeNull();
        AppCategorizer.CategoryFromChocolateyXml("").ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Frame sizing + free-space placement (pure layout math)
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeFrameSize_SizesFromContent_AndClampsHeight()
    {
        // 4 columns wide; 1 row for up to 4 items.
        var (w1, h1) = AppCategorizer.ComputeFrameSize(3, 1000);
        var (_, h2) = AppCategorizer.ComputeFrameSize(5, 1000);   // 2 rows
        var (_, hBig) = AppCategorizer.ComputeFrameSize(100, 1000); // clamped

        w1.ShouldBe(350);           // 4 * 80 + 30 chrome
        h2.ShouldBeGreaterThan(h1); // more rows -> taller
        hBig.ShouldBe(600);         // clamped to 60% of the 1000px work area
    }

    [Fact]
    public void FindFreePosition_EmptyScreen_PicksTopLeft()
    {
        var pos = AppCategorizer.FindFreePosition(300, 200,
            Array.Empty<(double, double, double, double)>(), (0, 0, 1920, 1040));

        pos.ShouldNotBeNull();
        pos!.Value.X.ShouldBe(0);
        pos.Value.Y.ShouldBe(0);
    }

    [Fact]
    public void FindFreePosition_SkipsOccupiedSpace()
    {
        // A frame occupying the whole top band forces placement below it.
        var occupied = new List<(double X, double Y, double W, double H)> { (0, 0, 1920, 300) };
        var pos = AppCategorizer.FindFreePosition(300, 200, occupied, (0, 0, 1920, 1040), step: 24);

        pos.ShouldNotBeNull();
        pos!.Value.Y.ShouldBeGreaterThanOrEqualTo(300);

        // And the result really does not intersect the occupied rect.
        var r = occupied[0];
        bool intersects = pos.Value.X < r.X + r.W && r.X < pos.Value.X + 300 &&
                          pos.Value.Y < r.Y + r.H && r.Y < pos.Value.Y + 200;
        intersects.ShouldBeFalse();
    }

    [Fact]
    public void FindFreePosition_NothingFits_ReturnsNull()
    {
        // The screen is fully covered — the caller falls back to the cascade.
        var occupied = new List<(double X, double Y, double W, double H)> { (0, 0, 1920, 1040) };
        AppCategorizer.FindFreePosition(300, 200, occupied, (0, 0, 1920, 1040)).ShouldBeNull();

        // Frame bigger than the work area can never fit either.
        AppCategorizer.FindFreePosition(3000, 200,
            Array.Empty<(double, double, double, double)>(), (0, 0, 1920, 1040)).ShouldBeNull();
    }

    [Fact]
    public void FindFreePosition_RespectsWorkAreaOrigin()
    {
        // Taskbar on the left: work area starts at X=64.
        var pos = AppCategorizer.FindFreePosition(300, 200,
            Array.Empty<(double, double, double, double)>(), (64, 0, 1856, 1040));

        pos.ShouldNotBeNull();
        pos!.Value.X.ShouldBe(64);
    }

    // ---------------------------------------------------------------
    // Documents / Images — extension classification
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Users\u\Desktop\report.pdf", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\notes.docx", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\budget.xlsx", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\deck.pptx", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\readme.md", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\data.csv", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\book.epub", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\notebook.one", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\photo.png", "Images")]
    [InlineData(@"C:\Users\u\Desktop\photo.jpg", "Images")]
    [InlineData(@"C:\Users\u\Desktop\photo.jpeg", "Images")]
    [InlineData(@"C:\Users\u\Desktop\scan.tiff", "Images")]
    [InlineData(@"C:\Users\u\Desktop\pic.heic", "Images")]
    [InlineData(@"C:\Users\u\Desktop\logo.svg", "Images")]
    [InlineData(@"C:\Users\u\Desktop\comp.psd", "Images")]
    public void ClassifyByExtension_MapsDocumentAndImageExtensions(string path, string expected)
    {
        AppCategorizer.ClassifyByExtension(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"C:\Users\u\Desktop\REPORT.PDF", "Documents")]
    [InlineData(@"C:\Users\u\Desktop\Photo.JPG", "Images")]
    [InlineData(@"C:\Users\u\Desktop\mixed.DocX", "Documents")]
    public void ClassifyByExtension_IsCaseInsensitive(string path, string expected)
    {
        AppCategorizer.ClassifyByExtension(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"C:\Users\u\Desktop\app.lnk")]
    [InlineData(@"C:\Users\u\Desktop\site.url")]
    [InlineData(@"C:\Users\u\Desktop\setup.exe")]
    [InlineData(@"C:\Users\u\Desktop\archive.zip")]
    [InlineData(@"C:\Users\u\Desktop\video.mp4")]
    [InlineData(@"C:\Users\u\Desktop\download.crdownload")]
    [InlineData(@"C:\Users\u\Desktop\noextension")]
    [InlineData("")]
    [InlineData(null)]
    public void ClassifyByExtension_ReturnsNull_ForNonDocumentNonImage(string? path)
    {
        AppCategorizer.ClassifyByExtension(path).ShouldBeNull();
    }

    [Theory]
    [InlineData(@"C:\d\app.lnk", true)]
    [InlineData(@"C:\d\site.url", true)]
    [InlineData(@"C:\d\tool.exe", true)]
    [InlineData(@"C:\d\paper.pdf", true)]
    [InlineData(@"C:\d\cat.webp", true)]
    [InlineData(@"C:\d\movie.mkv", false)]
    [InlineData(@"C:\d\desktop.ini", false)]
    [InlineData(@"C:\d\partial.part", false)]
    public void IsCandidateFile_AcceptsAppsDocumentsAndImagesOnly(string path, bool expected)
    {
        AppCategorizer.IsCandidateFile(path).ShouldBe(expected);
    }
}
