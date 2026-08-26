using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    public class UtilityTests
    {
        #region FormatFileSize

        [Theory]
        [InlineData(-1, "")]                        // negative sizes render as empty
        [InlineData(0, "0 bytes")]
        [InlineData(1023, "1023 bytes")]            // boundary: last raw-bytes value
        [InlineData(1024, "1 KB")]                  // boundary: first KB value
        [InlineData(1025, "2 KB")]                  // Explorer-style: KB rounds UP
        [InlineData(1048575, "1,024 KB")]           // boundary: last KB value (ceil to 1024)
        [InlineData(1048576, "1.0 MB")]             // boundary: first MB value
        [InlineData(1572864, "1.5 MB")]             // MB shown with one decimal
        [InlineData(1073741824, "1.0 GB")]          // boundary: first GB value
        [InlineData(1610612736, "1.5 GB")]          // GB shown with one decimal
        public void FormatFileSize_VariousByteCounts_FormatsLikeExplorer(long bytes, string expected)
        {
            // Arrange - pin the culture so "N0"/"N1" formatting is deterministic
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            try
            {
                // Act
                string result = Utility.FormatFileSize(bytes);

                // Assert
                result.ShouldBe(expected);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        #endregion

        #region Shortcut helpers (non-COM paths only)

        [Fact]
        public void GetShortcutTarget_NonLnkPath_ReturnsPathUnchanged()
        {
            // Arrange - anything that is not a .lnk is treated as its own target
            string path = @"C:\somewhere\document.pdf";

            // Act
            string? result = Utility.GetShortcutTarget(path);

            // Assert
            result.ShouldBe(path);
        }

        [Fact]
        public void GetShortcutArguments_NonLnkPath_ReturnsNull()
        {
            // Act
            string? result = Utility.GetShortcutArguments(@"C:\somewhere\tool.exe");

            // Assert
            result.ShouldBeNull();
        }

        #endregion

        #region IsStoreAppShortcut

        [Fact]
        public void IsStoreAppShortcut_FileContainingAppsSignature_ReturnsTrue()
        {
            // Arrange - the detector scans the first 4096 bytes for the ASCII "APPS" marker
            string file = Path.Combine(Path.GetTempPath(), $"dfp_store_{Guid.NewGuid():N}.lnk");
            File.WriteAllBytes(file, Encoding.ASCII.GetBytes("L\0\0\0somethingAPPSsomething"));

            try
            {
                // Act & Assert
                Utility.IsStoreAppShortcut(file).ShouldBeTrue();
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void IsStoreAppShortcut_FileWithoutSignatureOrMissingFile_ReturnsFalse()
        {
            // Arrange
            string file = Path.Combine(Path.GetTempPath(), $"dfp_store_{Guid.NewGuid():N}.lnk");
            File.WriteAllBytes(file, Encoding.ASCII.GetBytes("L\0\0\0just a regular shortcut body"));

            try
            {
                // Act & Assert
                Utility.IsStoreAppShortcut(file).ShouldBeFalse();
                Utility.IsStoreAppShortcut(file + ".missing").ShouldBeFalse();
            }
            finally
            {
                File.Delete(file);
            }
        }

        #endregion

        #region GetColorFromName

        [Theory]
        [InlineData("Red", 0x9E, 0x05, 0x2E)]
        [InlineData("Teal", 0x00, 0x80, 0x80)]
        [InlineData("Gray", 0x6E, 0x6E, 0x6E)]
        [InlineData("Black", 0x0B, 0x0B, 0x0C)]
        public void GetColorFromName_KnownNames_ReturnsPaletteColor(string name, byte r, byte g, byte b)
        {
            // Arrange - Chameleon mode (default on) replaces default-color lookups with the
            // wallpaper color; pin it off to test the palette mapping itself.
            bool chameleon = SettingsManager.EnableChameleonMode;
            SettingsManager.EnableChameleonMode = false;
            try
            {
                // Act
                Color result = Utility.GetColorFromName(name);

                // Assert
                result.ShouldBe(Color.FromRgb(r, g, b));
            }
            finally
            {
                SettingsManager.EnableChameleonMode = chameleon;
            }
        }

        [Theory]
        [InlineData("NotAColor")]
        [InlineData("")]
        [InlineData(null)]
        public void GetColorFromName_UnknownOrMissingName_ReturnsTransparent(string? name)
        {
            // Arrange - see GetColorFromName_KnownNames_ReturnsPaletteColor
            bool chameleon = SettingsManager.EnableChameleonMode;
            SettingsManager.EnableChameleonMode = false;
            try
            {
                // Act
                Color result = Utility.GetColorFromName(name!);

                // Assert
                result.ShouldBe(Colors.Transparent);
            }
            finally
            {
                SettingsManager.EnableChameleonMode = chameleon;
            }
        }

        #endregion
    }
}
