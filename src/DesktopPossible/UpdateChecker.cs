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
