using ETHWyckoffBot.Indicators;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Strategy;

public class EntryFilters
{
    private readonly TradingConfig _config;
    private readonly List<decimal> _volumes = new();
    private decimal _avgVolume;

    public VolumeProfile? VolumeProfile { get; set; }
    public CumulativeVolumeDelta? CVD { get; set; }
    public VWAP? Vwap { get; set; }
    public EMARibbon EmaRibbon { get; } = new();

    public EntryFilters(TradingConfig config, LadderConfig ladderConfig)
    {
        _config = config;
    }

    public void Update(string timeframe, Candle candle)
    {
        if (timeframe != "1m") return;
        _volumes.Add(candle.Volume);
        if (_volumes.Count > 20) _volumes.RemoveAt(0);
        _avgVolume = _volumes.Count > 0 ? _volumes.Average() : 0;
        EmaRibbon.Update(candle.Close);
    }

    public bool HasBullishOrderFlow(Dictionary<string, Candle> latest)
    {
        if (!latest.TryGetValue("1m", out var c1)) return false;
        
        // Order flow: CVD bullish and Price above VWAP (up days)
        double cvdScore = CVD?.GetScore() ?? 0;
        bool cvdBullish = cvdScore > 0.0;
        bool aboveVwap = c1.Close > (Vwap?.Value ?? 0);
        
        return cvdBullish && aboveVwap;
    }

    public bool HasBearishOrderFlow(Dictionary<string, Candle> latest)
    {
        if (!latest.TryGetValue("1m", out var c1)) return false;
        
        // Order flow: CVD bearish and Price below VWAP (down days)
        double cvdScore = CVD?.GetScore() ?? 0;
        bool cvdBearish = cvdScore < 0.0;
        bool belowVwap = c1.Close < (Vwap?.Value ?? 0);
        
        return cvdBearish && belowVwap;
    }

    public bool CanEnterLong(Dictionary<string, Candle> latest)
    {
        if (!latest.TryGetValue("1m", out var c1)) return false;

        if (_avgVolume > 0 && c1.Volume < _avgVolume * 0.8m) return false;

        if (VolumeProfile != null && VolumeProfile.IsReady)
        {
            if (c1.Close > VolumeProfile.ValueAreaHigh * 1.03m) return false;
        }

        return true;
    }

    public bool CanEnterShort(Dictionary<string, Candle> latest)
    {
        if (!latest.TryGetValue("1m", out var c1)) return false;

        if (_avgVolume > 0 && c1.Volume < _avgVolume * 0.8m) return false;

        if (VolumeProfile != null && VolumeProfile.IsReady)
        {
            if (c1.Close < VolumeProfile.ValueAreaLow * 0.97m) return false;
        }

        return true;
    }

    public decimal VWAPValue => Vwap?.Value ?? 0;

    public (bool b9, bool b21, bool b50, bool b200) GetEMAStatus(string tf = "1m")
    {
        var price = EmaRibbon.Ema9.Value;
        if (price == 0) return (false, false, false, false);
        return (
            EmaRibbon.Ema9.Value > price,
            EmaRibbon.Ema21.Value > price,
            EmaRibbon.Ema50.Value > price,
            EmaRibbon.Ema200.Value > price
        );
    }

    public double GetEMAScore()
    {
        if (EmaRibbon.Ema9.Value == 0) return 0;
        if (EmaRibbon.IsBullishStacked) return 0.6;
        if (EmaRibbon.IsBearishStacked) return -0.6;
        return 0;
    }

    public void Reset()
    {
        _volumes.Clear();
        _avgVolume = 0;
        EmaRibbon.Reset();
    }
}
