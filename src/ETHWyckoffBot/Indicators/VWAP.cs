namespace ETHWyckoffBot.Indicators;

public class VWAP
{
    private decimal _cumulativeTPV;
    private decimal _cumulativeVolume;

    public decimal Value { get; private set; }

    public void Update(decimal high, decimal low, decimal close, decimal volume)
    {
        var typicalPrice = (high + low + close) / 3;
        _cumulativeTPV += typicalPrice * volume;
        _cumulativeVolume += volume;

        if (_cumulativeVolume > 0)
            Value = _cumulativeTPV / _cumulativeVolume;
    }

    public void Reset()
    {
        _cumulativeTPV = 0;
        _cumulativeVolume = 0;
        Value = 0;
    }
}
