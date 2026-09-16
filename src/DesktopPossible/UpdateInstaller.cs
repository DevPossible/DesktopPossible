using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop_Frames;

/// <summary>
/// Downloads the latest release's MSI, proves it is the file GitHub published (size + SHA-256)
/// and that it is Authenticode-signed by DevPossible, then hands it to msiexec. The running
/// exe would hold Windows Installer's "files in use" lock, so the hand-off is a small script
/// that waits for this process to exit, runs the installer, and (optionally) relaunches the app.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>Subject CN every shipped binary is signed with (Azure Trusted Signing, DevPossible account).</summary>
    public const string TrustedSignerCommonName = "DevPossible LLC";

    /// <summary>Where downloaded installers land; cleared by the hand-off script after install.</summary>
    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "DesktopPossible-Update");

    /// <summary>Streams the asset to <paramref name="destination"/>, reporting (bytesDone, bytesTotal).</summary>
    public static async Task DownloadAsync(UpdateChecker.ReleaseAsset asset, string destination,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DesktopPossible/{UpdateChecker.CurrentVersion}");

        using var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        var buffer = new byte[1 << 16];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report((done, total));
        }
    }

    /// <summary>
    /// Checks the downloaded file against the release metadata and its Authenticode signature.
    /// Returns null when everything checks out, otherwise a user-readable reason.
    /// </summary>
    public static string? Verify(string path, UpdateChecker.ReleaseAsset asset)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return "The downloaded file is missing.";
        if (asset.Size > 0 && info.Length != asset.Size)
            return $"Size mismatch: expected {asset.Size:N0} bytes, got {info.Length:N0}.";

        if (asset.Sha256 != null)
        {
            string actual = ComputeSha256(path);
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                return "The file's SHA-256 hash does not match the published release.";
        }

        if (!VerifyAuthenticode(path, out string? subject))
            return "The installer's digital signature is missing or not trusted by Windows.";

        if (!IsTrustedSigner(subject))
            return $"The installer is signed by an unexpected publisher ({subject ?? "unknown"}).";

        return null;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>True when an X.500 subject names DevPossible LLC as its CN.</summary>
    public static bool IsTrustedSigner(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        foreach (var part in subject.Split(','))
        {
            var kv = part.Trim();
            if (kv.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kv[3..].Trim().Trim('"'), TrustedSignerCommonName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Writes the hand-off script and starts it hidden, then shuts the app down. The script
    /// waits for this process to exit, runs msiexec in passive mode (progress UI, no prompts),
    /// relaunches the app when asked, and deletes the installer and itself.
    /// </summary>
    public static void LaunchInstaller(string msiPath, bool relaunch)
    {
        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DesktopPossible.exe");
        string script = BuildInstallScript(Environment.ProcessId, msiPath, exePath, relaunch);
        string scriptPath = Path.Combine(DownloadFolder, "install-update.cmd");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
            $"UpdateInstaller: handing off to {msiPath} (relaunch={relaunch})");

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
    }

    /// <summary>Pure: the cmd script that performs the install once process <paramref name="pid"/> has exited.</summary>
    public static string BuildInstallScript(int pid, string msiPath, string exePath, bool relaunch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine(":wait");
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find /I \"DesktopPossible\" >nul");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        sb.AppendLine($"msiexec /i \"{msiPath}\" /passive /norestart");
        sb.AppendLine("set RC=%ERRORLEVEL%");
        if (relaunch)
            sb.AppendLine($"if \"%RC%\"==\"0\" start \"\" \"{exePath}\"");
        sb.AppendLine($"del \"{msiPath}\" >nul 2>&1");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
        return sb.ToString();
    }

    // ---- WinVerifyTrust: does Windows itself accept the file's Authenticode signature? ----

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x40;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    /// <summary>Leading fields of CRYPT_PROVIDER_CERT; only pCert (the verified signer's CERT_CONTEXT) is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CRYPT_PROVIDER_CERT_HEAD
    {
        public uint cbStruct;
        public IntPtr pCert;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr pProvData, uint idxSigner, [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner, uint idxCounterSigner);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr pSgnr, uint idxCert);

    /// <summary>
    /// Asks Windows to validate the file's Authenticode signature and chain. On success,
    /// <paramref name="signerSubject"/> is the subject of the signing certificate taken from
    /// the chain Windows just verified (not re-read from the file).
    /// </summary>
    public static bool VerifyAuthenticode(string path, out string? signerSubject)
    {
        signerSubject = null;
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT,
            };
            Marshal.StructureToPtr(data, pData, false);

            uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);
            data = Marshal.PtrToStructure<WINTRUST_DATA>(pData);

            if (result == 0)
            {
                try
                {
                    IntPtr provData = WTHelperProvDataFromStateData(data.hWVTStateData);
                    IntPtr signer = provData == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provData, 0, false, 0);
                    IntPtr provCert = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
                    if (provCert != IntPtr.Zero)
                    {
                        IntPtr certContext = Marshal.PtrToStructure<CRYPT_PROVIDER_CERT_HEAD>(provCert).pCert;
                        if (certContext != IntPtr.Zero)
                        {
                            // The handle is owned by the verifier state; the copy is ours to dispose.
                            using var cert = new X509Certificate2(certContext);
                            signerSubject = cert.Subject;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"UpdateInstaller: signer read failed: {ex.Message}");
                }
            }

            // Release the verifier's state handle regardless of the outcome.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(data, pData, false);
            WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);

            if (result != 0)
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"UpdateInstaller: WinVerifyTrust returned 0x{result:X8} for {path}");
            return result == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
            Marshal.FreeHGlobal(pData);
        }
    }
}
