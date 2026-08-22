using System;
using Desktop_Frames;
using Shouldly;
using Xunit;

namespace DesktopPossible.Tests;

public class TextTemplateTests
{
    private static System.Collections.Generic.Dictionary<string, TextTemplate.TokenResolver> Table()
    {
        var t = TextTemplate.NewTable();
        t["Name"] = _ => "DESKTOP-01";
        t["Date"] = arg => arg == null ? "Friday" : $"fmt:{arg}";
        t["Boom"] = _ => throw new InvalidOperationException("nope");
        return t;
    }

    [Fact]
    public void Expand_ReplacesKnownTokens()
    {
        TextTemplate.Expand("Host: {Name}", Table()).ShouldBe("Host: DESKTOP-01");
    }

    [Fact]
    public void Expand_IsCaseInsensitive()
    {
        TextTemplate.Expand("{name} {NAME}", Table()).ShouldBe("DESKTOP-01 DESKTOP-01");
    }

    [Fact]
    public void Expand_PassesTheArgumentAfterTheColon()
    {
        TextTemplate.Expand("{Date} / {Date:yyyy-MM-dd}", Table()).ShouldBe("Friday / fmt:yyyy-MM-dd");
    }

    [Fact]
    public void Expand_LeavesUnknownTokensVerbatim()
    {
        TextTemplate.Expand("{Nope} and {Name}", Table()).ShouldBe("{Nope} and DESKTOP-01");
    }

    [Fact]
    public void Expand_DoubledBracesAreLiterals()
    {
        TextTemplate.Expand("{{Name}} = {Name}", Table()).ShouldBe("{Name} = DESKTOP-01");
    }

    [Fact]
    public void Expand_ToleratesUnterminatedBrace()
    {
        TextTemplate.Expand("open {Name and {Name}", Table()).ShouldBe("open {Name and DESKTOP-01");
    }

    [Fact]
    public void Expand_ReportsAResolverFailureInline()
    {
        TextTemplate.Expand("x {Boom} y", Table()).ShouldBe("x <Boom: nope> y");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("plain text", "plain text")]
    public void Expand_HandlesEmptyAndPlainInput(string? template, string expected)
    {
        TextTemplate.Expand(template, Table()).ShouldBe(expected);
    }

    [Fact]
    public void SystemInfoTokens_ReferenceListMatchesTheBuiltTable()
    {
        var table = SystemInfoTokens.Build();
        foreach (var (token, _) in SystemInfoTokens.Reference)
        {
            string name = token.Trim('{', '}');
            int colon = name.IndexOf(':');
            if (colon >= 0) name = name[..colon];
            table.ContainsKey(name).ShouldBeTrue($"{token} is documented but has no resolver");
        }
    }

    [Theory]
    [InlineData(0, 0, 12, "12m")]
    [InlineData(0, 4, 12, "4h 12m")]
    [InlineData(3, 4, 12, "3d 4h 12m")]
    public void FormatUptime_UsesTheLargestUnitPresent(int d, int h, int m, string expected)
    {
        SystemInfoTokens.FormatUptime(new TimeSpan(d, h, m, 0)).ShouldBe(expected);
    }
}
