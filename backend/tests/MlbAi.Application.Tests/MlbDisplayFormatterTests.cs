using MlbAi.Application.Services;
using Xunit;

namespace MlbAi.Application.Tests;

public sealed class MlbDisplayFormatterTests
{
    [Fact]
    public void FormatRecord_ReturnsWinsAndLosses()
    {
        var result = MlbDisplayFormatter.FormatRecord(88, 61);

        Assert.Equal("88-61", result);
    }

    [Theory]
    [InlineData("R", "Regular Season")]
    [InlineData("S", "Spring Training")]
    [InlineData("F", "Wild Card")]
    [InlineData("D", "Division Series")]
    [InlineData("L", "League Championship")]
    [InlineData("W", "World Series")]
    [InlineData("X", "X")]
    [InlineData(null, null)]
    public void FormatGameType_MapsKnownMlbCodes(string? gameType, string? expected)
    {
        var result = MlbDisplayFormatter.FormatGameType(gameType);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("N", "No")]
    [InlineData("Y", "Yes")]
    [InlineData("S", "Split")]
    [InlineData("custom", "custom")]
    [InlineData(null, null)]
    public void FormatDoubleHeader_MapsKnownMlbCodes(string? doubleHeader, string? expected)
    {
        var result = MlbDisplayFormatter.FormatDoubleHeader(doubleHeader);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("day", "Day")]
    [InlineData("NIGHT", "Night")]
    [InlineData("late afternoon", "Late Afternoon")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void FormatTitleCase_ReturnsReadableTitleCase(string? value, string? expected)
    {
        var result = MlbDisplayFormatter.FormatTitleCase(value);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, "s")]
    [InlineData(1, "")]
    [InlineData(2, "s")]
    public void Pluralize_ReturnsEnglishPluralSuffix(int count, string expected)
    {
        var result = MlbDisplayFormatter.Pluralize(count);

        Assert.Equal(expected, result);
    }
}
