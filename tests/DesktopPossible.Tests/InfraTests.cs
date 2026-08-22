using System;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    /// <summary>
    /// Headless tests for infrastructure/interop helpers: CleanPath UNC handling,
    /// tray tooltip truncation, and the hotkey modifier-string parser.
    /// </summary>
    public class InfraTests
    {
        #region CoreUtilities.CleanPath — UNC preservation

        [Theory]
        [InlineData(@"\\server\share\folder", @"\\server\share\folder")]           // UNC prefix preserved
        [InlineData(@"\\server\share", @"\\server\share")]                         // bare UNC share
        [InlineData(@"  \\server\share\file.txt  ", @"\\server\share\file.txt")]   // trimmed, UNC intact
        [InlineData(@"C:\folder\\sub\file.txt", @"C:\folder\sub\file.txt")]        // doubled separator collapsed
        [InlineData(@"\\server\share\a\\b", @"\\server\share\a\b")]                // UNC kept, inner doubles collapsed
        [InlineData(@"C:\normal\path.txt", @"C:\normal\path.txt")]                 // untouched
        [InlineData(@"\\", @"\\")]                                                 // two-char path returned as-is
        [InlineData("", "")]                                                       // empty passthrough
        [InlineData(null, null)]                                                   // null passthrough
        public void CleanPath_VariousPaths_PreservesUncAndCollapsesDoubles(string? path, string? expected)
        {
            CoreUtilities.CleanPath(path!).ShouldBe(expected);
        }

        #endregion

        #region TrayManager.TruncateTrayText — NotifyIcon 63-char cap

        [Fact]
        public void TruncateTrayText_ShortText_ReturnedUnchanged()
        {
            TrayManager.TruncateTrayText("DesktopPossible (Default)").ShouldBe("DesktopPossible (Default)");
        }

        [Fact]
        public void TruncateTrayText_ExactlyMaxLength_ReturnedUnchanged()
        {
            string text = new string('x', 63);
            TrayManager.TruncateTrayText(text).ShouldBe(text);
        }

        [Fact]
        public void TruncateTrayText_LongProfileName_TruncatedTo63()
        {
            string longName = "DesktopPossible (" + new string('p', 80) + ")";
            string result = TrayManager.TruncateTrayText(longName);

            result.Length.ShouldBe(63);
            result.ShouldBe(longName.Substring(0, 63));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TruncateTrayText_NullOrEmpty_Passthrough(string? text)
        {
            TrayManager.TruncateTrayText(text!).ShouldBe(text);
        }

        [Fact]
        public void TruncateTrayText_CustomMaxLength_Respected()
        {
            TrayManager.TruncateTrayText("abcdef", 3).ShouldBe("abc");
        }

        #endregion

        #region GlobalHotkeyManager.ParseModifierMask — Ctrl=1, Alt=2, Shift=4, Win=8

        [Theory]
        [InlineData("Control", 1)]
        [InlineData("Ctrl", 1)]
        [InlineData("Alt", 2)]
        [InlineData("Shift", 4)]
        [InlineData("Win", 8)]
        [InlineData("Control, Alt", 3)]
        [InlineData("Control, Alt, Shift", 7)]
        [InlineData("Control, Alt, Shift, Win", 15)]
        [InlineData("ctrl,shift", 5)]
        [InlineData("CONTROL", 1)]           // case-insensitive
        [InlineData("shift", 4)]             // lowercase
        [InlineData("None", 0)]              // no known modifier tokens -> 0
        [InlineData("garbage", 0)]           // unknown text -> 0
        public void ParseModifierMask_VariousStrings_ReturnsExpectedMask(string modifiers, int expected)
        {
            GlobalHotkeyManager.ParseModifierMask(modifiers).ShouldBe(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseModifierMask_NullOrWhitespace_ReturnsMinusOne(string? modifiers)
        {
            GlobalHotkeyManager.ParseModifierMask(modifiers!).ShouldBe(-1);
        }

        [Fact]
        public void ParseModifierMask_IsCultureInvariant()
        {
            // Turkish 'İ'/'ı' casing must not break matching ("SHIFT".ToLower() -> "shıft" in tr-TR).
            var original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                GlobalHotkeyManager.ParseModifierMask("SHIFT, WIN").ShouldBe(12);
                GlobalHotkeyManager.ParseModifierMask("Control, SHIFT").ShouldBe(5);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        #endregion
    }
}
