using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// One entry in the on-disk category cache ("category_cache.json" beside the exe).
    /// Category is a fixed category name, or "none" for a cached negative result.
    /// </summary>
    public class CategoryCacheEntry
    {
        public string? Category { get; set; }
        public DateTime Timestamp { get; set; }
        /// <summary>Classifier generation that produced this verdict; entries from
        /// older generations are ignored so classifier improvements re-evaluate them.</summary>
        public int Version { get; set; }
    }

    /// <summary>
    /// The app-categorization engine that replaced the old rules-based auto-organize.
    ///
    /// Classifies desktop items (shortcuts and executables) into a FIXED set of
    /// category frames using three tiers — plus real files (documents, images)
    /// classified purely by extension into the "Documents" / "Images" frames:
    ///   Tier 1a — curated known-app list (instant, offline),
    ///   Tier 1b — install-path / URL heuristics (instant, offline),
    ///   Tier 2  — package-manager metadata (winget.run, then Chocolatey OData;
    ///             async, 3s timeout each, results cached in category_cache.json).
    /// The first tier that answers wins; items no tier can classify are left alone.
    ///
    /// The pure parts (known-list lookup, path heuristics, tag mapping, cache
    /// round-trip, free-space frame placement) are static and headless-testable.
    /// SortDesktopAsync is the orchestration entry point used by the tray command
    /// and by AutoOrganizeManager's desktop watcher.
    /// </summary>
    public static class AppCategorizer
    {
        #region Fixed categories

        public const string Productivity = "Productivity";
        public const string Utilities = "Utilities";
        public const string Games = "Games";
        public const string VR = "VR";
        public const string DeveloperTools = "Developer Tools";
        public const string SecurityApps = "Security Apps";
        public const string Media = "Media";
        public const string Documents = "Documents";
        public const string Images = "Images";

        /// <summary>The fixed categories in display order. Not user-editable.</summary>
        public static readonly string[] Categories =
        {
            Productivity, Utilities, Games, VR, DeveloperTools, SecurityApps, Media, Documents, Images
        };

        /// <summary>Top-level desktop FILES with these extensions are Documents (lowercase, no dot).</summary>
        internal static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "rtf",
            "odt", "ods", "odp", "csv", "xps", "epub", "one"
        };

        /// <summary>Top-level desktop FILES with these extensions are Images (lowercase, no dot).</summary>
        internal static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff", "heic", "svg", "psd"
        };

        /// <summary>
        /// Classifies a real file purely by extension: Documents, Images, or null.
        /// Case-insensitive; shortcuts/executables/folders never match.
        /// </summary>
        public static string? ClassifyByExtension(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string ext;
            try { ext = Path.GetExtension(path).TrimStart('.'); }
            catch { return null; }
            if (ext.Length == 0) return null;
            if (DocumentExtensions.Contains(ext)) return Documents;
            if (ImageExtensions.Contains(ext)) return Images;
            return null;
        }

        /// <summary>
        /// True for a desktop file the sort considers at all: app-like items
        /// (.lnk/.url/.exe) and files classifiable by extension (documents, images).
        /// </summary>
        public static bool IsCandidateFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext;
            try { ext = Path.GetExtension(path).ToLowerInvariant(); }
            catch { return false; }
            return ext == ".lnk" || ext == ".url" || ext == ".exe" || ClassifyByExtension(path) != null;
        }

        /// <summary>Sentinel stored in the cache for a confirmed "could not classify".</summary>
        public const string NoneCategory = "none";

        /// <summary>Negative ("none") cache entries expire after this many days.</summary>
        public const int NegativeCacheDays = 30;

        /// <summary>Bump when classification logic improves: stale cached verdicts
        /// (especially negatives) are then re-resolved instead of trusted.</summary>
        public const int CacheVersion = 2;

        #endregion

        #region Tier 1a — curated known apps (lowercase exe name or product name -> category)

        /// <summary>
        /// Curated well-known apps: key = lowercase target exe name WITHOUT extension
        /// (also matched against the item's display name). Extend freely.
        /// </summary>
        internal static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Productivity: browsers / office / mail / notes / chat ----
            ["chrome"] = Productivity,
            ["msedge"] = Productivity,
            ["firefox"] = Productivity,
            ["brave"] = Productivity,
            ["opera"] = Productivity,
            ["opera_gx"] = Productivity,
            ["vivaldi"] = Productivity,
            ["iexplore"] = Productivity,
            ["arc"] = Productivity,
            ["winword"] = Productivity,
            ["excel"] = Productivity,
            ["powerpnt"] = Productivity,
            ["outlook"] = Productivity,
            ["olk"] = Productivity,
            ["onenote"] = Productivity,
            ["msaccess"] = Productivity,
            ["mspub"] = Productivity,
            ["visio"] = Productivity,
            ["winproj"] = Productivity,
            ["soffice"] = Productivity,
            ["notion"] = Productivity,
            ["obsidian"] = Productivity,
            ["logseq"] = Productivity,
            ["joplin"] = Productivity,
            ["evernote"] = Productivity,
            ["slack"] = Productivity,
            ["teams"] = Productivity,
            ["ms-teams"] = Productivity,
            ["zoom"] = Productivity,
            ["webex"] = Productivity,
            ["thunderbird"] = Productivity,
            ["mailbird"] = Productivity,
            ["em client"] = Productivity,
            ["todoist"] = Productivity,
            ["trello"] = Productivity,
            ["anki"] = Productivity,
            ["acrord32"] = Productivity,
            ["acrobat"] = Productivity,
            ["foxitpdfreader"] = Productivity,
            ["sumatrapdf"] = Productivity,
            ["grammarly"] = Productivity,
            ["discord"] = Productivity,
            ["telegram"] = Productivity,
            ["whatsapp"] = Productivity,
            ["signal"] = Productivity,

            // ---- Utilities: system tools ----
            ["7zfm"] = Utilities,
            ["winrar"] = Utilities,
            ["peazip"] = Utilities,
            ["everything"] = Utilities,
            ["powertoys"] = Utilities,
            ["treesize"] = Utilities,
            ["treesizefree"] = Utilities,
            ["windirstat"] = Utilities,
            ["wiztree"] = Utilities,
            ["ccleaner"] = Utilities,
            ["ccleaner64"] = Utilities,
            ["rufus"] = Utilities,
            ["balenaetcher"] = Utilities,
            ["ventoy2disk"] = Utilities,
            ["speccy"] = Utilities,
            ["cpuz"] = Utilities,
            ["cpu-z"] = Utilities,
            ["gpu-z"] = Utilities,
            ["hwinfo64"] = Utilities,
            ["hwmonitor"] = Utilities,
            ["crystaldiskinfo"] = Utilities,
            ["crystaldiskmark"] = Utilities,
            ["teamviewer"] = Utilities,
            ["anydesk"] = Utilities,
            ["rustdesk"] = Utilities,
            ["sharex"] = Utilities,
            ["greenshot"] = Utilities,
            ["lightshot"] = Utilities,
            ["autohotkey"] = Utilities,
            ["revouninstaller"] = Utilities,
            ["procexp"] = Utilities,
            ["procexp64"] = Utilities,
            ["procmon"] = Utilities,
            ["procmon64"] = Utilities,
            ["speedfan"] = Utilities,
            ["defraggler"] = Utilities,
            ["recuva"] = Utilities,
            ["synctoy"] = Utilities,
            ["freefilesync"] = Utilities,

            // ---- Developer Tools ----
            ["code"] = DeveloperTools,
            ["code - insiders"] = DeveloperTools,
            ["devenv"] = DeveloperTools,
            ["rider64"] = DeveloperTools,
            ["idea64"] = DeveloperTools,
            ["pycharm64"] = DeveloperTools,
            ["webstorm64"] = DeveloperTools,
            ["clion64"] = DeveloperTools,
            ["goland64"] = DeveloperTools,
            ["datagrip64"] = DeveloperTools,
            ["phpstorm64"] = DeveloperTools,
            ["rubymine64"] = DeveloperTools,
            ["studio64"] = DeveloperTools,
            ["sublime_text"] = DeveloperTools,
            ["notepad++"] = DeveloperTools,
            ["dbeaver"] = DeveloperTools,
            ["postman"] = DeveloperTools,
            ["insomnia"] = DeveloperTools,
            ["docker desktop"] = DeveloperTools,
            ["githubdesktop"] = DeveloperTools,
            ["github desktop"] = DeveloperTools,
            ["sourcetree"] = DeveloperTools,
            ["gitkraken"] = DeveloperTools,
            ["fork"] = DeveloperTools,
            ["tortoisegitproc"] = DeveloperTools,
            ["putty"] = DeveloperTools,
            ["kitty"] = DeveloperTools,
            ["mobaxterm"] = DeveloperTools,
            ["windowsterminal"] = DeveloperTools,
            ["wt"] = DeveloperTools,
            ["termius"] = DeveloperTools,
            ["cmder"] = DeveloperTools,
            ["conemu64"] = DeveloperTools,
            ["alacritty"] = DeveloperTools,
            ["filezilla"] = DeveloperTools,
            ["winscp"] = DeveloperTools,
            ["cyberduck"] = DeveloperTools,
            ["heidisql"] = DeveloperTools,
            ["ssms"] = DeveloperTools,
            ["mysqlworkbench"] = DeveloperTools,
            ["pgadmin4"] = DeveloperTools,
            ["unity hub"] = DeveloperTools,
            ["unityhub"] = DeveloperTools,
            ["godot"] = DeveloperTools,
            ["pwsh"] = DeveloperTools,
            ["windbg"] = DeveloperTools,
            ["ilspy"] = DeveloperTools,
            ["dnspy"] = DeveloperTools,
            ["linqpad8"] = DeveloperTools,
            ["fiddler"] = DeveloperTools,
            ["gvim"] = DeveloperTools,
            ["emacs"] = DeveloperTools,
            ["gitextensions"] = DeveloperTools,

            // ---- Security Apps ----
            ["malwarebytes"] = SecurityApps,
            ["mbam"] = SecurityApps,
            ["bitwarden"] = SecurityApps,
            ["keepass"] = SecurityApps,
            ["keepassxc"] = SecurityApps,
            ["1password"] = SecurityApps,
            ["keeper"] = SecurityApps,
            ["lastpass"] = SecurityApps,
            ["dashlane"] = SecurityApps,
            ["enpass"] = SecurityApps,
            ["nordvpn"] = SecurityApps,
            ["expressvpn"] = SecurityApps,
            ["protonvpn"] = SecurityApps,
            ["mullvad vpn"] = SecurityApps,
            ["surfshark"] = SecurityApps,
            ["openvpn-gui"] = SecurityApps,
            ["tailscale"] = SecurityApps,
            ["wireshark"] = SecurityApps,
            ["nmap"] = SecurityApps,
            ["zenmap"] = SecurityApps,
            ["glasswire"] = SecurityApps,
            ["avastui"] = SecurityApps,
            ["avgui"] = SecurityApps,
            ["avp"] = SecurityApps,
            ["egui"] = SecurityApps,
            ["veracrypt"] = SecurityApps,
            ["cryptomator"] = SecurityApps,
            ["authy"] = SecurityApps,

            // ---- Media ----
            ["vlc"] = Media,
            ["spotify"] = Media,
            ["itunes"] = Media,
            ["applemusic"] = Media,
            ["wmplayer"] = Media,
            ["mpc-hc"] = Media,
            ["mpc-hc64"] = Media,
            ["potplayermini64"] = Media,
            ["smplayer"] = Media,
            ["obs64"] = Media,
            ["obs32"] = Media,
            ["audacity"] = Media,
            ["foobar2000"] = Media,
            ["musicbee"] = Media,
            ["aimp"] = Media,
            ["winamp"] = Media,
            ["kodi"] = Media,
            ["plex"] = Media,
            ["jellyfin media player"] = Media,
            ["stremio"] = Media,
            ["deezer"] = Media,
            ["tidal"] = Media,
            ["amazon music"] = Media,
            ["mediamonkey"] = Media,
            ["calibre"] = Media,
            ["yacreader"] = Media,
            ["i_view64"] = Media,
            ["irfanview"] = Media,
            ["xnview"] = Media,
            ["fsviewer"] = Media,
            ["gimp"] = Media,
            ["gimp-2.10"] = Media,
            ["krita"] = Media,
            ["inkscape"] = Media,
            ["photoshop"] = Media,
            ["illustrator"] = Media,
            ["lightroom"] = Media,
            ["premiere"] = Media,
            ["afterfx"] = Media,
            ["resolve"] = Media,
            ["handbrake"] = Media,
            ["paintdotnet"] = Media,
            ["shotcut"] = Media,
            ["kdenlive"] = Media,
            ["openshot-qt"] = Media,
            ["blender"] = Media,
            ["darktable"] = Media,
            ["rawtherapee"] = Media,
            ["mkvtoolnix-gui"] = Media,

            // ---- Games / launchers ----
            ["steam"] = Games,
            ["epicgameslauncher"] = Games,
            ["galaxyclient"] = Games,
            ["battle.net"] = Games,
            ["riotclientservices"] = Games,
            ["eadesktop"] = Games,
            ["origin"] = Games,
            ["ubisoftconnect"] = Games,
            ["upc"] = Games,
            ["playnite"] = Games,
            ["playnite.desktopapp"] = Games,
            ["xboxpcapp"] = Games,
            ["xbox"] = Games,
            ["minecraftlauncher"] = Games,
            ["curseforge"] = Games,
            ["modrinth app"] = Games,
            ["itch"] = Games,
            ["heroic"] = Games,
            ["lunarclient"] = Games,
            ["wemod"] = Games,
            ["bethesdanetlauncher"] = Games,
            ["wgc"] = Games,
            ["geforcenow"] = Games,
            ["moonlight"] = Games,
            ["parsec"] = Games,
            ["retroarch"] = Games,

            // ---- VR ----
            ["oculusclient"] = VR,
            ["metaquestlink"] = VR,
            ["vrmonitor"] = VR,
            ["vrstartup"] = VR,
            ["virtualdesktop.streamer"] = VR,
            ["viveconsole"] = VR,
            ["vrchat"] = VR,
            ["pimaxclient"] = VR,
            ["varjobase"] = VR,
            ["mixedrealityportal"] = VR,
            // --- Added from real-desktop misses (2026-08-22) ---
            ["cursor"] = DeveloperTools, ["tabby"] = DeveloperTools, ["bcompare"] = DeveloperTools,
            ["opencode"] = DeveloperTools, ["beyond compare"] = DeveloperTools,
            ["peggle"] = Games, ["hytale-launcher"] = Games, ["hytale launcher"] = Games,
            ["lunar client"] = Games, ["ftb app"] = Games, ["overwolflauncher"] = Games, ["overwolf"] = Games,
            ["immersed"] = VR, ["immersed agent"] = VR,
            ["kindle"] = Media, ["openmpt"] = Media, ["gimp-3"] = Media,
            ["nvidia app"] = Utilities, ["logioptionsplus"] = Utilities, ["logi options+"] = Utilities,
            ["unigetui"] = Utilities, ["diskinfo64a"] = Utilities, ["crystaldiskinfo"] = Utilities,
            ["advanced_port_scanner"] = Utilities, ["advanced port scanner"] = Utilities,
            ["nextgenlenovodiagnostics"] = Utilities, ["lenovo diagnostics"] = Utilities,
            ["eppcchkr"] = Utilities, ["epplusg"] = Utilities, ["epson photo+"] = Utilities,
            ["brother utilities"] = Utilities, ["brlauncher"] = Utilities,
            ["v2v_converter"] = Utilities, ["starwind v2v converter"] = Utilities,
            ["vspemulator"] = Utilities, ["vspe"] = Utilities, ["aurgaviewer"] = Utilities, ["aurga viewer"] = Utilities,
            ["playstationaccessories"] = Games, ["playstation accessories"] = Games,
            ["openscad"] = Productivity, ["meshmixer"] = Productivity, ["crealityprint"] = Productivity,
            ["creality print"] = Productivity, ["flashprint"] = Productivity, ["falcondesignspace"] = Productivity,
            ["falcon design space"] = Productivity, ["perplexity ai"] = Productivity, ["perplexity"] = Productivity,
        };

        /// <summary>
        /// Multi-word product-name fragments matched by SUBSTRING against the display
        /// name (lowercase). Used when the exe/display name is not an exact known key.
        /// </summary>
        internal static readonly (string Fragment, string Category)[] KnownNameSubstrings =
        {
            ("visual studio code", DeveloperTools),
            ("visual studio", DeveloperTools),
            ("android studio", DeveloperTools),
            ("intellij idea", DeveloperTools),
            ("sql server management", DeveloperTools),
            ("windows terminal", DeveloperTools),
            ("docker desktop", DeveloperTools),
            ("github desktop", DeveloperTools),
            ("epic games", Games),
            ("gog galaxy", Games),
            ("riot client", Games),
            ("league of legends", Games),
            ("battle.net", Games),
            ("ubisoft connect", Games),
            ("rockstar games", Games),
            ("ea app", Games),
            ("meta quest", VR),
            ("oculus", VR),
            ("steamvr", VR),
            ("virtual desktop", VR),
            ("malwarebytes", SecurityApps),
            ("proton vpn", SecurityApps),
            ("norton 360", SecurityApps),
            ("obs studio", Media),
            ("vlc media player", Media),
            ("davinci resolve", Media),
            ("adobe photoshop", Media),
            ("adobe premiere", Media),
            ("paint.net", Media),
            ("7-zip", Utilities),
            ("google chrome", Productivity),
            ("microsoft edge", Productivity),
            ("mozilla firefox", Productivity),
            ("microsoft teams", Productivity),
        };

        /// <summary>
        /// Normalizes "gimp-3.0.6" / "Creality Print 7.2" / "openSCAD Nightly" to a
        /// base token and looks for a known key it starts with (longest key wins).
        /// </summary>
        internal static string? MatchKnownNormalized(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string norm = raw.ToLowerInvariant();
            norm = Regex.Replace(norm, @"\s*(nightly|beta|alpha|preview|portable|x64|x86|64|32)\b", "");
            norm = Regex.Replace(norm, @"[\s_\-]*v?\d+(\.\d+)*\s*$", "");   // trailing version
            norm = Regex.Replace(norm, @"[\s_\-]+", "");                      // "creality print" -> "crealityprint"
            if (norm.Length < 4) return null;

            string? best = null; int bestLen = 0;
            foreach (var kv in KnownApps)
            {
                string key = Regex.Replace(kv.Key.ToLowerInvariant(), @"[\s_\-]+", "");
                if (key.Length >= 4 && norm.StartsWith(key, StringComparison.Ordinal) && key.Length > bestLen)
                {
                    best = kv.Value; bestLen = key.Length;
                }
            }
            return best;
        }

        /// <summary>
        /// Tier 1a: classify by the curated known-app list. Matches the target's exe
        /// name (without extension), then the display name — exact first, then the
        /// multi-word product-name substrings. Case-insensitive. Null = no answer.
        /// </summary>
        public static string? ClassifyKnown(string? targetPath, string? displayName)
        {
            try
            {
                string exeName = "";
                if (!string.IsNullOrWhiteSpace(targetPath) && !LooksLikeUrl(targetPath))
                {
                    try { exeName = Path.GetFileNameWithoutExtension(targetPath) ?? ""; }
                    catch { exeName = ""; }
                }

                if (exeName.Length > 0 && KnownApps.TryGetValue(exeName, out var byExe))
                    return byExe;

                string name = (displayName ?? "").Trim();
                if (name.Length > 0 && KnownApps.TryGetValue(name, out var byName))
                    return byName;

                // Version-tolerant matching: "gimp-3", "gimp-3.0.6", "Creality Print 7.2",
                // "openSCAD Nightly" must still hit their known entry. Strip trailing
                // version/edition tokens, then accept a known key that the normalized
                // exe/display name starts with (keys of 4+ chars only, to avoid noise).
                string? byNormalized = MatchKnownNormalized(exeName) ?? MatchKnownNormalized(name);
                if (byNormalized != null) return byNormalized;

                string nameLower = name.ToLowerInvariant();
                if (nameLower.Length > 0)
                {
                    foreach (var (fragment, category) in KnownNameSubstrings)
                        if (nameLower.Contains(fragment))
                            return category;
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region Tier 1b — path / URL heuristics

        /// <summary>
        /// Install-path and protocol fragments checked (case-insensitively) against BOTH
        /// the resolved target path and the shortcut arguments/URL. Order matters:
        /// VR entries come first so e.g. "\steamapps\common\SteamVR\" resolves to VR,
        /// not Games. Extend freely.
        /// </summary>
        internal static readonly (string Fragment, string Category)[] PathHints =
        {
            // VR first (more specific than the Steam/game-store fragments below)
            (@"\oculus\", VR),
            (@"\meta quest\", VR),
            (@"\steamvr\", VR),
            ("vrmonitor", VR),
            ("oculus://", VR),

            // Game stores / libraries
            (@"\steamapps\", Games),
            (@"\steam\", Games),
            ("steam://", Games),
            (@"\xboxgames\", Games),
            ("microsoft.gamingapp", Games),
            (@"\epic games\", Games),
            ("com.epicgames", Games),
            (@"\gog galaxy\", Games),
            (@"\gog games\", Games),
            ("goggalaxy://", Games),
            (@"\riot games\", Games),
            (@"\battle.net\", Games),
            ("battlenet://", Games),
            (@"\ea games\", Games),
            (@"\electronic arts\", Games),
            (@"\ubisoft\", Games),
            ("uplay://", Games),
        };

        /// <summary>
        /// Tier 1c: generic keyword heuristics over the display name and exe name,
        /// consulted only after the known list and path hints miss. Ordered so the
        /// more specific signals (security, VR, developer) win over broad ones.
        /// </summary>
        internal static readonly (string Category, string[] Keywords)[] NameKeywordRules =
        {
            (SecurityApps,   new[] { "antivirus", "anti-virus", "vpn", "password", "firewall", "security", "malware", "defender" }),
            (VR,             new[] { " vr", "vr ", "oculus", "quest link", "immersed", "steamvr", "virtual desktop" }),
            (DeveloperTools, new[] { "terminal", "console", "ide", "compiler", "sdk", "devtools", "developer", "debugger", "git ", "docker", "postman", "code editor", "opencode" }),
            (Games,          new[] { "launcher", "minecraft", " game", "games", "emulator", "steam", "epic games", "gog ", "xbox", "playstation" }),
            (Media,          new[] { "player", "music", "video", "reader", "kindle", "photo", "image editor", "camera", " tv", "podcast", "audio", "spotify" }),
            (Productivity,   new[] { "print", "slicer", "cad", "scad", "mesh", "design space", "office", "notes", "mail", "browser", "docs", "calendar", "chat", "ai" }),
            (Utilities,      new[] { "viewer", "diagnostic", "utilit", "toolbox", "converter", "scanner", "driver", "options", "checker", "control center", "cleaner", "monitor", "setup", "update", "printer", "connection", "manager", "config", "settings", "tool" }),
        };

        public static string? ClassifyByNameKeywords(string? displayName, string? targetPath)
        {
            try
            {
                string exe = "";
                if (!string.IsNullOrWhiteSpace(targetPath) && !LooksLikeUrl(targetPath))
                {
                    try { exe = Path.GetFileNameWithoutExtension(targetPath) ?? ""; } catch { }
                }
                string hay = (" " + (displayName ?? "") + " " + exe + " ").ToLowerInvariant()
                    .Replace("_", " ").Replace("-", " ");
                if (hay.Trim().Length == 0) return null;

                foreach (var (category, keywords) in NameKeywordRules)
                    foreach (string kw in keywords)
                        if (hay.Contains(kw)) return category;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Tier 1b: classify by install-path / URL heuristics. Both the resolved target
        /// path and the shortcut arguments (or .url URL) are searched. Null = no answer.
        /// </summary>
        public static string? ClassifyByPath(string? targetPath, string? argumentsOrUrl)
        {
            try
            {
                string haystack = ((targetPath ?? "") + "\n" + (argumentsOrUrl ?? "")).ToLowerInvariant();
                if (haystack.Trim().Length == 0) return null;

                foreach (var (fragment, category) in PathHints)
                    if (haystack.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                        return category;
            }
            catch { }
            return null;
        }

        private static bool LooksLikeUrl(string value) =>
            value.Contains("://", StringComparison.Ordinal);

        #endregion

        #region Tier 2 — package-manager tag mapping (pure part)

        /// <summary>
        /// Keyword table mapping package tags to categories. Scanned in order — the
        /// more specific categories (VR, Games, Security) win over generic ones.
        /// </summary>
        internal static readonly (string Category, string[] Keywords)[] TagKeywordTable =
        {
            (VR, new[] { "vr", "steamvr", "oculus" }),
            (Games, new[] { "game", "games", "gaming" }),
            (SecurityApps, new[] { "security", "antivirus", "vpn", "password", "passwords", "privacy", "firewall", "encryption", "malware" }),
            (DeveloperTools, new[] { "development", "developer", "programming", "ide", "sdk", "cli", "terminal", "git", "database", "debugger", "devops" }),
            (Media, new[] { "video", "audio", "music", "media", "player", "photo", "photos", "image", "images", "ebook", "comic", "comics", "streaming", "podcast", "movie", "movies", "photography", "graphics" }),
            (Productivity, new[] { "office", "productivity", "notes", "note", "email", "browser", "calendar", "documents", "pdf", "spreadsheet", "todo" }),
            (Utilities, new[] { "utility", "utilities", "tool", "tools", "system", "backup", "compression", "archiver", "cleaner" }),
        };

        /// <summary>
        /// Maps package-manager tags to a category. Tags are tokenized on common
        /// separators and matched case-insensitively against the keyword table.
        /// Null = no keyword matched (unclassified).
        /// </summary>
        public static string? CategoryFromTags(IEnumerable<string>? tags)
        {
            if (tags == null) return null;

            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                foreach (string token in tag.Split(new[] { ' ', '-', '_', ',', ';', '/', '\t' },
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    tokens.Add(token);
                }
            }
            if (tokens.Count == 0) return null;

            foreach (var (category, keywords) in TagKeywordTable)
                if (keywords.Any(tokens.Contains))
                    return category;

            return null;
        }

        #endregion

        #region Category cache (category_cache.json at the app root)

        /// <summary>Default cache file location: beside the exe (portable-app convention).</summary>
        public static string CacheFilePath => Path.Combine(AppContext.BaseDirectory, "category_cache.json");

        /// <summary>Loads the category cache; any failure yields an empty cache.</summary>
        public static Dictionary<string, CategoryCacheEntry> LoadCategoryCache(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cache = JsonConvert.DeserializeObject<Dictionary<string, CategoryCacheEntry>>(json);
                    if (cache != null)
                        return new Dictionary<string, CategoryCacheEntry>(cache, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"App-Categorize: could not load category cache: {ex.Message}");
            }
            return new Dictionary<string, CategoryCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Persists the category cache atomically (crash-safe swap).</summary>
        public static void SaveCategoryCache(string path, Dictionary<string, CategoryCacheEntry> cache)
        {
            try
            {
                AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(cache, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"App-Categorize: could not save category cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Consults the cache. Returns true when the cache answers: category is the
        /// cached category, or null for a fresh negative ("none") entry. Returns false
        /// (lookup needed) for missing entries, stale negatives, and unknown category
        /// names left behind by older versions.
        /// </summary>
        public static bool TryGetCachedCategory(Dictionary<string, CategoryCacheEntry> cache,
            string key, DateTime utcNow, out string? category)
        {
            category = null;
            if (cache == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!cache.TryGetValue(key, out var entry) || entry == null) return false;
            if (entry.Version != CacheVersion) return false; // older classifier generation

            if (string.Equals(entry.Category, NoneCategory, StringComparison.OrdinalIgnoreCase))
            {
                // Negative result: honoured only while fresh — retry after 30 days.
                return (utcNow - entry.Timestamp) < TimeSpan.FromDays(NegativeCacheDays);
            }

            if (Categories.Contains(entry.Category, StringComparer.OrdinalIgnoreCase))
            {
                category = Categories.First(c => string.Equals(c, entry.Category, StringComparison.OrdinalIgnoreCase));
                return true;
            }

            return false; // unknown category name from an older version — re-resolve
        }

        /// <summary>Cache key for an item: lowercase exe name, else lowercase display name.</summary>
        public static string CacheKeyFor(string? targetPath, string? displayName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(targetPath) && !LooksLikeUrl(targetPath))
                {
                    string exe = Path.GetFileNameWithoutExtension(targetPath) ?? "";
                    if (exe.Length > 0) return exe.ToLowerInvariant();
                }
            }
            catch { }
            return (displayName ?? "").Trim().ToLowerInvariant();
        }

        #endregion

        #region Tier 2 — online lookups (winget.run, Chocolatey)

        private static readonly Lazy<HttpClient> _http = new(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopPossible");
            return client;
        });

        /// <summary>
        /// Best-effort online classification via package-manager metadata:
        /// winget community API first, then the Chocolatey OData feed. Every failure
        /// (network, timeout, parse) yields null. NEVER call on the UI thread.
        /// </summary>
        internal static async Task<string?> LookupOnlineAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // winget.run's community index has gone empty (every query returns zero
            // packages), so Chocolatey's documented Search() endpoint is the sole source.
            return await TryChocolateyAsync(name);
        }

        private static async Task<string?> TryWingetAsync(string name)
        {
            try
            {
                string url = $"https://api.winget.run/v2/packages/search?query={Uri.EscapeDataString(name)}&take=3";
                string json = await _http.Value.GetStringAsync(url);
                return CategoryFromWingetJson(json, name);
            }
            catch { return null; }
        }

        private static async Task<string?> TryChocolateyAsync(string name)
        {
            try
            {
                // Search() is the endpoint the choco CLI itself uses; the
                // Packages()/substringof filter form returns no entries on this feed.
                string url = "https://community.chocolatey.org/api/v2/Search()?$filter=IsLatestVersion&searchTerm='"
                             + Uri.EscapeDataString(name.ToLowerInvariant()) + "'&targetFramework=''&includePrerelease=false&$top=3";
                string xml = await _http.Value.GetStringAsync(url);
                return CategoryFromChocolateyXml(xml);
            }
            catch { return null; }
        }

        /// <summary>
        /// Parses a winget.run search response: only packages whose name/id actually
        /// matches the query contribute their tags. Pure and testable.
        /// </summary>
        public static string? CategoryFromWingetJson(string json, string query)
        {
            try
            {
                var root = JObject.Parse(json);
                var packages = root["Packages"] as JArray;
                if (packages == null) return null;

                string q = query.Trim().ToLowerInvariant();

                foreach (var pkg in packages.OfType<JObject>())
                {
                    var latest = pkg["Latest"] as JObject;
                    string pkgName = (latest?["Name"] ?? pkg["Name"])?.ToString() ?? "";
                    string pkgId = pkg["Id"]?.ToString() ?? "";

                    // Name-match quality gate: the query must appear in the package
                    // name/id (or vice versa) — a fuzzy search hit with an unrelated
                    // name must not classify our item.
                    string nameLower = pkgName.ToLowerInvariant();
                    string idLower = pkgId.ToLowerInvariant();
                    bool matches = nameLower.Contains(q) || q.Contains(nameLower) && nameLower.Length >= 3
                                   || idLower.Contains(q);
                    if (!matches) continue;

                    var tagsToken = (latest?["Tags"] ?? pkg["Tags"]) as JArray;
                    var tags = tagsToken?.Select(t => t.ToString());
                    string? category = CategoryFromTags(tags);
                    if (category != null) return category;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Extracts the d:Tags values from a Chocolatey OData (Atom XML) response and
        /// maps them to a category. Pure and testable.
        /// </summary>
        public static string? CategoryFromChocolateyXml(string xml)
        {
            try
            {
                foreach (Match m in Regex.Matches(xml ?? "", @"<d:Tags[^>]*>(.*?)</d:Tags>",
                             RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string? category = CategoryFromTags(new[] { m.Groups[1].Value });
                    if (category != null) return category;
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region Frame sizing + free-space placement (pure, testable)

        /// <summary>Grid cell width for default icon spacing (60 + 5*4), see GetFreeArrangeCellWidth.</summary>
        private const double CellWidth = 80;
        private const double CellHeight = 80;
        private const double FrameChromeWidth = 30;   // borders + scrollbar allowance
        private const double FrameTitleBarHeight = 40;
        private const int TargetColumns = 4;

        /// <summary>
        /// Sizes a new category frame from its content: width for ~4 icon columns,
        /// height for ceil(count/4) rows plus the title bar, clamped to ~60% of the
        /// work-area height (leftover icons still land in the grid; the frame scrolls
        /// and stays user-resizable).
        /// </summary>
        public static (double Width, double Height) ComputeFrameSize(int itemCount, double workAreaHeight)
        {
            double width = TargetColumns * CellWidth + FrameChromeWidth;
            int rows = Math.Max(1, (int)Math.Ceiling(itemCount / (double)TargetColumns));
            double height = rows * CellHeight + FrameTitleBarHeight;
            double maxHeight = Math.Max(CellHeight + FrameTitleBarHeight, workAreaHeight * 0.6);
            return (width, Math.Min(height, maxHeight));
        }

        /// <summary>
        /// Finds the first position (row-major, coarse step, from the work area's
        /// top-left) where a rect of the given size fits entirely inside the work area
        /// and intersects none of the occupied rects. Null when nothing fits — callers
        /// fall back to a small cascade (overlap allowed only as the last resort).
        /// </summary>
        public static (double X, double Y)? FindFreePosition(double width, double height,
            IReadOnlyCollection<(double X, double Y, double W, double H)> occupied,
            (double X, double Y, double W, double H) workArea, double step = 24)
        {
            if (step <= 0) step = 24;
            occupied ??= Array.Empty<(double, double, double, double)>();

            for (double y = workArea.Y; y + height <= workArea.Y + workArea.H; y += step)
            {
                for (double x = workArea.X; x + width <= workArea.X + workArea.W; x += step)
                {
                    bool clash = false;
                    foreach (var r in occupied)
                    {
                        if (x < r.X + r.W && r.X < x + width && y < r.Y + r.H && r.Y < y + height)
                        {
                            clash = true;
                            break;
                        }
                    }
                    if (!clash) return (x, y);
                }
            }
            return null;
        }

        #endregion

        #region Sorting flow

        private static int _sorting; // 0 = idle, 1 = a sort is running

        /// <summary>
        /// True while a categorize-and-sort pass is running. Virtual-desktop profile
        /// switching defers while set — a switch mid-sort would tear the frame data
        /// out from under the in-flight moves.
        /// </summary>
        public static bool IsSorting => Volatile.Read(ref _sorting) == 1;

        private sealed class DesktopEntry
        {
            public string Path = "";           // the desktop file (.lnk/.url/.exe)
            public string Ext = "";            // lowercase extension
            public string? Target;             // resolved target path (or URL)
            public string? Arguments;          // shortcut arguments / .url URL
            public string DisplayName = "";
            public bool IsWebLink;
            public bool IsRawFile;            // document/image: the file itself is the item
            public string? Category;
        }

        /// <summary>
        /// THE public entry point: classifies the desktop's top-level shortcuts and
        /// executables and moves the classified ones into fixed category frames
        /// (created on demand in free screen space). Items no tier can classify are
        /// left untouched; items already referenced by any frame are never touched;
        /// raw .exe files are wrapped in a shortcut and never deleted.
        /// Returns (items moved, categories used). Safe to fire-and-forget.
        /// </summary>
        /// <param name="limitToPaths">Optional: restrict the pass to these desktop files
        /// (used by the auto-organize watcher for single new arrivals).</param>
        public static async Task<(int ItemsMoved, int CategoriesUsed)> SortDesktopAsync(IEnumerable<string>? limitToPaths = null)
        {
            if (Interlocked.CompareExchange(ref _sorting, 1, 0) != 0)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General,
                    "App-Categorize: sort already in progress, skipping.");
                return (0, 0);
            }

            try
            {
                return await Task.Run(() => SortDesktopCoreAsync(limitToPaths?.ToList()));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"App-Categorize: sort failed: {ex.Message}");
                return (0, 0);
            }
            finally
            {
                Volatile.Write(ref _sorting, 0);
            }
        }

        private static async Task<(int ItemsMoved, int CategoriesUsed)> SortDesktopCoreAsync(List<string>? limitToPaths)
        {
            // ---- 1. Enumerate candidates: user + common desktop, top level only ----
            var candidatePaths = new List<string>();

            if (limitToPaths != null)
            {
                candidatePaths.AddRange(limitToPaths.Where(File.Exists));
            }
            else
            {
                string[] roots =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                };
                foreach (string root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
                {
                    try { candidatePaths.AddRange(Directory.GetFiles(root)); }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                            $"App-Categorize: cannot enumerate '{root}': {ex.Message}");
                    }
                }
            }

            candidatePaths = candidatePaths
                .Where(IsCandidateFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidatePaths.Count == 0) return (0, 0);

            // ---- 2. Never touch anything already framed ----
            var framedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            OnUi(() => CollectFramedItemPaths(framedPaths));
            candidatePaths.RemoveAll(framedPaths.Contains);
            if (candidatePaths.Count == 0) return (0, 0);

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"App-Categorize: classifying {candidatePaths.Count} desktop item(s)...");

            // ---- 3. Resolve + classify (tier 1 sync, tier 2 batched async) ----
            var entries = new List<DesktopEntry>();
            foreach (string path in candidatePaths)
            {
                var entry = ResolveEntry(path);
                if (entry == null) continue;

                // Documents/images are classified by extension in ResolveEntry and never
                // go through the app tiers (a "chrome.pdf" is a document, not a browser).
                entry.Category ??= ClassifyKnown(entry.Target, entry.DisplayName)
                                   ?? ClassifyByPath(entry.Target, entry.Arguments)
                                   ?? ClassifyByNameKeywords(entry.DisplayName, entry.Target);
                entries.Add(entry);
            }

            var unresolved = entries.Where(e => e.Category == null).ToList();
            if (unresolved.Count > 0)
                await ClassifyOnlineBatchAsync(unresolved);

            var classified = entries.Where(e => e.Category != null).ToList();
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"App-Categorize: {classified.Count} of {entries.Count} item(s) classified.");
            if (classified.Count == 0) return (0, 0);

            // ---- 4. Move into category frames on the UI thread ----
            int moved = 0;
            var usedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            OnUi(() =>
            {
                var placedThisRun = new List<(double X, double Y, double W, double H)>();

                foreach (string category in Categories) // fixed display order
                {
                    var items = classified.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (items.Count == 0) continue;

                    dynamic? frame = FindDataFrameByTitle(category) ?? CreateCategoryFrame(category, items.Count, placedThisRun);
                    if (frame == null) continue;

                    foreach (var item in items)
                    {
                        if (AddItemToFrame(frame, item))
                        {
                            moved++;
                            usedCategories.Add(category);
                        }
                    }
                }

                if (moved > 0)
                {
                    FrameDataManager.SaveFrameData();
                    Framemanager.ReloadFrames(true); // one refresh for everything at the end
                }
            });

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"App-Categorize: moved {moved} item(s) into {usedCategories.Count} category frame(s).");
            return (moved, usedCategories.Count);
        }

        /// <summary>Resolves a desktop file into a classifiable entry (COM off the UI thread).</summary>
        private static DesktopEntry? ResolveEntry(string path)
        {
            try
            {
                var entry = new DesktopEntry
                {
                    Path = path,
                    Ext = Path.GetExtension(path).ToLowerInvariant(),
                    DisplayName = Path.GetFileNameWithoutExtension(path)
                };

                if (entry.Ext == ".lnk")
                {
                    entry.Target = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                    entry.Arguments = Utility.GetShortcutArguments(path);
                    entry.IsWebLink = entry.Target != null && LooksLikeUrl(entry.Target);

                    // Folder-target shortcuts are skipped UNLESS the folder is a
                    // game-store path (e.g. a shortcut to a Steam library folder).
                    if (!entry.IsWebLink && !string.IsNullOrEmpty(entry.Target) && Directory.Exists(entry.Target))
                    {
                        if (ClassifyByPath(entry.Target, entry.Arguments) == null) return null;
                    }
                }
                else if (entry.Ext == ".url")
                {
                    entry.IsWebLink = true;
                    try { entry.Arguments = CoreUtilities.ExtractWebUrlFromFile(path); } catch { }
                    entry.Target = entry.Arguments; // classify by the URL (steam:// etc.)
                }
                else if (entry.Ext == ".exe")
                {
                    entry.Target = path;
                }
                else
                {
                    // Real file (document / image): it is its own target, classified by
                    // extension only. Anything else is not a candidate.
                    entry.Category = ClassifyByExtension(path);
                    if (entry.Category == null) return null;
                    entry.Target = path;
                    entry.IsRawFile = true;
                }

                return entry;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"App-Categorize: cannot resolve '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tier 2 for a batch: cache first, then winget/Chocolatey with a small
        /// concurrency cap. All results (including negatives) are cached.
        /// </summary>
        private static async Task ClassifyOnlineBatchAsync(List<DesktopEntry> entries)
        {
            var cache = LoadCategoryCache(CacheFilePath);
            bool cacheDirty = false;
            var gate = new SemaphoreSlim(3);
            var now = DateTime.UtcNow;

            var lookups = new List<Task>();
            var cacheLock = new object();

            foreach (var entry in entries)
            {
                string key = CacheKeyFor(entry.Target, entry.DisplayName);
                if (key.Length == 0) continue;

                bool answered;
                string? cached;
                lock (cacheLock) { answered = TryGetCachedCategory(cache, key, now, out cached); }
                if (answered)
                {
                    entry.Category = cached; // null = fresh cached negative — leave alone
                    continue;
                }

                lookups.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        string? category = await LookupOnlineAsync(key);
                        entry.Category = category;
                        lock (cacheLock)
                        {
                            cache[key] = new CategoryCacheEntry
                            {
                                Category = category ?? NoneCategory,
                                Timestamp = DateTime.UtcNow,
                                Version = CacheVersion
                            };
                            cacheDirty = true;
                        }
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General,
                            $"App-Categorize: online lookup '{key}' -> {category ?? "unclassified"}");
                    }
                    finally { gate.Release(); }
                }));
            }

            if (lookups.Count > 0) await Task.WhenAll(lookups);
            if (cacheDirty) SaveCategoryCache(CacheFilePath, cache);
        }

        /// <summary>
        /// Collects the resolved full paths of every item referenced by any frame
        /// (main lists and all tabs) so the sort can never touch something already
        /// framed. Runs on the UI thread.
        /// </summary>
        private static void CollectFramedItemPaths(HashSet<string> into)
        {
            try
            {
                string baseDir = ProfileManager.CurrentProfileDir;

                void CollectList(JArray? items)
                {
                    if (items == null) return;
                    foreach (var item in items.OfType<JObject>())
                    {
                        string filename = item["Filename"]?.ToString() ?? "";
                        if (filename.Length == 0) continue;
                        try
                        {
                            into.Add(Path.IsPathRooted(filename)
                                ? Path.GetFullPath(filename)
                                : Path.GetFullPath(Path.Combine(baseDir, filename)));
                        }
                        catch { }
                    }
                }

                foreach (dynamic frame in FrameDataManager.FrameData)
                {
                    try
                    {
                        CollectList(frame.Items as JArray);
                        if (frame.Tabs is JArray tabs)
                            foreach (var tab in tabs.OfType<JObject>())
                                CollectList(tab["Items"] as JArray);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Finds an existing Data frame titled exactly the category name.</summary>
        private static dynamic? FindDataFrameByTitle(string title)
        {
            try
            {
                foreach (dynamic frame in FrameDataManager.FrameData)
                {
                    try
                    {
                        if (frame.ItemsType?.ToString() == "Data" &&
                            string.Equals(frame.Title?.ToString(), title, StringComparison.OrdinalIgnoreCase))
                            return frame;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Creates a new Data frame for a category, auto-laid-out into free screen
        /// space: sized from its content, placed at the first spot in the primary
        /// monitor's work area (coarse row-major scan) that overlaps no existing
        /// frame window, no persisted hidden-frame rect, and nothing placed earlier
        /// this run. Falls back to a small cascade (logged) when nothing fits.
        /// Runs on the UI thread.
        /// </summary>
        private static dynamic? CreateCategoryFrame(string category, int itemCount,
            List<(double X, double Y, double W, double H)> placedThisRun)
        {
            try
            {
                var wa = System.Windows.SystemParameters.WorkArea;
                var (width, height) = ComputeFrameSize(itemCount, wa.Height);

                // Occupied space: live windows first (real on-screen rects), persisted
                // geometry for frames without a window, plus this run's placements.
                var occupied = new List<(double X, double Y, double W, double H)>(placedThisRun);

                var winByFrameId = new Dictionary<string, NonActivatingWindow>(StringComparer.OrdinalIgnoreCase);
                if (System.Windows.Application.Current != null)
                {
                    foreach (var win in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
                    {
                        string id = win.Tag?.ToString() ?? "";
                        if (id.Length > 0) winByFrameId[id] = win;
                    }
                }

                foreach (dynamic frame in FrameDataManager.FrameData)
                {
                    try
                    {
                        string id = frame.Id?.ToString() ?? "";
                        if (id.Length > 0 && winByFrameId.TryGetValue(id, out var win))
                        {
                            double w = win.ActualWidth > 0 ? win.ActualWidth : ParseDouble(win.Width, 230);
                            double h = win.ActualHeight > 0 ? win.ActualHeight : ParseDouble(win.Height, 130);
                            occupied.Add((win.Left, win.Top, w, h));
                        }
                        else
                        {
                            occupied.Add((ParseDouble(frame.X, 100), ParseDouble(frame.Y, 100),
                                          ParseDouble(frame.Width, 230), ParseDouble(frame.Height, 130)));
                        }
                    }
                    catch { }
                }

                var position = FindFreePosition(width, height, occupied, (wa.X, wa.Y, wa.Width, wa.Height));
                double x, y;
                if (position.HasValue)
                {
                    x = position.Value.X; y = position.Value.Y;
                }
                else
                {
                    // Last resort: small cascade from the work area's top-left (overlap allowed).
                    int cascadeIndex = placedThisRun.Count;
                    x = wa.X + 40 + cascadeIndex * 30;
                    y = wa.Y + 40 + cascadeIndex * 30;
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        $"App-Categorize: no free screen space for '{category}' frame — cascading at {x},{y} (may overlap).");
                }

                dynamic frameNew = FrameDataManager.CreateNewFrame(category, "Data", x, y);
                var dict = (IDictionary<string, object>)frameNew;
                dict["Title"] = category; // CreateNewFrame randomizes Data-frame titles
                dict["Width"] = width;
                dict["Height"] = height;
                dict["UnrolledHeight"] = height;

                placedThisRun.Add((x, y, width, height));
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"App-Categorize: created category frame '{category}' at {x},{y} ({width}x{height}).");
                return frameNew;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"App-Categorize: failed to create frame '{category}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Adds one classified desktop item to a category frame with the SAME
        /// mechanics as a desktop drop into a folder-backed frame:
        ///   CASE A — raw .exe: a new .lnk wrapping it is created in the frame's folder
        ///            (the exe itself is NEVER moved or deleted);
        ///   CASE B — existing .lnk/.url: MOVED verbatim into the frame's folder
        ///            (custom icons survive; nothing is left on the desktop);
        ///   CASE C — document/image: MOVED into the frame's folder; the item IS the file.
        /// Items store the absolute path of their backing file. Placement goes through
        /// the shared free-grid routine (first free cell).
        /// Runs on the UI thread; caller persists via SaveFrameData + reload.
        /// </summary>
        private static bool AddItemToFrame(dynamic frame, DesktopEntry item)
        {
            try
            {
                string frameFolder = FrameStore.GetFrameFolder(frame);
                bool isShortcut = item.Ext == ".lnk" || item.Ext == ".url";

                string fullTarget;
                bool isFolder = false;
                if (isShortcut || item.IsRawFile)
                {
                    // CASE B / CASE C: move the original exactly as-is.
                    fullTarget = FrameStore.MoveIntoFolder(frameFolder, item.Path, copy: false);
                    if (isShortcut && !item.IsWebLink && !string.IsNullOrEmpty(item.Target))
                        isFolder = Directory.Exists(item.Target);
                }
                else
                {
                    // CASE A: wrap the raw executable in a new shortcut (late-bound COM).
                    fullTarget = FrameStore.UniqueDestinationPath(frameFolder,
                        Path.GetFileNameWithoutExtension(item.Path) + ".lnk");
                    try
                    {
                        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                        dynamic shortcut = shell.CreateShortcut(fullTarget);
                        shortcut.TargetPath = item.Path;
                        shortcut.WorkingDirectory = Path.GetDirectoryName(item.Path) ?? "";
                        shortcut.Save();
                    }
                    catch (Exception comEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling,
                            $"App-Categorize: could not create shortcut for '{item.Path}': {comEx.Message}");
                        return false;
                    }
                }

                // Item JSON: absolute path of the backing file in the frame folder.
                var newItem = new Dictionary<string, object?>
                {
                    ["Filename"] = fullTarget,
                    ["IsFolder"] = isFolder,
                    ["IsLink"] = item.IsWebLink,
                    ["IsNetwork"] = Framemanager.IsNetworkPath(fullTarget),
                    ["DisplayName"] = item.DisplayName,
                    ["AlwaysRunAsAdmin"] = false
                };

                // Destination list: current tab when tabs are on, else the main list.
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";
                JArray? items = null;
                int currentTab = 0;
                JArray? tabs = null;
                if (tabsEnabled)
                {
                    try { currentTab = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0"); } catch { }
                    tabs = frame.Tabs as JArray;
                    if (tabs != null && currentTab >= 0 && currentTab < tabs.Count &&
                        tabs[currentTab] is JObject activeTab)
                    {
                        items = activeTab["Items"] as JArray;
                        if (items == null) { items = new JArray(); activeTab["Items"] = items; }
                    }
                }
                if (items == null)
                {
                    tabsEnabled = false;
                    items = frame.Items as JArray;
                    if (items == null) { items = new JArray(); frame.Items = items; }
                }

                newItem["DisplayOrder"] = items.Count;
                Framemanager.PlaceItemInFreeGrid(frame, items, newItem);
                items.Add(JObject.FromObject(newItem));

                // Tab0 and the main list mirror each other (see SynchronizeTab0Content).
                if (tabsEnabled && currentTab == 0)
                    frame.Items = JArray.FromObject(items.ToArray());

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling,
                    $"App-Categorize: '{item.DisplayName}' -> {item.Category}");
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling,
                    $"App-Categorize: failed to add '{item.Path}': {ex.Message}");
                return false;
            }
        }

        private static double ParseDouble(object? value, double fallback)
        {
            try
            {
                if (value == null) return fallback;
                if (value is double d) return double.IsNaN(d) ? fallback : d;
                return double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
            }
            catch { return fallback; }
        }

        private static void OnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
        }

        #endregion
    }
}
