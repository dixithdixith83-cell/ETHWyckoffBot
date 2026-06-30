namespace ETHWyckoffBot.Indicators;

public class OrderBookImbalance
{
    public double Ratio { get; private set; } = 1.0;

    public void Update(List<(decimal Price, decimal Volume)> bids, List<(decimal Price, decimal Volume)> asks, int levels = 10)
    {
        var bidVol = bids.Take(levels).Sum(b => (double)(b.Price * b.Volume));
        var askVol = asks.Take(levels).Sum(a => (double)(a.Price * a.Volume));
        Ratio = askVol > 0 ? bidVol / askVol : 1.0;
    }

    public double GetScore() => Math.Clamp((Ratio - 1.0) * 2, -1, 1);
}
