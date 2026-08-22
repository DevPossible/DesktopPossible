using System;
using System.IO;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    public class CoreUtilitiesTests
    {
        #region IsTemporaryFile

        [Theory]
        [InlineData(@"C:\docs\~$Budget.xlsx", true)]        // Excel temp
        [InlineData(@"C:\docs\~WRL0001.tmp", true)]         // Word temp
        [InlineData(@"C:\docs\~PPT1234.tmp", true)]         // PowerPoint temp
        [InlineData(@"C:\docs\report.tmp", true)]           // .tmp extension
        [InlineData(@"C:\docs\notes.temp", true)]           // .temp extension
        [InlineData(@"C:\docs\Thumbs.db", true)]            // system file
        [InlineData(@"C:\docs\desktop.ini", true)]          // system file
        [InlineData(@"C:\docs\.DS_Store", true)]            // macOS system file
        [InlineData(@"C:\docs\Budget.xlsx", false)]         // regular document
        [InlineData(@"C:\docs\report.docx", false)]         // regular document
        [InlineData("", true)]                              // empty path is filtered
        [InlineData(null, true)]                            // null path is filtered
        public void IsTemporaryFile_VariousPaths_DetectsTemporaryFiles(string? path, bool expected)
        {
            // Act
            bool result = CoreUtilities.IsTemporaryFile(path!);

            // Assert
            result.ShouldBe(expected);
        }

        #endregion

        #region Extension helpers

        [Theory]
        [InlineData(@"C:\apps\tool.EXE", ".exe", true)]     // case-insensitive
        [InlineData(@"C:\apps\tool.exe", ".EXE", true)]     // case-insensitive both ways
        [InlineData(@"C:\apps\tool.exe", ".lnk", false)]
        [InlineData(@"C:\apps\noextension", ".exe", false)]
        [InlineData("", ".exe", false)]
        [InlineData(@"C:\apps\tool.exe", "", false)]
        public void HasExtension_VariousInputs_MatchesCaseInsensitively(string path, string extension, bool expected)
        {
            // Act
            bool result = CoreUtilities.HasExtension(path, extension);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData(@"C:\links\app.lnk", true)]
        [InlineData(@"C:\links\site.URL", true)]            // case-insensitive
        [InlineData(@"C:\links\app.exe", false)]
        [InlineData(@"C:\links\readme.txt", false)]
        public void IsShortcutFile_VariousPaths_DetectsLnkAndUrl(string path, bool expected)
        {
            // Act
            bool result = CoreUtilities.IsShortcutFile(path);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData(@"C:\apps\setup.exe", true)]
        [InlineData(@"C:\apps\run.BAT", true)]
        [InlineData(@"C:\apps\install.msi", true)]
        [InlineData(@"C:\apps\saver.scr", true)]
        [InlineData(@"C:\apps\readme.txt", false)]
        [InlineData(@"C:\apps\app.lnk", false)]
        public void IsExecutableFile_VariousPaths_DetectsExecutables(string path, bool expected)
        {
            // Act
            bool result = CoreUtilities.IsExecutableFile(path);

            // Assert
            result.ShouldBe(expected);
        }

        #endregion

        #region String validation

        [Theory]
        [InlineData("frame name", true)]
        [InlineData("   ", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidString_VariousInputs_RejectsBlankValues(string? input, bool expected)
        {
            // Act
            bool result = CoreUtilities.IsValidString(input!);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData(" ab ", 2, true)]    // trimmed length exactly at minimum
        [InlineData(" ab ", 3, false)]   // trimmed length below minimum
        [InlineData("abcd", 3, true)]
        [InlineData("", 1, false)]
        public void IsValidString_WithMinLength_UsesTrimmedLength(string input, int minLength, bool expected)
        {
            // Act
            bool result = CoreUtilities.IsValidString(input, minLength);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData("My Frame 2", true)]
        [InlineData("bad|name", false)]
        [InlineData("a<b", false)]
        [InlineData("time: now", false)]
        [InlineData(null, false)]
        [InlineData("  ", false)]
        public void IsValidName_VariousNames_RejectsInvalidFileSystemChars(string? name, bool expected)
        {
            // Act
            bool result = CoreUtilities.IsValidName(name!);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData(@"C:\Users\me\file.txt", false)]
        [InlineData(@"C:\Пользователи\файл.txt", true)]
        [InlineData(@"C:\用户\文档.txt", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ContainsUnicodeCharacters_VariousPaths_DetectsNonAscii(string? path, bool expected)
        {
            // Act
            bool containsUnicode = CoreUtilities.ContainsUnicodeCharacters(path!);
            bool isAscii = CoreUtilities.IsAsciiPath(path!);

            // Assert - IsAsciiPath is the exact inverse for non-empty paths,
            // and both treat null/empty as "ascii, no unicode".
            containsUnicode.ShouldBe(expected);
            isAscii.ShouldBe(!expected);
        }

        #endregion

        #region Display name / path processing

        [Fact]
        public void TruncateDisplayName_LongerThanMax_AppendsEllipsis()
        {
            // Arrange
            string name = "VeryLongDisplayName";

            // Act
            string result = CoreUtilities.TruncateDisplayName(name, 8);

            // Assert
            result.ShouldBe("VeryLong...");
        }

        [Theory]
        [InlineData("Short", 10, "Short")]      // shorter than max: unchanged
        [InlineData("Exactly10!", 10, "Exactly10!")] // boundary: exactly max is unchanged
        [InlineData("", 5, "")]
        [InlineData(null, 5, null)]
        public void TruncateDisplayName_WithinLimit_ReturnsUnchanged(string? name, int max, string? expected)
        {
            // Act
            string result = CoreUtilities.TruncateDisplayName(name!, max);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData("  C:\\Users\\me  ", "C:\\Users\\me")]        // trims whitespace
        [InlineData("C:\\\\Temp\\\\file.txt", "C:\\Temp\\file.txt")] // collapses doubled backslashes
        [InlineData("", "")]
        [InlineData(null, null)]
        public void CleanPath_VariousPaths_TrimsAndCollapsesBackslashes(string? path, string? expected)
        {
            // Act
            string result = CoreUtilities.CleanPath(path!);

            // Assert
            result.ShouldBe(expected);
        }

        #endregion

        #region Icon sizing / spacing

        [Theory]
        [InlineData("Tiny", 16)]
        [InlineData("Small", 24)]
        [InlineData("Medium", 32)]
        [InlineData("Large", 48)]
        [InlineData("Huge", 64)]
        [InlineData("Gigantic", 32)]  // unknown falls back to medium
        [InlineData(null, 32)]        // null falls back to medium
        public void GetIconSizePixels_VariousNames_MapsToPixels(string? sizeName, int expected)
        {
            // Act
            int result = CoreUtilities.GetIconSizePixels(sizeName!);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData(null, 5)]     // null -> default spacing
        [InlineData("12", 12)]    // parses string values
        [InlineData(100, 50)]     // clamped to upper bound
        [InlineData(-3, 0)]       // clamped to lower bound
        [InlineData("abc", 5)]    // unparsable -> default spacing
        public void ValidateIconSpacing_VariousValues_ClampsAndDefaults(object? value, int expected)
        {
            // Act
            int result = CoreUtilities.ValidateIconSpacing(value!);

            // Assert
            result.ShouldBe(expected);
        }

        #endregion

        #region Configuration validation

        [Theory]
        [InlineData("#123456", "#123456")]      // 7-char hex accepted
        [InlineData("#123456AB", "#123456AB")]  // 9-char hex (with alpha) accepted
        [InlineData("#12345", "#FF1E1E1E")]     // wrong length -> default
        [InlineData("red", "#FF1E1E1E")]        // named color -> default
        [InlineData("", "#FF1E1E1E")]           // empty -> default
        [InlineData(null, "#FF1E1E1E")]         // null -> default
        public void ValidateframeColor_VariousValues_FallsBackToDefault(string? color, string expected)
        {
            // Act
            string result = CoreUtilities.ValidateframeColor(color!);

            // Assert
            result.ShouldBe(expected);
        }

        [Fact]
        public void ValidateframeColor_CustomDefault_UsedWhenInvalid()
        {
            // Act
            string result = CoreUtilities.ValidateframeColor("not-a-color", "#FF000000");

            // Assert
            result.ShouldBe("#FF000000");
        }

        [Theory]
        [InlineData(null, true, true)]      // null -> supplied default
        [InlineData(null, false, false)]
        [InlineData(true, false, true)]     // bool passthrough wins over default
        [InlineData("TRUE", false, true)]   // string parsed case-insensitively
        [InlineData("false", true, false)]
        [InlineData("junk", true, true)]    // unparsable -> default
        public void ValidateBooleanSetting_VariousValues_ParsesOrDefaults(object? value, bool defaultValue, bool expected)
        {
            // Act
            bool result = CoreUtilities.ValidateBooleanSetting(value!, defaultValue);

            // Assert
            result.ShouldBe(expected);
        }

        [Theory]
        [InlineData(null, 7, 0, 100, 7)]      // null -> default
        [InlineData("42", 7, 0, 100, 42)]     // parses string
        [InlineData(250, 7, 0, 100, 100)]     // clamped to max
        [InlineData(-10, 7, 0, 100, 0)]       // clamped to min
        [InlineData("oops", 7, 0, 100, 7)]    // unparsable -> default
        public void ValidateIntegerSetting_VariousValues_ClampsAndDefaults(object? value, int defaultValue, int min, int max, int expected)
        {
            // Act
            int result = CoreUtilities.ValidateIntegerSetting(value!, defaultValue, min, max);

            // Assert
            result.ShouldBe(expected);
        }

        #endregion

        #region Tooltip / naming

        [Fact]
        public void CreateTooltipText_TargetSameAsFile_OmitsTargetLine()
        {
            // Arrange
            string path = @"C:\frames\app.lnk";

            // Act
            string result = CoreUtilities.CreateTooltipText(path, targetPath: path);

            // Assert
            result.ShouldBe("File: app.lnk");
        }

        [Fact]
        public void CreateTooltipText_DistinctTargetAndArguments_IncludesAllLines()
        {
            // Act
            string result = CoreUtilities.CreateTooltipText(
                @"C:\frames\app.lnk", @"C:\Program Files\App\app.exe", "--fast");

            // Assert
            result.ShouldBe("File: app.lnk\nTarget: C:\\Program Files\\App\\app.exe\nParameters: --fast");
        }

        [Fact]
        public void GenerateRandomFrameName_Always_ReturnsAdjectiveSpacePlace()
        {
            // Act - generate several names; every one must be two non-empty words
            for (int i = 0; i < 20; i++)
            {
                string name = CoreUtilities.GenerateRandomFrameName();

                // Assert
                name.ShouldNotBeNullOrWhiteSpace();
                string[] parts = name.Split(' ');
                parts.Length.ShouldBe(2);
                parts[0].ShouldNotBeNullOrWhiteSpace();
                parts[1].ShouldNotBeNullOrWhiteSpace();
            }
        }

        #endregion

        #region File system probes (temp-dir only)

        [Fact]
        public void SafeFileExists_ExistingAndMissingFiles_ReturnsCorrectResult()
        {
            // Arrange
            string file = Path.Combine(Path.GetTempPath(), $"dfp_test_{Guid.NewGuid():N}.txt");
            File.WriteAllText(file, "x");

            try
            {
                // Act & Assert
                CoreUtilities.SafeFileExists(file).ShouldBeTrue();
                CoreUtilities.SafeFileExists(file + ".missing").ShouldBeFalse();
                CoreUtilities.SafeFileExists(null!).ShouldBeFalse();
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void SafeDirectoryExists_ExistingAndMissingDirectories_ReturnsCorrectResult()
        {
            // Arrange
            string dir = Path.Combine(Path.GetTempPath(), $"dfp_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            try
            {
                // Act & Assert
                CoreUtilities.SafeDirectoryExists(dir).ShouldBeTrue();
                CoreUtilities.SafeDirectoryExists(dir + "_missing").ShouldBeFalse();
            }
            finally
            {
                Directory.Delete(dir);
            }
        }

        [Fact]
        public void ExtractUrlFromFile_UrlFileWithUrlLine_ReturnsTrimmedUrl()
        {
            // Arrange
            string file = Path.Combine(Path.GetTempPath(), $"dfp_test_{Guid.NewGuid():N}.url");
            File.WriteAllLines(file, new[]
            {
                "[InternetShortcut]",
                "url=https://example.com/page  ", // lowercase key + trailing spaces
                "IconIndex=0"
            });

            try
            {
                // Act
                string? result = CoreUtilities.ExtractUrlFromFile(file);

                // Assert - key match is case-insensitive and the value is trimmed
                result.ShouldBe("https://example.com/page");
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void ExtractUrlFromFile_NoUrlLineOrMissingFile_ReturnsNull()
        {
            // Arrange
            string file = Path.Combine(Path.GetTempPath(), $"dfp_test_{Guid.NewGuid():N}.url");
            File.WriteAllLines(file, new[] { "[InternetShortcut]", "IconIndex=0" });

            try
            {
                // Act & Assert
                CoreUtilities.ExtractUrlFromFile(file).ShouldBeNull();
                CoreUtilities.ExtractUrlFromFile(file + ".missing").ShouldBeNull();
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void IsWebLinkShortcut_UrlFiles_DetectsWebSchemesOnly()
        {
            // Arrange
            string webFile = Path.Combine(Path.GetTempPath(), $"dfp_test_{Guid.NewGuid():N}.url");
            string fileScheme = Path.Combine(Path.GetTempPath(), $"dfp_test_{Guid.NewGuid():N}.url");
            File.WriteAllText(webFile, "[InternetShortcut]\r\nURL=https://example.com\r\n");
            File.WriteAllText(fileScheme, "[InternetShortcut]\r\nURL=file:///C:/local.txt\r\n");

            try
            {
                // Act & Assert
                CoreUtilities.IsWebLinkShortcut(webFile).ShouldBeTrue();
                CoreUtilities.IsWebLinkShortcut(fileScheme).ShouldBeFalse();        // non-web scheme
                CoreUtilities.IsWebLinkShortcut(webFile + ".missing.url").ShouldBeFalse(); // missing file
                CoreUtilities.IsWebLinkShortcut(@"C:\docs\readme.txt").ShouldBeFalse();    // not a shortcut type
            }
            finally
            {
                File.Delete(webFile);
                File.Delete(fileScheme);
            }
        }

        #endregion
    }
}
