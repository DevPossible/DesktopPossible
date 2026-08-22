using System;
using System.IO;
using System.Windows.Media;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    /// <summary>
    /// Regression tests for the "dropped desktop shortcut shows a different icon" bug.
    /// These create real temp .lnk files via WScript.Shell COM (Windows-only, like the
    /// production code path) and exercise IconManager.TryGetShortcutCustomIcon, which
    /// resolves a shortcut's custom IconLocation the way Explorer does.
    /// </summary>
    public class IconManagerShortcutIconTests : IDisposable
    {
        private readonly string _tempDir;

        public IconManagerShortcutIconTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DFP_IconTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>Creates a real .lnk on disk using the same COM path production uses.</summary>
        private string CreateShortcut(string name, string targetPath, string? iconLocation)
        {
            string lnkPath = Path.Combine(_tempDir, name);
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            if (iconLocation != null) shortcut.IconLocation = iconLocation;
            shortcut.Save();
            return lnkPath;
        }

        private static string NotepadPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        [Fact]
        public void TryGetShortcutCustomIcon_NoCustomIconLocation_ReturnsNull()
        {
            // Arrange - a plain shortcut; WScript stores IconLocation as ",0" (use target icon)
            string lnk = CreateShortcut("plain.lnk", NotepadPath, iconLocation: null);

            // Act
            ImageSource? result = IconManager.TryGetShortcutCustomIcon(lnk, NotepadPath);

            // Assert - no custom icon declared, caller falls back to the target's icon
            result.ShouldBeNull();
        }

        [Fact]
        public void TryGetShortcutCustomIcon_AbsoluteIconLocation_ReturnsIcon()
        {
            // Arrange - explicit custom icon: shell32.dll icon index 3 (stock folder icon)
            string shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
            string lnk = CreateShortcut("custom.lnk", NotepadPath, $"{shell32},3");

            // Act
            ImageSource? result = IconManager.TryGetShortcutCustomIcon(lnk, NotepadPath);

            // Assert - the declared custom icon must be resolved, not the notepad target icon
            result.ShouldNotBeNull();
        }

        [Fact]
        public void TryGetShortcutCustomIcon_EnvironmentVariableIconPath_ReturnsIcon()
        {
            // Arrange - Explorer-style unexpanded env-var icon path. The old naive
            // File.Exists("%SystemRoot%\\...") check failed silently and the frame fell
            // back to the target's icon - the reported "different icon" bug.
            string lnk = CreateShortcut("envvar.lnk", NotepadPath, @"%SystemRoot%\System32\shell32.dll,3");

            // Act
            ImageSource? result = IconManager.TryGetShortcutCustomIcon(lnk, NotepadPath);

            // Assert
            result.ShouldNotBeNull();
        }

        [Fact]
        public void TryGetShortcutCustomIcon_RelativeIconPath_ReturnsIcon()
        {
            // Arrange - icon path relative to the shortcut's own folder (Explorer resolves
            // these against the .lnk directory, not the process working directory)
            string localExe = Path.Combine(_tempDir, "iconsource.exe");
            File.Copy(NotepadPath, localExe);
            string lnk = CreateShortcut("relative.lnk", NotepadPath, "iconsource.exe,0");

            // Act
            ImageSource? result = IconManager.TryGetShortcutCustomIcon(lnk, NotepadPath);

            // Assert
            result.ShouldNotBeNull();
        }

        [Fact]
        public void TryGetShortcutCustomIcon_NegativeResourceIdIndex_ReturnsIcon()
        {
            // Arrange - negative index means "resource ID", used by many installed apps
            string shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
            string lnk = CreateShortcut("resid.lnk", NotepadPath, $"{shell32},-4");

            // Act
            ImageSource? result = IconManager.TryGetShortcutCustomIcon(lnk, NotepadPath);

            // Assert
            result.ShouldNotBeNull();
        }
    }
}
