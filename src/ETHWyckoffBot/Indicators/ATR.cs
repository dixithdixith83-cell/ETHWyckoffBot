namespace ETHWyckoffBot.Indicators;

public class ATR
{
    private readonly int _period;
    private readonly Queue<decimal> _trueRanges = new();
    private decimal _previousClose;
    private bool _isInitialized;

    public ATR(int period = 14)
    {
        _period = period;
    }

    public decimal Value { get; private set; }

    public void Update(decimal high, decimal low, decimal close)
    {
        if (!_isInitialized)
        {
            _previousClose = close;
            _isInitialized = true;
            return;
        }

        var tr = TrueRange.Calculate(high, low, _previousClose);
        _previousClose = close;

        if (_trueRanges.Count < _period)
        {
            _trueRanges.Enqueue(tr);
            Value = _trueRanges.Average();
            return;
        }

        // EMA of TR for ATR after warmup
        if (_trueRanges.Count == _period)
        {
            var initialAtr = _trueRanges.Average();
            Value = initialAtr;
            _trueRanges.Clear();
        }

        Value = ((Value * (_period - 1)) + tr) / _period;
    }

    public void Reset()
    {
        _trueRanges.Clear();
        _isInitialized = false;
        Value = 0;
    }
}
