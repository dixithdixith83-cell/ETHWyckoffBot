using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Strategy;

public class RangeDetector
{
    private readonly List<Candle> _buffer = new();
    private const int Lookback = 30;

    public (decimal Support, decimal Resistance)? CurrentRange { get; private set; }
    public int SupportTouches { get; private set; }
    public int ResistanceTouches { get; private set; }
    public double RangeWidthPercent { get; private set; }
    public bool HasData => _buffer.Count >= Lookback;

    public void Update(Candle candle)
    {
        _buffer.Add(candle);
        if (_buffer.Count > Lookback * 2)
            _buffer.RemoveRange(0, _buffer.Count - Lookback * 2);

        if (_buffer.Count < Lookback) return;

        var recent = _buffer.TakeLast(Lookback).ToList();
        var lows = recent.Select(c => c.Low).OrderBy(x => x).ToList();
        var highs = recent.Select(c => c.High).OrderByDescending(x => x).ToList();

        var support = lows.Take(3).Average();
        var resistance = highs.Take(3).Average();
        var width = (double)((resistance - support) / support * 100);

        if (width > 15)
        {
            CurrentRange = null;
            SupportTouches = 0;
            ResistanceTouches = 0;
            return;
        }

        CurrentRange = (support, resistance);
        RangeWidthPercent = width;

        SupportTouches = recent.Count(c => Math.Abs(c.Low - support) / support < 0.005m);
        ResistanceTouches = recent.Count(c => Math.Abs(c.High - resistance) / resistance < 0.005m);
    }

    public void Reset()
    {
        _buffer.Clear();
        CurrentRange = null;
        SupportTouches = 0;
        ResistanceTouches = 0;
    }
}
