using System;
using System.IO;
using System.Linq;
using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopFramesPossible.Tests
{
    public class FilePathUtilitiesTests : IDisposable
    {
        private readonly string _tempDir;

        public FilePathUtilitiesTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"dfp_fpu_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        #region GetShortcutTargetUnicodeSafe

        [Fact]
        public void GetShortcutTargetUnicodeSafe_MissingShortcutFile_ReturnsEmpty()
        {
            // Arrange
            string missing = Path.Combine(_tempDir, "does_not_exist.lnk");

            // Act
            string result = FilePathUtilities.GetShortcutTargetUnicodeSafe(missing);

            // Assert
            result.ShouldBe(string.Empty);
        }

        #endregion

        #region DoesFolderExist

        [Fact]
        public void DoesFolderExist_DirectFolderPath_ChecksDirectoryExistence()
        {
            // Act & Assert - a real directory passed directly (not a .lnk) is checked as-is
            FilePathUtilities.DoesFolderExist(_tempDir, isFolder: true).ShouldBeTrue();
            FilePathUtilities.DoesFolderExist(Path.Combine(_tempDir, "missing_sub"), isFolder: true).ShouldBeFalse();
        }

        [Fact]
        public void DoesFolderExist_IsFolderFalse_ReturnsFalseEvenForExistingFolder()
        {
            // Act
            bool result = FilePathUtilities.DoesFolderExist(_tempDir, isFolder: false);

            // Assert
            result.ShouldBeFalse();
        }

        #endregion

        #region ValidateUnicodeFolderPath

        [Fact]
        public void ValidateUnicodeFolderPath_ExistingUnicodeFolder_ReturnsNormalizedPath()
        {
            // Arrange - folder with non-ASCII characters in its name
            string unicodeDir = Path.Combine(_tempDir, "Ünïcode_测试_フォルダ");
            Directory.CreateDirectory(unicodeDir);

            // Act
            bool result = FilePathUtilities.ValidateUnicodeFolderPath(unicodeDir, out string sanitized);

            // Assert
            result.ShouldBeTrue();
            sanitized.ShouldBe(Path.GetFullPath(unicodeDir));
        }

        [Fact]
        public void ValidateUnicodeFolderPath_MissingFolder_ReturnsFalseAndKeepsOriginalPath()
        {
            // Arrange
            string missing = Path.Combine(_tempDir, "nope_不存在");

            // Act
            bool result = FilePathUtilities.ValidateUnicodeFolderPath(missing, out string sanitized);

            // Assert
            result.ShouldBeFalse();
            sanitized.ShouldBe(missing);
        }

        #endregion

        #region ExtractUrlFromFileAdvanced

        [Fact]
        public void ExtractUrlFromFileAdvanced_ValidHttpsUrl_ReturnsUrl()
        {
            // Arrange
            string file = Path.Combine(_tempDir, "site.url");
            File.WriteAllLines(file, new[] { "[InternetShortcut]", "URL=https://example.com/path?q=1" });

            // Act
            string? result = FilePathUtilities.ExtractUrlFromFileAdvanced(file);

            // Assert
            result.ShouldBe("https://example.com/path?q=1");
        }

        [Fact]
        public void ExtractUrlFromFileAdvanced_NonWebUrl_StillReturnsValueForCallerToDecide()
        {
            // Arrange - a URL= value that fails http/https validation is returned anyway
            string file = Path.Combine(_tempDir, "weird.url");
            File.WriteAllLines(file, new[] { "[InternetShortcut]", "URL=notaurl" });

            // Act
            string? result = FilePathUtilities.ExtractUrlFromFileAdvanced(file);

            // Assert
            result.ShouldBe("notaurl");
        }

        [Fact]
        public void ExtractUrlFromFileAdvanced_NoUrlLineOrMissingFile_ReturnsNull()
        {
            // Arrange
            string file = Path.Combine(_tempDir, "empty.url");
            File.WriteAllLines(file, new[] { "[InternetShortcut]" });

            // Act & Assert
            FilePathUtilities.ExtractUrlFromFileAdvanced(file).ShouldBeNull();
            FilePathUtilities.ExtractUrlFromFileAdvanced(Path.Combine(_tempDir, "missing.url")).ShouldBeNull();
        }

        #endregion

        #region ClearDeadShortcutsFromFrame

        private static JObject MakeItem(string filename, bool isLink = false, bool isFolder = false) => new JObject
        {
            ["Filename"] = filename,
            ["IsLink"] = isLink,
            ["IsFolder"] = isFolder
        };

        [Fact]
        public void ClearDeadShortcutsFromFrame_NonDataFrame_ReturnsZeroWithoutTouchingItems()
        {
            // Arrange - Portal frames are excluded from dead-shortcut cleanup
            dynamic frame = new JObject
            {
                ["Title"] = "My Portal",
                ["ItemsType"] = "Portal",
                ["Items"] = new JArray(MakeItem(@"C:\definitely\missing\ghost.txt"))
            };

            // Act
            int removed = FilePathUtilities.ClearDeadShortcutsFromFrame(frame);

            // Assert
            removed.ShouldBe(0);
            ((JArray)frame.Items).Count.ShouldBe(1);
        }

        [Fact]
        public void ClearDeadShortcutsFromFrame_DataFrame_RemovesDeadKeepsLiveAndWebLinks()
        {
            // Arrange
            string liveFile = Path.Combine(_tempDir, "alive.txt");
            File.WriteAllText(liveFile, "x");
            string deadFile = Path.Combine(_tempDir, "ghost.txt"); // never created

            dynamic frame = new JObject
            {
                ["Title"] = "Data Frame",
                ["ItemsType"] = "Data",
                ["TabsEnabled"] = "false",
                ["Items"] = new JArray(
                    MakeItem(liveFile),
                    MakeItem(deadFile),
                    MakeItem("https://example.com", isLink: true))
            };

            // Act
            int removed = FilePathUtilities.ClearDeadShortcutsFromFrame(frame);

            // Assert - only the dead local file is removed; web links are never validated
            removed.ShouldBe(1);
            var remaining = ((JArray)frame.Items).Select(i => i["Filename"]!.ToString()).ToList();
            remaining.ShouldBe(new[] { liveFile, "https://example.com" });
        }

        [Fact]
        public void ClearDeadShortcutsFromFrame_ItemWithEmptyFilename_IsRemoved()
        {
            // Arrange
            dynamic frame = new JObject
            {
                ["Title"] = "Data Frame",
                ["ItemsType"] = "Data",
                ["TabsEnabled"] = "false",
                ["Items"] = new JArray(MakeItem(string.Empty))
            };

            // Act
            int removed = FilePathUtilities.ClearDeadShortcutsFromFrame(frame);

            // Assert
            removed.ShouldBe(1);
            ((JArray)frame.Items).Count.ShouldBe(0);
        }

        [Fact]
        public void ClearDeadShortcutsFromFrame_TabbedFrame_CleansEveryTab()
        {
            // Arrange
            string liveFile = Path.Combine(_tempDir, "tab_alive.txt");
            File.WriteAllText(liveFile, "x");

            dynamic frame = new JObject
            {
                ["Title"] = "Tabbed Frame",
                ["ItemsType"] = "Data",
                ["TabsEnabled"] = "true",
                ["Tabs"] = new JArray(
                    new JObject { ["Name"] = "Tab1", ["Items"] = new JArray(MakeItem(Path.Combine(_tempDir, "dead1.txt"))) },
                    new JObject { ["Name"] = "Tab2", ["Items"] = new JArray(MakeItem(Path.Combine(_tempDir, "dead2.txt")), MakeItem(liveFile)) })
            };

            // Act
            int removed = FilePathUtilities.ClearDeadShortcutsFromFrame(frame);

            // Assert - one dead item removed from each tab, live item survives
            removed.ShouldBe(2);
            var tabs = (JArray)frame.Tabs;
            ((JArray)tabs[0]["Items"]!).Count.ShouldBe(0);
            ((JArray)tabs[1]["Items"]!).Select(i => i["Filename"]!.ToString()).ShouldBe(new[] { liveFile });
        }

        #endregion
    }
}
