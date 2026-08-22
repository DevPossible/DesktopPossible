using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Desktop_Frames
{
    /// <summary>
    /// Template expansion for Text frames: <c>{Token}</c> and <c>{Token:arg}</c> placeholders
    /// (case-insensitive) resolved through a token table; <c>{{</c> / <c>}}</c> are literal
    /// braces; unknown tokens are left verbatim so a typo is visible on the desktop.
    /// Pure — no WPF, no IO — so it is unit-tested headless.
    /// </summary>
    public static class TextTemplate
    {
        /// <summary>A token resolver: receives the optional argument after ':' (or null).</summary>
        public delegate string TokenResolver(string? argument);

        public static string Expand(string? template, IReadOnlyDictionary<string, TokenResolver> tokens)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var sb = new StringBuilder(template.Length + 64);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{')
                {
                    if (i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                    int close = template.IndexOf('}', i + 1);
                    if (close > i + 1)
                    {
                        string body = template.Substring(i + 1, close - i - 1);
                        int colon = body.IndexOf(':');
                        string name = colon >= 0 ? body.Substring(0, colon) : body;
                        string? arg = colon >= 0 ? body.Substring(colon + 1) : null;
                        if (tokens.TryGetValue(name.Trim(), out var resolver))
                        {
                            string value;
                            try { value = resolver(arg) ?? ""; }
                            catch (Exception ex) { value = $"<{name}: {ex.Message}>"; }
                            sb.Append(value);
                            i = close + 1;
                            continue;
                        }
                    }
                    sb.Append('{');
                    i++;
                    continue;
                }
                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Case-insensitive token table builder.</summary>
        public static Dictionary<string, TokenResolver> NewTable() => new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bginfo-style token set available to Text frames. Values are read live at each
    /// refresh; anything that fails resolves to a short error marker instead of throwing.
    /// </summary>
    public static class SystemInfoTokens
    {
        /// <summary>(Token, description) pairs shown in the editor's reference list.</summary>
        public static readonly (string Token, string Description)[] Reference =
        {
            ("{ComputerName}", "Machine name"),
            ("{UserName}", "Logged-on user"),
            ("{Domain}", "User domain / workgroup"),
            ("{OS}", "Windows edition and version"),
            ("{OSVersion}", "Version number (e.g. 10.0.26200)"),
            ("{Architecture}", "OS architecture (x64, Arm64)"),
            ("{CPU}", "Processor name"),
            ("{Cores}", "Logical processor count"),
            ("{CPUUsage}", "Processor load since the last refresh (%)"),
            ("{RAM}", "Installed memory (GB)"),
            ("{RAMUsed}", "Memory in use (GB)"),
            ("{RAMFree}", "Memory available (GB)"),
            ("{RAMUsage}", "Memory in use (%)"),
            ("{IP}", "Primary IPv4 address"),
            ("{ExternalIP}", "Public IPv4 address (looked up online, cached 10 min)"),
            ("{MAC}", "Primary adapter MAC address"),
            ("{Disks}", "One line per fixed disk: letter, free and total space"),
            ("{Uptime}", "Time since boot"),
            ("{BootTime}", "Last boot, local time"),
            ("{Date}", "Today (long); {Date:yyyy-MM-dd} for a custom format"),
            ("{Time}", "Now (short); {Time:HH:mm:ss} for a custom format"),
            ("{Now:format}", "Date and time with a .NET format string"),
            ("{FreeSpace:C}", "Free space on a drive letter (GB)"),
            ("{TotalSpace:C}", "Total size of a drive letter (GB)"),
            ("{Profile}", "Active DesktopPossible profile"),
            ("{AppVersion}", "DesktopPossible version"),
        };

        public static Dictionary<string, TextTemplate.TokenResolver> Build()
        {
            var t = TextTemplate.NewTable();
            t["ComputerName"] = _ => Environment.MachineName;
            t["UserName"] = _ => Environment.UserName;
            t["Domain"] = _ => Environment.UserDomainName;
            t["OS"] = _ => OsName();
            t["OSVersion"] = _ => Environment.OSVersion.Version.ToString();
            t["Architecture"] = _ => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
            t["CPU"] = _ => CpuName();
            t["Cores"] = _ => Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
            t["CPUUsage"] = _ => CpuUsagePercent().ToString("0", CultureInfo.CurrentCulture) + "%";
            t["RAM"] = _ => Gb((long)Memory().Total);
            t["RAMUsed"] = _ => { var m = Memory(); return Gb((long)(m.Total - m.Available)); };
            t["RAMFree"] = _ => Gb((long)Memory().Available);
            t["RAMUsage"] = _ => { var m = Memory(); return m.Total == 0 ? "n/a" : ((m.Total - m.Available) * 100.0 / m.Total).ToString("0", CultureInfo.CurrentCulture) + "%"; };
            t["IP"] = _ => PrimaryIPv4();
            t["ExternalIP"] = _ => ExternalIPv4();
            t["MAC"] = _ => PrimaryMac();
            t["Disks"] = _ => DiskList();
            t["Uptime"] = _ => FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
            t["BootTime"] = a => DateTime.Now.AddMilliseconds(-Environment.TickCount64).ToString(a ?? "g", CultureInfo.CurrentCulture);
            t["Date"] = a => DateTime.Now.ToString(a ?? "D", CultureInfo.CurrentCulture);
            t["Time"] = a => DateTime.Now.ToString(a ?? "t", CultureInfo.CurrentCulture);
            t["Now"] = a => DateTime.Now.ToString(a ?? "G", CultureInfo.CurrentCulture);
            t["FreeSpace"] = a => DriveGb(a, free: true);
            t["TotalSpace"] = a => DriveGb(a, free: false);
            t["Profile"] = _ => ProfileManager.CurrentProfileName ?? "";
            t["AppVersion"] = _ => typeof(SystemInfoTokens).Assembly.GetName().Version?.ToString(3) ?? "";
            return t;
        }

        /// <summary>Human-readable uptime: "3d 4h 12m" / "4h 12m" / "12m".</summary>
        public static string FormatUptime(TimeSpan span)
        {
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
            if (span.TotalHours >= 1) return $"{span.Hours}h {span.Minutes}m";
            return $"{span.Minutes}m";
        }

        private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.#", CultureInfo.CurrentCulture) + " GB";

        // ---- CPU load: delta of kernel+user vs idle time between two reads (GetSystemTimes) ----

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        private static long _lastIdle, _lastBusy;
        private static double _lastCpuPercent;
        private static readonly object _cpuLock = new();

        /// <summary>CPU utilization since the previous call (first call primes and returns 0).</summary>
        public static double CpuUsagePercent()
        {
            lock (_cpuLock)
            {
                if (!GetSystemTimes(out long idle, out long kernel, out long user)) return _lastCpuPercent;
                long busy = kernel + user; // kernel includes idle
                long dIdle = idle - _lastIdle, dBusy = busy - _lastBusy;
                bool primed = _lastBusy != 0;
                _lastIdle = idle; _lastBusy = busy;
                if (!primed || dBusy <= 0) return _lastCpuPercent;
                _lastCpuPercent = Math.Clamp((dBusy - dIdle) * 100.0 / dBusy, 0, 100);
                return _lastCpuPercent;
            }
        }

        // ---- Memory: GlobalMemoryStatusEx ----

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private static (ulong Total, ulong Available) Memory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m)) return (m.ullTotalPhys, m.ullAvailPhys);
            long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return ((ulong)total, 0);
        }

        // ---- External IP: fetched in the background, cached, never blocks a refresh ----

        private static string _externalIp = "…";
        private static DateTime _externalIpFetched = DateTime.MinValue;
        private static int _externalIpFetching;
        private static readonly System.Net.Http.HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        /// <summary>Raised (on a worker thread) when a background lookup changed the cached external IP.</summary>
        public static event Action? ExternalIpChanged;

        private static string ExternalIPv4()
        {
            if (DateTime.UtcNow - _externalIpFetched > TimeSpan.FromMinutes(10)
                && System.Threading.Interlocked.CompareExchange(ref _externalIpFetching, 1, 0) == 0)
            {
                _ = FetchExternalIpAsync();
            }
            return _externalIp;
        }

        private static async System.Threading.Tasks.Task FetchExternalIpAsync()
        {
            try
            {
                string ip = (await _http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false)).Trim();
                if (System.Net.IPAddress.TryParse(ip, out _) && ip != _externalIp)
                {
                    _externalIp = ip;
                    ExternalIpChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                if (_externalIp == "…") _externalIp = "n/a";
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"External IP lookup failed: {ex.Message}");
            }
            finally
            {
                _externalIpFetched = DateTime.UtcNow;
                System.Threading.Interlocked.Exchange(ref _externalIpFetching, 0);
            }
        }

        // ---- Disks ----

        private static string DiskList()
        {
            var lines = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" ({d.VolumeLabel})";
                    lines.Add($"{d.Name.TrimEnd('\\')}{label}  {Gb(d.AvailableFreeSpace)} free of {Gb(d.TotalSize)}");
                }
                catch { }
            }
            return lines.Count == 0 ? "n/a" : string.Join(Environment.NewLine, lines);
        }

        private static string DriveGb(string? letter, bool free)
        {
            string name = string.IsNullOrWhiteSpace(letter) ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C" : letter.Trim();
            if (name.Length == 1) name += ":\\";
            var drive = new DriveInfo(name);
            return Gb(free ? drive.AvailableFreeSpace : drive.TotalSize);
        }

        private static string OsName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string? product = key?.GetValue("ProductName")?.ToString();
                string? display = key?.GetValue("DisplayVersion")?.ToString();
                int build = Environment.OSVersion.Version.Build;
                if (!string.IsNullOrEmpty(product))
                {
                    if (build >= 22000 && product.Contains("Windows 10")) product = product.Replace("Windows 10", "Windows 11");
                    return string.IsNullOrEmpty(display) ? product : $"{product} {display}";
                }
            }
            catch { }
            return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        }

        private static string CpuName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string? name = key?.GetValue("ProcessorNameString")?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ");
            }
            catch { }
            return "Unknown CPU";
        }

        private static NetworkInterface? PrimaryAdapter() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count)
                .FirstOrDefault();

        private static string PrimaryIPv4()
        {
            var nic = PrimaryAdapter();
            var ip = nic?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            return ip?.ToString() ?? "n/a";
        }

        private static string PrimaryMac()
        {
            var mac = PrimaryAdapter()?.GetPhysicalAddress().GetAddressBytes();
            return mac == null || mac.Length == 0 ? "n/a" : string.Join(":", mac.Select(b => b.ToString("X2")));
        }
    }
}
