using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// The frames of one profile: the active profile's live in-memory frames, or another
    /// profile's frames.json loaded from disk. <see cref="Save"/> persists either kind.
    /// </summary>
    public sealed class ProfileFrameSet
    {
        public required string Dir { get; init; }
        public required bool IsActive { get; init; }
        public required List<dynamic> Frames { get; init; }
        public required Action Save { get; init; }

        /// <summary>Item Filenames are absolute, or (legacy) relative to the profile folder.</summary>
        public string Resolve(string filename) =>
            Path.IsPathRooted(filename) ? Path.GetFullPath(filename) : Path.GetFullPath(Path.Combine(Dir, filename));
    }

    /// <summary>
    /// Frame data across ALL profiles. Desktop icons are shared by every profile and every
    /// virtual desktop (virtual-desktop automation switches profiles), so hiding framed desktop
    /// icons and migrating frame folders must consider every profile, not just the active one.
    /// UI thread only (the active profile's frames are UI-thread state).
    /// </summary>
    public static class AllProfileFrames
    {
        public static string ProfilesRoot => Path.Combine(AppPaths.DataRoot, "Profiles");

        public static List<ProfileFrameSet> Load()
        {
            string activeDir = Path.GetFullPath(ProfileManager.CurrentProfileDir);
            var sets = new List<ProfileFrameSet>
            {
                new() { Dir = activeDir, IsActive = true, Frames = FrameDataManager.FrameData ?? new List<dynamic>(), Save = FrameDataManager.SaveFrameData },
            };

            if (!Directory.Exists(ProfilesRoot)) return sets;
            foreach (string dir in Directory.GetDirectories(ProfilesRoot))
            {
                if (string.Equals(Path.GetFullPath(dir), activeDir, StringComparison.OrdinalIgnoreCase)) continue;
                string json = Path.Combine(dir, "frames.json");
                if (!File.Exists(json)) continue;
                try
                {
                    var token = JToken.Parse(File.ReadAllText(json));
                    var array = token as JArray ?? new JArray(token);
                    sets.Add(new ProfileFrameSet
                    {
                        Dir = Path.GetFullPath(dir),
                        IsActive = false,
                        Frames = array.OfType<JObject>().Cast<dynamic>().ToList(),
                        Save = () => AtomicFile.WriteAllText(json, array.ToString(Formatting.Indented)),
                    });
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"AllProfileFrames: cannot read '{json}': {ex.Message}");
                }
            }
            return sets;
        }

        /// <summary>A Data frame's item lists: the main Items plus every tab's Items.</summary>
        public static IEnumerable<JArray> ItemArrays(dynamic frame)
        {
            if (frame.ItemsType?.ToString() != "Data") yield break;
            if (frame.Items is JArray main) yield return main;
            if (frame.Tabs is JArray tabs)
                foreach (var tab in tabs.OfType<JObject>())
                    if (tab["Items"] is JArray items) yield return items;
        }

        /// <summary>Every real (non-spacer) item of a frame set's Data frames, with its resolved full path.</summary>
        public static IEnumerable<(JObject Item, string FullPath, dynamic Frame)> Items(ProfileFrameSet set)
        {
            foreach (dynamic frame in set.Frames)
                foreach (JArray items in ItemArrays(frame))
                    foreach (var item in items.OfType<JObject>())
                    {
                        string? name = item["Filename"]?.ToString();
                        if (string.IsNullOrEmpty(name) || name.StartsWith("INTERNAL_BLANK_", StringComparison.Ordinal)) continue;
                        string full;
                        try { full = set.Resolve(name); } catch { continue; }
                        yield return (item, full, frame);
                    }
        }
    }
}
