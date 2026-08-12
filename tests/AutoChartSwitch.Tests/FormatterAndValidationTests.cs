using System.Globalization;
using AutoChartSwitch.Core;

namespace AutoChartSwitch.Tests;

public sealed class FormatterAndValidationTests
{
    [Theory]
    [InlineData("0.0", "")]
    [InlineData("0.5", "0.5")]
    [InlineData("5.0", "5")]
    [InlineData("5.5", "5.5")]
    [InlineData("10.0", "10.0")]
    [InlineData("99.9", "99.9")]
    public void DifficultyFormattingMatchesBroadcastRules(string input, string expected)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        Assert.Equal(expected, ChartFormatter.FormatDifficulty(value));
    }

    [Fact]
    public void CreditsUseExactTwoLineProjection()
    {
        Assert.Equal("Illust: Alice\nChart: Bob", ChartFormatter.FormatCredits("Alice", "Bob"));
    }

    [Fact]
    public void RequiredFieldsAndTenthsAreValidated()
    {
        var validator = new ChartValidator();
        var invalid = ValidChart() with { Artist = "", DifficultyNumber = 10.01m };
        var result = validator.Validate(invalid, "C:\\difficulty", false);

        Assert.False(result.CanSave);
        Assert.Contains(result.Issues, x => x.Field == nameof(ChartInfo.Artist));
        Assert.Contains(result.Issues, x => x.Field == nameof(ChartInfo.DifficultyNumber));
    }

    [Fact]
    public void MissingFilesWarnButDoNotPreventQueueSave()
    {
        var validator = new ChartValidator();
        var result = validator.Validate(ValidChart(), "C:\\missing-difficulty", true);

        Assert.True(result.CanSave);
        Assert.False(result.CanDisplay);
        Assert.Contains(result.Issues, x => x.Message == "File does not exist.");
    }

    [Fact]
    public void NegativeTechStatsAreRejected()
    {
        var result = new ChartValidator().Validate(ValidChart() with { TechStats = new() { Chip = -0.1m } }, "C:\\difficulty", false);

        Assert.False(result.CanSave);
        Assert.Contains(result.Issues, x => x.Field == "TechStats.Chip");
    }

    public static ChartInfo ValidChart(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = "Song",
        Artist = "Artist",
        Illustrator = "Illustrator",
        Charter = "Charter",
        DifficultyName = "Master",
        DifficultyNumber = 12.3m,
        JacketPath = Path.GetFullPath("jacket.png"),
        TechStats = new() { Chip = 30m, Tech = 40m, Stream = 50m, Chord = 60m, Burst = 70m },
        ShowcaseVideoPath = Path.GetFullPath("showcase.mp4")
    };
}
