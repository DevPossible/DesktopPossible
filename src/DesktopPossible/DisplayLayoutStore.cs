using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Desktop_Frames
{
    /// <summary>One remembered display configuration and the frame geometry that goes with it.</summary>
    public sealed class LayoutConfig
    {
        public string Fingerprint { get; set; } = "";
        public string Label { get; set; } = "";
        public DateTime LastSeenUtc { get; set; }
        public List<MonitorInfo> Monitors { get; set; } = new List<MonitorInfo>();

        /// <summary>Frame Id -> geometry, in device-independent units.</summary>
        public Dictionary<string, FrameRect> Frames { get; set; } = new Dictionary<string, FrameRect>();
    }

    /// <summary>
    /// The per-profile layouts.json sidecar: every display configuration the profile has been
    /// seen on, and where its frames sat on each.
    ///
    /// This file is a CACHE, never the source of truth for what a frame IS — frames.json still
    /// owns that, including its live X/Y/Width/Height. Deleting layouts.json costs nothing: the
    /// next load re-seeds it from the frames themselves. That is what makes the whole feature
    /// safe to add without touching the frames.json schema.
    /// </summary>
    public sealed class LayoutStore
    {
        public int Version { get; set; } = 1;

        /// <summary>Fingerprint of the configuration the frames are currently laid out for.</summary>
        public string ActiveFingerprint { get; set; } = "";

        public List<LayoutConfig> Configs { get; set; } = new List<LayoutConfig>();

        /// <summary>Content of the last successful save, so an unchanged store never hits the disk.</summary>
        [JsonIgnore]
        private string _lastWritten = "";

        public LayoutConfig? Find(string? fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return null;
            return Configs.FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.Ordinal));
        }

        /// <summary>
        /// The configuration to remap FROM when the current one is unknown: the one that was
        /// active a moment ago, else the most recently seen other configuration. Never the
        /// configuration being built (<paramref name="excluding"/>).
        /// </summary>
        public LayoutConfig? MostRecentOther(string excluding)
        {
            var active = Find(ActiveFingerprint);
            if (active != null && !string.Equals(active.Fingerprint, excluding, StringComparison.Ordinal)) return active;

            return Configs
                .Where(c => !string.Equals(c.Fingerprint, excluding, StringComparison.Ordinal))
                .OrderByDescending(c => c.LastSeenUtc)
                .FirstOrDefault();
        }

        public LayoutConfig GetOrAdd(string fingerprint, IReadOnlyList<MonitorInfo> monitors)
        {
            var existing = Find(fingerprint);
            if (existing != null) return existing;

            var created = new LayoutConfig
            {
                Fingerprint = fingerprint,
                Label = DisplayConfig.Describe(monitors),
                LastSeenUtc = DateTime.UtcNow,
                Monitors = monitors.ToList()
            };
            Configs.Add(created);
            return created;
        }

        /// <summary>
        /// Drops geometry for frames that no longer exist and keeps only the most recently seen
        /// configurations (the active one always survives). Bounds the file without ever losing
        /// the setup the user is sitting at.
        /// </summary>
        public void Prune(IEnumerable<string> liveFrameIds, int maxConfigs)
        {
            var live = new HashSet<string>(liveFrameIds, StringComparer.Ordinal);

            foreach (var config in Configs)
            {
                var dead = config.Frames.Keys.Where(id => !live.Contains(id)).ToList();
                foreach (string id in dead) config.Frames.Remove(id);
            }

            if (Configs.Count <= maxConfigs) return;

            var keep = Configs
                .OrderByDescending(c => string.Equals(c.Fingerprint, ActiveFingerprint, StringComparison.Ordinal))
                .ThenByDescending(c => c.LastSeenUtc)
                .Take(maxConfigs)
                .ToList();

            Configs = Configs.Where(keep.Contains).ToList();
        }

        /// <summary>Reads the sidecar, returning an empty store for a missing or unreadable file.</summary>
        public static LayoutStore Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new LayoutStore();

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return new LayoutStore();

                var store = JsonConvert.DeserializeObject<LayoutStore>(json);
                if (store == null) return new LayoutStore();

                store.Configs ??= new List<LayoutConfig>();
                store.Configs.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Fingerprint));
                foreach (var config in store.Configs)
                {
                    config.Monitors ??= new List<MonitorInfo>();
                    config.Frames ??= new Dictionary<string, FrameRect>();
                }
                store._lastWritten = json;
                return store;
            }
            catch (Exception ex)
            {
                // A corrupt sidecar is not worth a single frame: start over from the live geometry.
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Settings,
                    $"DisplayLayout: could not read '{path}', starting with an empty layout store: {ex.Message}");
                return new LayoutStore();
            }
        }

        /// <summary>Writes the sidecar atomically, skipping the IO when nothing changed.</summary>
        public void Save(string path)
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                if (string.Equals(json, _lastWritten, StringComparison.Ordinal)) return;

                AtomicFile.WriteAllText(path, json);
                _lastWritten = json;
            }
            catch (Exception ex)
            {
                // Same contract as SaveFrameData: never let a transient IO failure escape a
                // background save path. The layout is a cache; the next save retries.
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings,
                    $"DisplayLayout: failed to save '{path}': {ex.Message}");
            }
        }
    }
}
