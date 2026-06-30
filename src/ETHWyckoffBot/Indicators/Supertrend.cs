using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Indicators;

public class Supertrend
{
    private readonly int _atrPeriod;
    private readonly double _multiplier;
    private readonly ATR _atr;
    private decimal _previousUpperBand;
    private decimal _previousLowerBand;
    private TradeDirection _previousDirection;
    private bool _isInitialized;
    private readonly Queue<(decimal High, decimal Low, decimal Close)> _buffer = new();

    public Supertrend(int atrPeriod = 10, double multiplier = 3.0)
    {
        _atrPeriod = atrPeriod;
        _multiplier = multiplier;
        _atr = new ATR(atrPeriod);
    }

    public decimal Value { get; private set; }
    public TradeDirection Direction { get; private set; } = TradeDirection.None;
    public double AngleDegrees { get; private set; }
    public decimal ATRValue => _atr.Value;

    public void Update(decimal high, decimal low, decimal close)
    {
        _buffer.Enqueue((high, low, close));
        if (_buffer.Count > 50) _buffer.Dequeue();

        if (!_isInitialized)
        {
            _atr.Update(high, low, close);
            _isInitialized = true;
            _previousDirection = TradeDirection.Long;
            return;
        }

        _atr.Update(high, low, close);

        var atrValue = _atr.Value;
        var mid = (high + low) / 2;
        var upperBand = mid + (decimal)(_multiplier * (double)atrValue);
        var lowerBand = mid - (decimal)(_multiplier * (double)atrValue);

        TradeDirection newDirection;

        if (close > _previousUpperBand)
            newDirection = TradeDirection.Long;
        else if (close < _previousLowerBand)
            newDirection = TradeDirection.Short;
        else
            newDirection = _previousDirection;

        Value = newDirection == TradeDirection.Long ? lowerBand : upperBand;
        _previousUpperBand = upperBand;
        _previousLowerBand = lowerBand;
        Direction = newDirection;
        _previousDirection = newDirection;

        AngleDegrees = CalculateAngle();
    }

    private double CalculateAngle()
    {
        if (_buffer.Count < 5) return 0;

        var arr = _buffer.ToArray();
        var start = arr[^5];
        var end = arr[^1];

        var priceChange = (double)(end.Close - start.Close);
        var avgPrice = (double)(start.Close + end.Close) / 2;
        var normalizedChange = priceChange / avgPrice;
        var angle = Math.Atan2(normalizedChange * 100, 5) * (180.0 / Math.PI);
        return Math.Round(Math.Max(-90, Math.Min(90, angle)), 1);
    }

    public void Reset()
    {
        _atr.Reset();
        _isInitialized = false;
        Direction = TradeDirection.None;
        Value = 0;
        _buffer.Clear();
    }
}
