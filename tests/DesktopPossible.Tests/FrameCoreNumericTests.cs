using System;
using System.Globalization;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

/// <summary>
/// Tests for Framemanager.ToDoubleInvariant — the culture-safe parser used for all
/// persisted frame geometry (Height, UnrolledHeight, Width). frames.json values must
/// round-trip identically on comma-decimal locales (de-DE, el-GR...): "130.5" must
/// never become 1305.
/// </summary>
public class FrameCoreNumericTests
{
    private static double ParseUnder(CultureInfo culture, object value, double fallback)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return Framemanager.ToDoubleInvariant(value, fallback);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("130.5", 130.5)]
    [InlineData("28", 28)]
    [InlineData("-12.25", -12.25)]
    [InlineData("0", 0)]
    public void InvariantStrings_ParseTheSame_OnCommaDecimalLocale(string text, double expected)
    {
        ParseUnder(new CultureInfo("de-DE"), text, -1).ShouldBe(expected);
        ParseUnder(CultureInfo.InvariantCulture, text, -1).ShouldBe(expected);
    }

    [Fact]
    public void InvariantString_IsNotMisreadAsThousands_OnGermanLocale()
    {
        // The original bug: de-DE Convert.ToDouble("130.5") == 1305 (dot = thousands separator).
        ParseUnder(new CultureInfo("de-DE"), "130.5", -1).ShouldBe(130.5);
    }

    [Fact]
    public void LegacyCommaDecimal_FallsBackToCurrentCulture()
    {
        // Files written by older builds on a comma-decimal locale may contain "130,5".
        ParseUnder(new CultureInfo("de-DE"), "130,5", -1).ShouldBe(130.5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    public void InvalidValues_ReturnFallback(object? value)
    {
        Framemanager.ToDoubleInvariant(value!, 130).ShouldBe(130);
    }

    [Fact]
    public void NumericValues_PassThroughWithoutStringRoundTrip()
    {
        Framemanager.ToDoubleInvariant(130.5d, -1).ShouldBe(130.5);
        Framemanager.ToDoubleInvariant(130.5f, -1).ShouldBe(130.5, 0.001);
        Framemanager.ToDoubleInvariant(28, -1).ShouldBe(28);
        Framemanager.ToDoubleInvariant(28L, -1).ShouldBe(28);
        Framemanager.ToDoubleInvariant(130.5m, -1).ShouldBe(130.5);
    }

    [Fact]
    public void JsonNetValues_ParseViaToString()
    {
        var jValue = new Newtonsoft.Json.Linq.JValue(130.5);
        ParseUnder(new CultureInfo("de-DE"), jValue, -1).ShouldBe(130.5);
    }
}
