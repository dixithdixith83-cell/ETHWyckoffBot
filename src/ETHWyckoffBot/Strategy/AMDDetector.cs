using ETHWyckoffBot.Indicators;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Strategy;

public class AMDDetector
{
    public AMDPhase CurrentPhase { get; private set; } = AMDPhase.Unknown;
    public double Confidence { get; private set; }
    public bool HasEnoughData => _range.HasData;

    public event Action<AMDPhase>? PhaseChanged;

    private readonly RangeDetector _range = new();
    private readonly CumulativeVolumeDelta _cvd;
    private readonly OrderBookImbalance _imbalance;
    private decimal _prevClose;
    private readonly int _requiredConfirmation = 3;
    private int _confirmationCount;
    private AMDPhase _pendingPhase = AMDPhase.Unknown;

    public AMDDetector(CumulativeVolumeDelta cvd, OrderBookImbalance imbalance)
    {
        _cvd = cvd;
        _imbalance = imbalance;
    }

    public void Evaluate(Candle candle)
    {
        if (_prevClose == 0) _prevClose = candle.Close;
        _range.Update(candle);
        var oldPhase = CurrentPhase;

        var inRange = _range.CurrentRange.HasValue;
        var rangeWidth = inRange ? _range.RangeWidthPercent : 100;
        var touchesSupport = _range.SupportTouches;
        var touchesResistance = _range.ResistanceTouches;
        var volumeHigh = candle.Volume > 1.5m * GetAverageVolume(candle.Volume);
        var cvdPositive = _cvd.IsPositive;
        var imbaBull = _imbalance.Ratio > 1.3;

        var suggested = CurrentPhase switch
        {
            AMDPhase.Unknown => DetectInitial(candle, inRange, rangeWidth, touchesSupport, touchesResistance, volumeHigh, cvdPositive, imbaBull),
            AMDPhase.Accumulation => CheckForMarkup(candle, inRange, volumeHigh, cvdPositive, imbaBull),
            AMDPhase.Markup => CheckForDistribution(candle, inRange, volumeHigh),
            AMDPhase.Distribution => CheckForMarkdown(candle, inRange, volumeHigh),
            AMDPhase.Markdown => CheckForAccumulation(candle, inRange, rangeWidth),
            _ => AMDPhase.Unknown
        };

        if (suggested == CurrentPhase)
        {
            _confirmationCount = 0;
            _pendingPhase = AMDPhase.Unknown;
        }
        else if (suggested == _pendingPhase)
        {
            _confirmationCount++;
            if (_confirmationCount >= _requiredConfirmation)
            {
                CurrentPhase = suggested;
                _confirmationCount = 0;
                _pendingPhase = AMDPhase.Unknown;
            }
        }
        else
        {
            _pendingPhase = suggested;
            _confirmationCount = 1;
        }

        Confidence = CalculateConfidence(candle);
        _prevClose = candle.Close;

        if (CurrentPhase != oldPhase)
            PhaseChanged?.Invoke(CurrentPhase);
    }

    private AMDPhase DetectInitial(Candle c, bool inRange, double width, int sup, int res, bool volHigh, bool cvdPos, bool imba)
    {
        if (inRange && width < 8 && sup >= 2 && res >= 2 && cvdPos && imba)
            return AMDPhase.Accumulation;
        if (c.Close > _prevClose * 1.02m && volHigh && cvdPos)
            return AMDPhase.Markup;
        if (c.Close < _prevClose * 0.98m && volHigh && !cvdPos)
            return AMDPhase.Markdown;
        return AMDPhase.Unknown;
    }

    private AMDPhase CheckForMarkup(Candle c, bool inRange, bool volHigh, bool cvdPos, bool imba)
    {
        if (inRange && volHigh && !cvdPos && !imba)
            return AMDPhase.Distribution;
        if (c.Close < _prevClose * 0.97m && volHigh)
            return AMDPhase.Markdown;
        return AMDPhase.Markup;
    }

    private AMDPhase CheckForDistribution(Candle c, bool inRange, bool volHigh, bool? _ = null)
    {
        if (c.Close < _prevClose * 0.97m && volHigh)
            return AMDPhase.Markdown;
        if (inRange && _range.RangeWidthPercent < 6 && c.Close > _prevClose * 1.02m)
            return AMDPhase.Markup;
        return AMDPhase.Distribution;
    }

    private AMDPhase CheckForMarkdown(Candle c, bool inRange, bool volHigh)
    {
        if (inRange && _range.SupportTouches >= 2 && volHigh)
            return AMDPhase.Accumulation;
        return AMDPhase.Markdown;
    }

    private AMDPhase CheckForAccumulation(Candle c, bool inRange, double width)
    {
        if (inRange && width < 6 && _range.SupportTouches >= 2)
            return AMDPhase.Accumulation;
        return AMDPhase.Markdown;
    }

    private double CalculateConfidence(Candle c)
    {
        var score = 0.5;
        if (_range.CurrentRange.HasValue) score += 0.15;
        if (_cvd.IsPositive == (CurrentPhase is AMDPhase.Accumulation or AMDPhase.Markup)) score += 0.1;
        if (_imbalance.Ratio > 1.2 == (CurrentPhase is AMDPhase.Accumulation or AMDPhase.Markup)) score += 0.1;
        return Math.Clamp(score, 0, 1);
    }

    private decimal _avgVolume;
    private int _volumeSamples;
    private decimal GetAverageVolume(decimal current)
    {
        _avgVolume = ((_avgVolume * _volumeSamples) + current) / (_volumeSamples + 1);
        _volumeSamples = Math.Min(_volumeSamples + 1, 100);
        return _avgVolume;
    }

    public string GetPhaseDescription()
    {
        return CurrentPhase switch
        {
            AMDPhase.Accumulation => "Smart money accumulating. Prepare for markup.",
            AMDPhase.Markup => "Trend up. Hold with trailing stop.",
            AMDPhase.Distribution => "Smart money distributing. Prepare for markdown.",
            AMDPhase.Markdown => "Trend down. Hold short with trailing stop.",
            _ => "Market phase unclear. Waiting for setup."
        };
    }

    public void Reset()
    {
        CurrentPhase = AMDPhase.Unknown;
        Confidence = 0;
        _range.Reset();
        _avgVolume = 0;
        _volumeSamples = 0;
    }
}
