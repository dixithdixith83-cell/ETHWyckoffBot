using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class CandleCache
{
    private readonly Dictionary<string, List<Candle>> _cache = new();
    private readonly int _maxCandles;

    public CandleCache(int maxCandles = 200)
    {
        _maxCandles = maxCandles;
    }

    public void AddCandle(string timeframe, Candle candle)
    {
        if (!_cache.ContainsKey(timeframe))
            _cache[timeframe] = new List<Candle>();

        var list = _cache[timeframe];

        if (list.Count > 0 && list[^1].Timestamp == candle.Timestamp)
            list[^1] = candle;
        else
            list.Add(candle);

        if (list.Count > _maxCandles)
            list.RemoveRange(0, list.Count - _maxCandles);
    }

    public void AddCandles(string timeframe, List<Candle> candles)
    {
        foreach (var c in candles)
            AddCandle(timeframe, c);
    }

    public List<Candle> GetCandles(string timeframe)
    {
        return _cache.GetValueOrDefault(timeframe) ?? new List<Candle>();
    }

    public Candle? GetLatest(string timeframe)
    {
        var list = GetCandles(timeframe);
        return list.Count > 0 ? list[^1] : null;
    }

    public Dictionary<string, Candle> GetLatestAll()
    {
        var result = new Dictionary<string, Candle>();
        foreach (var (tf, _) in _cache)
        {
            var latest = GetLatest(tf);
            if (latest != null)
                result[tf] = latest;
        }
        return result;
    }

    public void Clear()
    {
        _cache.Clear();
    }
}
