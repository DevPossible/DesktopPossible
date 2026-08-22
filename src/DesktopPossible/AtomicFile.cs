using System.IO;
using System.Threading;

namespace Desktop_Frames
{
    /// <summary>
    /// Atomic text-file writes for the persistence layer (frames.json, options.json,
    /// ProfileOptions.json, category_cache.json, restores). The content is written to a
    /// temp file first and then swapped into place, so a crash or power loss mid-write
    /// can never leave the destination truncated or half-written.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/> atomically:
        /// writes to "path.tmp", then swaps it over the destination.
        ///
        /// File.Replace needs EXCLUSIVE access to the destination, and transient
        /// readers (our own auto-backup copying the file, antivirus, the search
        /// indexer) briefly hold it open — so the swap retries with backoff and
        /// finally falls back to File.Move(overwrite). If every attempt fails the
        /// temp file is cleaned up and the exception propagates with the previous
        /// destination content still intact — callers log and carry on; the next
        /// save simply retries.
        /// </summary>
        public static void WriteAllText(string path, string content)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content);

            try
            {
                const int attempts = 4;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            if (attempt < attempts)
                            {
                                File.Replace(tempPath, path, null);
                            }
                            else
                            {
                                // Last attempt: Move(overwrite) tolerates some sharing
                                // states Replace does not (still an atomic rename on NTFS).
                                File.Move(tempPath, path, overwrite: true);
                            }
                        }
                        else
                        {
                            File.Move(tempPath, path);
                        }
                        return;
                    }
                    catch (IOException) when (attempt < attempts)
                    {
                        Thread.Sleep(30 * attempt); // 30/60/90ms — outlasts a backup copy or AV peek
                    }
                }
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }
}
