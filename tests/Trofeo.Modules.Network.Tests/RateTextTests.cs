namespace Trofeo.Modules.Network.Tests;

public class RateTextTests
{
    [Theory]
    [InlineData(0, "0", "kb/s")]
    [InlineData(106_250, "850", "kb/s")]
    [InlineData(525_000, "4,2", "Mb/s")]
    [InlineData(51_500_000, "412", "Mb/s")]
    [InlineData(155_000_000, "1,2", "Gb/s")]
    [InlineData(double.NaN, "0", "kb/s")]
    public void Format_Thresholds(double bytesPerSecond, string value, string unit) =>
        Assert.Equal((value, unit), RateText.Format(bytesPerSecond));

    [Theory]
    [InlineData(-5, "0", "kb/s")]
    [InlineData(double.NegativeInfinity, "0", "kb/s")]
    [InlineData(double.PositiveInfinity, "0", "kb/s")]
    [InlineData(124_900, "999", "kb/s")]            // 999,2 kb/s
    [InlineData(124_970, "1,0", "Mb/s")]            // 999,76 kb/s s'arrondirait à 1000 kb/s
    [InlineData(1_243_000, "9,9", "Mb/s")]
    [InlineData(1_245_000, "10", "Mb/s")]           // 9,96 Mb/s s'arrondirait à 10,0
    [InlineData(124_970_000, "1,0", "Gb/s")]        // 999,76 Mb/s s'arrondirait à 1000 Mb/s
    public void Format_RoundingNeverShowsAThousand(double bytesPerSecond, string value, string unit) =>
        Assert.Equal((value, unit), RateText.Format(bytesPerSecond));
}
