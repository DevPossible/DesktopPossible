using System.IO;

namespace Desktop_Frames
{
    /// <summary>
    /// Atomic text-file writes for the persistence layer (frames.json, options.json,
    /// ProfileOptions.json, auto_organize.json, restores). The content is written to a
    /// temp file first and then swapped into place, so a crash or power loss mid-write
    /// can never leave the destination truncated or half-written.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/> atomically:
        /// writes to "path.tmp", then File.Replace over the existing destination
        /// (File.Move when no destination exists yet).
        /// </summary>
        public static void WriteAllText(string path, string content)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
    }
}
