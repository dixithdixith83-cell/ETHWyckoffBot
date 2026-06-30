namespace ETHWyckoffBot.Indicators;

public static class TrueRange
{
    public static decimal Calculate(decimal high, decimal low, decimal previousClose)
    {
        var hl = high - low;
        var hc = Math.Abs(high - previousClose);
        var lc = Math.Abs(low - previousClose);
        return Math.Max(hl, Math.Max(hc, lc));
    }
}
