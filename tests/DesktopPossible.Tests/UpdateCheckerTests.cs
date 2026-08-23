using Desktop_Frames;
using Xunit;

namespace DesktopPossible.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("V0.9.0", "0.9.0.0")]
    [InlineData("v1.2.3-beta.1", "1.2.3.0")]
    [InlineData("v1.2.3+build.7", "1.2.3.0")]
    [InlineData("v1.2", "1.2.0.0")]
    [InlineData("v1.2.3.4", "1.2.3.4")]
    public void TryParseReleaseTag_WithValidTag_ReturnsNormalisedFourPartVersion(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseReleaseTag(tag, out var version));
        Assert.Equal(new System.Version(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("release-1")]
    public void TryParseReleaseTag_WithInvalidTag_ReturnsFalse(string? tag)
    {
        Assert.False(UpdateChecker.TryParseReleaseTag(tag, out _));
    }

    [Fact]
    public void TryParseReleaseTag_NormalisedVersion_ComparesAgainstAssemblyStyleVersion()
    {
        UpdateChecker.TryParseReleaseTag("v1.0.0", out var release);
        Assert.True(release > new System.Version(0, 9, 0, 0));
        Assert.False(release > new System.Version(1, 0, 0, 0));
    }
}
