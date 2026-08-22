using System;
using System.IO;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests
{
    public class DataSafetyAtomicFileTests : IDisposable
    {
        private readonly string _tempDir;

        public DataSafetyAtomicFileTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"dfp_atomic_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void WriteAllText_NewFile_CreatesFileWithExactContent()
        {
            // Arrange
            string path = Path.Combine(_tempDir, "new.json");
            string content = "{ \"key\": \"value\" }";

            // Act
            AtomicFile.WriteAllText(path, content);

            // Assert
            File.ReadAllText(path).ShouldBe(content);
        }

        [Fact]
        public void WriteAllText_ExistingFile_ReplacesContent()
        {
            // Arrange
            string path = Path.Combine(_tempDir, "existing.json");
            File.WriteAllText(path, "old content");
            string newContent = "new content";

            // Act
            AtomicFile.WriteAllText(path, newContent);

            // Assert
            File.ReadAllText(path).ShouldBe(newContent);
        }

        [Fact]
        public void WriteAllText_NewFile_LeavesNoTmpFileBehind()
        {
            // Arrange
            string path = Path.Combine(_tempDir, "clean_new.json");

            // Act
            AtomicFile.WriteAllText(path, "content");

            // Assert
            File.Exists(path + ".tmp").ShouldBeFalse();
            Directory.GetFiles(_tempDir, "*.tmp").ShouldBeEmpty();
        }

        [Fact]
        public void WriteAllText_ExistingFile_LeavesNoTmpFileBehind()
        {
            // Arrange
            string path = Path.Combine(_tempDir, "clean_existing.json");
            File.WriteAllText(path, "old");

            // Act
            AtomicFile.WriteAllText(path, "new");

            // Assert
            File.Exists(path + ".tmp").ShouldBeFalse();
            Directory.GetFiles(_tempDir, "*.tmp").ShouldBeEmpty();
        }

        [Fact]
        public void WriteAllText_UnicodeContent_RoundTrips()
        {
            // Arrange
            string path = Path.Combine(_tempDir, "unicode.json");
            string content = "{ \"Title\": \"Ünïcodé — 日本語 🚀\" }";

            // Act - write twice: once via the create path, once via the replace path
            AtomicFile.WriteAllText(path, "seed");
            AtomicFile.WriteAllText(path, content);

            // Assert
            File.ReadAllText(path).ShouldBe(content);
        }

        [Fact]
        public void WriteAllText_RepeatedWrites_KeepLatestContent()
        {
            // Arrange
            string path = Path.Combine(_tempDir, "repeated.json");

            // Act - simulates the constant autosave traffic frames.json receives
            for (int i = 0; i < 20; i++)
            {
                AtomicFile.WriteAllText(path, $"revision {i}");
            }

            // Assert
            File.ReadAllText(path).ShouldBe("revision 19");
            Directory.GetFiles(_tempDir, "*.tmp").ShouldBeEmpty();
        }
    }
}
