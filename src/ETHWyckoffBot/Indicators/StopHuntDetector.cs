using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Indicators;

public class StopHuntDetector
{
    private readonly int _lookback;
    private readonly decimal _breakPercent;
    private readonly List<Candle> _candles = new();

    public bool BuySignal { get; private set; }
    public bool SellSignal { get; private set; }
    public decimal HuntLevel { get; private set; }
    public decimal HuntVolume { get; private set; }

    public StopHuntDetector(int lookback = 20, decimal breakPercent = 0.003m)
    {
        _lookback = lookback;
        _breakPercent = breakPercent;
    }

    public void Update(Candle candle)
    {
        _candles.Add(candle);
        if (_candles.Count > _lookback + 5)
            _candles.RemoveAt(0);

        BuySignal = false;
        SellSignal = false;
        HuntLevel = 0;
        HuntVolume = 0;

        if (_candles.Count < _lookback + 2) return;

        var recent = _candles.TakeLast(_lookback).ToList();
        var support = recent.Average(c => c.Low);
        var resistance = recent.Average(c => c.High);
        var avgVol = recent.Average(c => c.Volume);

        var prev = _candles[^2];
        var curr = candle;

        var belowSupport = prev.Low < support * (1 - _breakPercent);
        var closeBackAbove = curr.Close > support;
        var volumeSpike = curr.Volume > avgVol * 1.5m;

        if (belowSupport && closeBackAbove && volumeSpike)
        {
            BuySignal = true;
            HuntLevel = support;
            HuntVolume = curr.Volume;
            return;
        }

        var aboveResistance = prev.High > resistance * (1 + _breakPercent);
        var closeBackBelow = curr.Close < resistance;
        var volSpike = curr.Volume > avgVol * 1.5m;

        if (aboveResistance && closeBackBelow && volSpike)
        {
            SellSignal = true;
            HuntLevel = resistance;
            HuntVolume = curr.Volume;
        }
    }

    public void Reset()
    {
        _candles.Clear();
        BuySignal = false;
        SellSignal = false;
        HuntLevel = 0;
        HuntVolume = 0;
    }
}
