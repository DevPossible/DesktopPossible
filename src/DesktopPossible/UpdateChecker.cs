using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames;

/// <summary>
/// Asks GitHub for the latest published (non-pre-release) release and remembers whether it is
/// newer than the running build. Purely informational: the Options page shows a link when an
/// update exists. Fails silently offline.
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/DevPossible/DesktopPossible/releases/latest";
    public const string ReleasesPage = "https://github.com/DevPossible/DesktopPossible/releases";

    private static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(6);
    private static DateTime _lastCheckUtc = DateTime.MinValue;
    private static Task? _inFlight;

    public static Version? LatestVersion { get; private set; }
    public static string ReleaseUrl { get; private set; } = ReleasesPage;
    public static bool IsUpdateAvailable => LatestVersion != null && LatestVersion > CurrentVersion;

    /// <summary>The latest release's Windows installer asset, when the release has one.</summary>
    public static ReleaseAsset? Installer { get; private set; }

    /// <summary>A downloadable release asset as reported by the GitHub releases API.</summary>
    public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Raised (on a background thread) after a check completes and an update is available.</summary>
    public static event Action? UpdateFound;

    /// <summary>Schedules a check after <paramref name="delay"/> so startup is not slowed down.</summary>
    public static void Start(TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            await CheckAsync();
        });
    }

    /// <summary>Runs a check unless one ran recently or is already running.</summary>
    public static Task CheckAsync()
    {
        if (_inFlight is { IsCompleted: false }) return _inFlight;
        if (DateTime.UtcNow - _lastCheckUtc < RecheckInterval) return Task.CompletedTask;
        _inFlight = PerformCheckAsync();
        return _inFlight;
    }

    private static async Task PerformCheckAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"DesktopPossible/{CurrentVersion}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await client.GetStringAsync(LatestReleaseApi);
            var release = JObject.Parse(json);
            _lastCheckUtc = DateTime.UtcNow;

            if (!TryParseReleaseTag(release["tag_name"]?.ToString(), out var version)) return;
            LatestVersion = version;
            ReleaseUrl = release["html_url"]?.ToString() is { Length: > 0 } url ? url : ReleasesPage;
            Installer = SelectInstallerAsset(release["assets"] as JArray);

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"UpdateChecker: latest release {LatestVersion}, running {CurrentVersion}");

            if (IsUpdateAvailable) UpdateFound?.Invoke();
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, or GitHub down: try again next interval.
            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"UpdateChecker: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks the win-x64 MSI from a release's asset list (name, download URL, size and the
    /// GitHub-computed SHA-256 digest when present). Null when the release has no installer.
    /// </summary>
    public static ReleaseAsset? SelectInstallerAsset(JArray? assets)
    {
        if (assets == null) return null;
        foreach (var asset in assets)
        {
            string name = asset["name"]?.ToString() ?? "";
            if (!name.EndsWith("-win-x64.msi", StringComparison.OrdinalIgnoreCase)) continue;

            string? url = asset["browser_download_url"]?.ToString();
            if (string.IsNullOrEmpty(url)) continue;

            long size = asset["size"]?.Type == JTokenType.Integer ? asset["size"]!.Value<long>() : 0;
            return new ReleaseAsset(name, url, size, ParseSha256Digest(asset["digest"]?.ToString()));
        }
        return null;
    }

    /// <summary>Extracts the lowercase hex hash from a GitHub asset digest ("sha256:&lt;hex&gt;"); null for anything else.</summary>
    public static string? ParseSha256Digest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest == null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        if (hex.Length != 64) return null;
        foreach (char c in hex)
            if (!Uri.IsHexDigit(c)) return null;
        return hex;
    }

    /// <summary>Parses a release tag such as "v1.2.3" (or "1.2.3") into a comparable 4-part version.</summary>
    public static bool TryParseReleaseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string text = tag.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];

        // Drop any pre-release / build suffix ("1.2.3-beta.1", "1.2.3+abc").
        int cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];

        if (!Version.TryParse(text, out var parsed)) return false;

        // Assembly versions are always 4-part; normalise so 1.2.3 == 1.2.3.0.
        version = new Version(parsed.Major, Math.Max(0, parsed.Minor), Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}
