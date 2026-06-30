namespace ETHWyckoffBot.Indicators;

public class CumulativeVolumeDelta
{
    private decimal _value;
    private decimal _deltaPerCandle;
    private readonly List<decimal> _recentDeltas = new();

    public decimal Value => _value;
    public decimal DeltaPerCandle => _deltaPerCandle;
    public bool IsPositive => _value > 0;
    public bool PositiveDivergence { get; private set; }

    public void AddTrade(decimal price, decimal volume, bool isBuy)
    {
        var signedVolume = isBuy ? volume : -volume;
        _value += signedVolume;
        _deltaPerCandle += signedVolume;
    }

    public void OnNewCandle()
    {
        _recentDeltas.Add(_deltaPerCandle);
        if (_recentDeltas.Count > 50) _recentDeltas.RemoveAt(0);
        _deltaPerCandle = 0;
    }

    public double GetScore()
    {
        if (_recentDeltas.Count < 5) return 0;
        var avg = _recentDeltas.TakeLast(5).Average();
        return Math.Clamp((double)(avg / 1000m), -1, 1);
    }

    public void Reset()
    {
        _value = 0;
        _deltaPerCandle = 0;
        _recentDeltas.Clear();
    }
}
