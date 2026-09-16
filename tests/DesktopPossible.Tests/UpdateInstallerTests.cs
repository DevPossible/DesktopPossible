using Desktop_Frames;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Headless tests for the "Update Now" plumbing: picking the MSI out of a GitHub release,
/// reading its digest, recognising our signer, and the msiexec hand-off script.
/// No network, no files, no shell.
/// </summary>
public class UpdateInstallerTests
{
    private static JArray Assets(params (string Name, long Size, string? Digest)[] assets)
    {
        var arr = new JArray();
        foreach (var (name, size, digest) in assets)
        {
            var o = new JObject
            {
                ["name"] = name,
                ["size"] = size,
                ["browser_download_url"] = $"https://github.com/DevPossible/DesktopPossible/releases/download/v9.9.9/{name}",
            };
            if (digest != null) o["digest"] = digest;
            arr.Add(o);
        }
        return arr;
    }

    private const string Hash = "9c42574ec1c1ac532d8d00c9d6f8dce516ecdc59bd36987e9c740b14c4e8d73a";

    [Fact]
    public void SelectInstallerAsset_PicksTheWinX64Msi_WithSizeAndDigest()
    {
        var assets = Assets(
            ("DesktopPossible-9.9.9-win-x64.zip", 82939358, "sha256:" + new string('0', 64)),
            ("DesktopPossible-9.9.9-win-x64.msi", 70402048, "sha256:" + Hash));

        var msi = UpdateChecker.SelectInstallerAsset(assets);

        msi.ShouldNotBeNull();
        msi.Name.ShouldBe("DesktopPossible-9.9.9-win-x64.msi");
        msi.Url.ShouldEndWith("/DesktopPossible-9.9.9-win-x64.msi");
        msi.Size.ShouldBe(70402048);
        msi.Sha256.ShouldBe(Hash);
    }

    [Fact]
    public void SelectInstallerAsset_WithoutDigest_StillReturnsAsset()
    {
        var msi = UpdateChecker.SelectInstallerAsset(Assets(("DesktopPossible-9.9.9-win-x64.msi", 123, null)));
        msi.ShouldNotBeNull();
        msi.Sha256.ShouldBeNull();
    }

    [Fact]
    public void SelectInstallerAsset_NoMsi_ReturnsNull()
    {
        UpdateChecker.SelectInstallerAsset(Assets(("DesktopPossible-9.9.9-win-x64.zip", 1, null))).ShouldBeNull();
        UpdateChecker.SelectInstallerAsset(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("sha256:" + Hash, Hash)]
    [InlineData("SHA256:9C42574EC1C1AC532D8D00C9D6F8DCE516ECDC59BD36987E9C740B14C4E8D73A", Hash)]
    [InlineData("sha512:abc", null)]
    [InlineData("sha256:tooshort", null)]
    [InlineData("sha256:zz42574ec1c1ac532d8d00c9d6f8dce516ecdc59bd36987e9c740b14c4e8d73a", null)]
    [InlineData(null, null)]
    public void ParseSha256Digest_AcceptsOnlyWellFormedSha256(string? digest, string? expected)
    {
        UpdateChecker.ParseSha256Digest(digest).ShouldBe(expected);
    }

    [Theory]
    [InlineData("CN=DevPossible LLC, O=DevPossible LLC, L=Montevallo, S=Alabama, C=US", true)]
    [InlineData("O=DevPossible LLC, CN=DevPossible LLC", true)]
    [InlineData("CN=\"DevPossible LLC\", C=US", true)]
    [InlineData("CN=DevPossible LLC Evil, O=Someone", false)]
    [InlineData("CN=Nikos Georgousis", false)]
    [InlineData("O=DevPossible LLC", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrustedSigner_RequiresExactDevPossibleCommonName(string? subject, bool expected)
    {
        UpdateInstaller.IsTrustedSigner(subject).ShouldBe(expected);
    }

    [Fact]
    public void BuildInstallScript_WaitsForPid_RunsMsiPassive_RelaunchesAndCleansUp()
    {
        string script = UpdateInstaller.BuildInstallScript(4242,
            @"C:\Users\me\AppData\Local\Temp\DesktopPossible-Update\DesktopPossible-9.9.9-win-x64.msi",
            @"C:\Program Files\DesktopPossible\DesktopPossible.exe", relaunch: true);

        script.ShouldContain("PID eq 4242");
        script.ShouldContain("goto wait");
        script.ShouldContain(@"msiexec /i ""C:\Users\me\AppData\Local\Temp\DesktopPossible-Update\DesktopPossible-9.9.9-win-x64.msi"" /passive /norestart");
        script.ShouldContain(@"start """" ""C:\Program Files\DesktopPossible\DesktopPossible.exe""");
        script.ShouldContain(@"del ""C:\Users\me\AppData\Local\Temp\DesktopPossible-Update\DesktopPossible-9.9.9-win-x64.msi""");
        script.ShouldContain(@"del ""%~f0""");
    }

    [Fact]
    public void BuildInstallScript_WithoutRelaunch_DoesNotStartTheApp()
    {
        string script = UpdateInstaller.BuildInstallScript(1, @"C:\t\u.msi", @"C:\p\DesktopPossible.exe", relaunch: false);
        script.ShouldNotContain("start \"\"");
        script.ShouldContain("msiexec /i");
    }
}
