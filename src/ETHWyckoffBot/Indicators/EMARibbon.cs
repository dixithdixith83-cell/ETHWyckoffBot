namespace ETHWyckoffBot.Indicators;

public class EMARibbon
{
    public EMA Ema9 { get; } = new(9);
    public EMA Ema21 { get; } = new(21);
    public EMA Ema50 { get; } = new(50);
    public EMA Ema200 { get; } = new(200);

    private decimal _previousSpread;

    public bool IsBullishStacked => Ema9.Value > Ema21.Value && Ema21.Value > Ema50.Value && Ema50.Value > Ema200.Value;
    public bool IsBearishStacked => Ema9.Value < Ema21.Value && Ema21.Value < Ema50.Value && Ema50.Value < Ema200.Value;
    public bool IsExpanding
    {
        get
        {
            var spread = Ema9.Value - Ema200.Value;
            var result = _previousSpread == 0 || spread > _previousSpread;
            _previousSpread = spread;
            return result;
        }
    }

    public void Update(decimal price)
    {
        Ema9.Update(price);
        Ema21.Update(price);
        Ema50.Update(price);
        Ema200.Update(price);
    }

    public void Reset()
    {
        Ema9.Reset();
        Ema21.Reset();
        Ema50.Reset();
        Ema200.Reset();
        _previousSpread = 0;
    }
}
