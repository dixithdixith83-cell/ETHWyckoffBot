using ETHWyckoffBot.Indicators;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Strategy;

public class TFLadderStrategy
{
    private AMDDetector? _amd;
    private StopHuntDetector? _stopHunt;
    private TradeDirection _positionDirection = TradeDirection.None;
    private int _ticksSinceEntry;
    private int _ticksWaiting;
    private decimal _entryPrice;
    private decimal _highestSinceEntry;
    private decimal _lowestSinceEntry;
    private double _entryFusionConfidence;

    private readonly AppConfig _config;
    private double EntryConfidenceMin => (double)_config.Trading.EntryFusionThreshold;
    private double ExitFusionThreshold => (double)_config.Trading.ExitFusionThreshold;
    private const int MinTicksBetweenEntries = 6;
    private const int MinTicksHold = 6;
    private const int MaxTicksHold = 60;

    public int CurrentTier { get; private set; }
    public TradeDirection PositionDirection => _positionDirection;
    public Dictionary<string, Supertrend> Supertrends { get; } = new();
    public int CandlesSinceEntry => _ticksSinceEntry;
    public EntryFilters? Filters { get; set; }
    public VolumeProfile? VolumeProfile { get; set; }
    public VWAP? Vwap { get; set; }
    public StopHuntDetector? StopHunt => _stopHunt;

    public event Action<TradingAction>? SignalGenerated;

    public TFLadderStrategy(LadderConfig config, TradingConfig tradingConfig)
    {
        _config = new AppConfig { Trading = tradingConfig, Ladder = config };
    }

    public void SetAMD(AMDDetector amd) { _amd = amd; }
    public void SetStopHunt(StopHuntDetector sh) { _stopHunt = sh; }

    public void UpdateIndicators(Dictionary<string, Candle> latest)
    {
        foreach (var (tf, candle) in latest)
        {
            if (!Supertrends.ContainsKey(tf))
                Supertrends[tf] = new Supertrend(10, 3.0);
            Supertrends[tf].Update(candle.High, candle.Low, candle.Close);
        }
    }

    public void WarmUp(Dictionary<string, List<Candle>> history)
    {
        foreach (var (tf, candles) in history)
        {
            if (!Supertrends.ContainsKey(tf))
                Supertrends[tf] = new Supertrend(10, 3.0);
            foreach (var c in candles)
                Supertrends[tf].Update(c.High, c.Low, c.Close);
        }
    }

    public TradingAction Evaluate(Dictionary<string, Candle> latestCandles, FusionResult? fusion = null)
    {
        foreach (var (tf, candle) in latestCandles)
        {
            Filters?.Update(tf, candle);
            VolumeProfile?.AddCandle(candle);
            Vwap?.Update(candle.High, candle.Low, candle.Close, candle.Volume);
        }

        UpdateIndicators(latestCandles);

        var c1 = latestCandles.GetValueOrDefault("1m");
        if (c1 != null)
        {
            _stopHunt?.Update(c1);
            _amd?.Evaluate(c1);
        }

        if (_positionDirection != TradeDirection.None)
        {
            _ticksSinceEntry++;
            if (c1 != null)
            {
                if (c1.High > _highestSinceEntry) _highestSinceEntry = c1.High;
                if (_lowestSinceEntry == 0 || c1.Low < _lowestSinceEntry) _lowestSinceEntry = c1.Low;
            }
            return EvaluateExit(latestCandles, fusion);
        }

        _ticksWaiting++;
        return EvaluateEntry(latestCandles, fusion);
    }

    private void SetEntryTracking(TradeDirection dir, decimal price)
    {
        _positionDirection = dir;
        CurrentTier = 1;
        _ticksSinceEntry = 0;
        _entryPrice = price;
        _highestSinceEntry = price;
        _lowestSinceEntry = price;
    }

    private bool IsPriceNearVwap(decimal price, TradeDirection direction)
    {
        var vwap = Vwap?.Value ?? 0;
        if (vwap == 0) return true;
        var pctFromVwap = Math.Abs((price - vwap) / vwap);
        // Mean reversion: enter when price is near VWAP or slightly away in our direction
        if (direction == TradeDirection.Long)
            return price <= vwap * 1.002m; // at or below 0.2% above VWAP
        else
            return price >= vwap * 0.998m; // at or above 0.2% below VWAP
    }

