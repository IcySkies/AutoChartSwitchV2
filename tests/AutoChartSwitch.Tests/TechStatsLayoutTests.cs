using AutoChartSwitch.App;

namespace AutoChartSwitch.Tests;

public sealed class TechStatsLayoutTests
{
    [Fact]
    public void GeometryMatchesAuthoritativeReferenceCapture()
    {
        Assert.Equal(508, TechStatsLayout.Width);
        Assert.Equal(200, TechStatsLayout.Height);
        Assert.Equal(189, TechStatsLayout.BarLeft);
        Assert.Equal(25, TechStatsLayout.BarHeight);
        Assert.Equal(240, TechStatsLayout.GetBarWidth(200));
        Assert.Equal(438, TechStatsLayout.GetNumberLeft(3));
    }

    [Fact]
    public void RowSpacingMatchesFiveAndSixRowLayouts()
    {
        Assert.Equal([0d, 35d, 70d, 105d, 140d, 175d],
            Enumerable.Range(0, 6).Select(row => TechStatsLayout.GetRowTop(row, true)));
        Assert.Equal([0d, 40d, 80d, 120d, 160d],
            Enumerable.Range(0, 5).Select(row => TechStatsLayout.GetRowTop(row, false)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(41, 50)]
    [InlineData(139, 170)]
    [InlineData(158, 190)]
    [InlineData(161, 195)]
    [InlineData(200, 240)]
    [InlineData(400, 240)]
    public void BarWidthsUseFivePixelGameMakerRasterSteps(double value, double expected)
    {
        Assert.Equal(expected, TechStatsLayout.GetBarWidth(value));
    }

    [Fact]
    public void BarColorUsesGameMakerHsvRamp()
    {
        var start = TechStatsPalette.GetBarColor(0);
        var end = TechStatsPalette.GetBarColor(200);

        Assert.Equal((byte)196, start.R);
        Assert.Equal((byte)255, start.G);
        Assert.Equal((byte)55, start.B);
        Assert.Equal((byte)255, end.R);
        Assert.Equal((byte)55, end.G);
        Assert.Equal((byte)55, end.B);
    }

    [Theory]
    [InlineData(399.49, false)]
    [InlineData(399.5, true)]
    [InlineData(400, true)]
    public void RainbowNumberStartsAtRounded400(decimal value, bool expected)
    {
        var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);

        Assert.Equal(expected, TechStatsPalette.UsesRainbowNumber(rounded));
    }

    [Theory]
    [InlineData(-0.4, "0")]
    [InlineData(-1.4, "-1")]
    [InlineData(-400, "-400")]
    public void SignedNumbersKeepTheirMinusSign(decimal value, string expected) =>
        Assert.Equal(expected, TechStatsLayout.FormatNumber(decimal.Round(value, 0, MidpointRounding.AwayFromZero)));

    [Fact]
    public void MinusGlyphUsesTheCenteredDigitCellGeometry()
    {
        var bounds = TechStatsLayout.GetMinusBounds(100, 20);

        Assert.Equal(new System.Windows.Rect(108, 31, 5, 3), bounds);
        Assert.Equal(TechStatsLayout.GetNumberLeft(2), TechStatsLayout.GetNumberLeft(1) - TechStatsLayout.DigitAdvance);
    }

    [Fact]
    public void NegativeBarsAreClampedToZero()
    {
        Assert.Equal(0, TechStatsLayout.GetBarWidth(-25));
        Assert.True(TechStatsPalette.UsesRainbowNumber(-400));
    }

    [Fact]
    public void CopyValuesClearsStatsThatAreMissingFromTheNewChart()
    {
        var targets = new[] { 10d, 20d, 30d };
        var display = new[] { 10m, 20m, 30m };

        TechStatsLayout.CopyValues([7m], targets, display);

        Assert.Equal([7d, 0d, 0d], targets);
        Assert.Equal([7m, 0m, 0m], display);
    }

    [Fact]
    public void RainbowHueUsesGameCurrentTimeRate()
    {
        Assert.Equal(TechStatsPalette.GetRainbowColor(0), TechStatsPalette.GetRainbowColor(510));
        Assert.NotEqual(TechStatsPalette.GetRainbowColor(0), TechStatsPalette.GetRainbowColor(255));
    }
}