    private TradingAction EvaluateEntry(Dictionary<string, Candle> latestCandles, FusionResult? fusion)
    {
        if (fusion == null || fusion.Confidence < EntryConfidenceMin) return TradingAction.Hold;
        if (_ticksWaiting < MinTicksBetweenEntries) return TradingAction.Hold;

        var price = latestCandles.GetValueOrDefault("1m")?.Close ?? 0;
        if (price == 0) return TradingAction.Hold;

        // INSTITUTIONAL ENTRY: Sequential hard conditions (must ALL pass)
        // 1. Stop Hunt = Liquidity Grab (smart money took out stops)
        if (_stopHunt == null) return TradingAction.Hold;
        
        bool wantLong = fusion.Action == TradingAction.EnterLong;
        bool wantShort = fusion.Action == TradingAction.EnterShort;
        
        // 2. StopHunt must fire in the direction of fusion
        if (wantLong && !_stopHunt.BuySignal) return TradingAction.Hold;
        if (wantShort && !_stopHunt.SellSignal) return TradingAction.Hold;

        // 3. Order Flow must confirm direction (real buying/selling pressure)
        if (wantLong && fusion.OrderFlowScore <= 0.0) return TradingAction.Hold;
        if (wantShort && fusion.OrderFlowScore >= 0.0) return TradingAction.Hold;

        // 4. Volume must confirm institutional participation
        var c1 = latestCandles.GetValueOrDefault("1m");
        if (c1 == null) return TradingAction.Hold;
        if (Filters == null) return TradingAction.Hold;
        double cvdScore = CVDScore(fusion);
        if (Math.Abs(cvdScore) < 0.05) return TradingAction.Hold; // weak order flow

        // ALL institutional conditions met — enter
        _entryFusionConfidence = fusion.Confidence;
        SetEntryTracking(wantLong ? TradeDirection.Long : TradeDirection.Short, price);
        SignalGenerated?.Invoke(fusion.Action);
        return fusion.Action;
    }

    private static double CVDScore(FusionResult fusion)
    {
        return fusion.OrderFlowScore;
    }

    public string? LastExitReason { get; private set; }

    private TradingAction EvaluateExit(Dictionary<string, Candle> latestCandles, FusionResult? fusion)
    {
        var c1 = latestCandles.GetValueOrDefault("1m");
        if (c1 == null) return TradingAction.Hold;

        if (_ticksSinceEntry < MinTicksHold) return TradingAction.Hold;

        // INSTITUTIONAL EXIT CONDITIONS:
        // 1. Stop Hunt reversed → smart money changed direction
        if (_stopHunt != null)
        {
            if (_positionDirection == TradeDirection.Long && _stopHunt.SellSignal)
                return ExitPosition("Stop hunt sell signal (smart money reversed)");
            if (_positionDirection == TradeDirection.Short && _stopHunt.BuySignal)
                return ExitPosition("Stop hunt buy signal (smart money reversed)");
        }

        return TradingAction.Hold;
    }

    private TradingAction ExitPosition(string reason)
    {
        LastExitReason = reason;
        var action = _positionDirection == TradeDirection.Long ? TradingAction.ExitLong : TradingAction.ExitShort;
        _positionDirection = TradeDirection.None;
        CurrentTier = 0;
        _ticksSinceEntry = 0;
        _highestSinceEntry = 0;
        _lowestSinceEntry = 0;
        _entryPrice = 0;
        _entryFusionConfidence = 0;
        SignalGenerated?.Invoke(action);
        return action;
    }

    public double GetAngle(string timeframe = "1m")
    {
        var st = Supertrends.GetValueOrDefault(timeframe);
        return st?.AngleDegrees ?? 0;
    }

    public bool ShouldHoldAggressively() => false;

    public void Reset()
    {
        _positionDirection = TradeDirection.None;
        CurrentTier = 0;
        _ticksSinceEntry = 0;
        _highestSinceEntry = 0;
        _lowestSinceEntry = 0;
        _entryPrice = 0;
        _entryFusionConfidence = 0;
        _stopHunt?.Reset();
        Filters?.Reset();
        VolumeProfile?.Reset();
    }
}