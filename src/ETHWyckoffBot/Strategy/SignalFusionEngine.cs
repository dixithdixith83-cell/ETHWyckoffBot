using ETHWyckoffBot.Indicators;
using ETHWyckoffBot.Models;
using ETHWyckoffBot.Services;

namespace ETHWyckoffBot.Strategy;

public class SignalFusionEngine
{
    private readonly AMDDetector _amd;
    private WhaleTrackerServiceRef? _whale;
    private readonly CumulativeVolumeDelta _cvd;
    private readonly OrderBookImbalance _imbalance;
    private readonly TFLadderStrategy _ladder;
    private readonly EntryFilters _filters;
    private readonly StopHuntDetector _stopHunt;
    private readonly List<decimal> _volHistory = new();
    private readonly AppConfig _config;

    public SignalFusionEngine(
        AMDDetector amd,
        CumulativeVolumeDelta cvd,
        OrderBookImbalance imbalance,
        TFLadderStrategy ladder,
        EntryFilters filters,
        StopHuntDetector stopHunt,
        AppConfig config)
    {
        _amd = amd;
        _cvd = cvd;
        _imbalance = imbalance;
        _ladder = ladder;
        _ladderRef = ladder;
        _filters = filters;
        _stopHunt = stopHunt;
        _config = config;
    }

    private double EntryThreshold => (double)_config.Trading.EntryFusionThreshold;
    private double ExitThreshold => (double)_config.Trading.ExitFusionThreshold;

    private readonly TFLadderStrategy _ladderRef;
    private WeakReference<WhaleTrackerServiceRef>? _whaleRef;

    public void SetWhaleTracker(WhaleTrackerServiceRef whale)
    {
        _whale = whale;
        _whaleRef = new WeakReference<WhaleTrackerServiceRef>(whale);
    }

    public WhaleTrackerServiceRef? Whale => _whale;

    public FusionResult Fuse(Dictionary<string, Candle> latest)
    {
        if (latest.TryGetValue("1m", out var volC))
        {
            _volHistory.Add(volC.Volume);
            if (_volHistory.Count > 20) _volHistory.RemoveAt(0);
        }

        var whaleScore = _whale?.GetScore() ?? 0;
        var ofScore = ScoreOrderFlow();
        var volScore = ScoreVolume(latest);
        var vpScore = ScoreVolumeProfile(latest);
        var huntBonus = _stopHunt.BuySignal ? 0.3 : _stopHunt.SellSignal ? -0.3 : 0;

        // Institutional fusion weights: OrderFlow 40%, Volume 25%, VP 25%, Whale 10%
        var total = (whaleScore * 0.10) + (ofScore * 0.40) + (volScore * 0.25) + (vpScore * 0.25);
        total += huntBonus * 0.5;

        TradingAction action;
        if (total >= EntryThreshold && _ladderRef.PositionDirection == TradeDirection.None)
            action = TradingAction.EnterLong;
        else if (total <= -EntryThreshold && _ladderRef.PositionDirection == TradeDirection.None)
            action = TradingAction.EnterShort;
        else if (total < -ExitThreshold && _ladderRef.PositionDirection == TradeDirection.Long)
            action = TradingAction.ExitLong;
        else if (total > ExitThreshold && _ladderRef.PositionDirection == TradeDirection.Short)
            action = TradingAction.ExitShort;
        else
            action = TradingAction.Hold;

        var hunt = _stopHunt.BuySignal ? " BUY-HUNT" : _stopHunt.SellSignal ? " SELL-HUNT" : "";
        var summary = $"OF={ofScore:F2} Vol={volScore:F2} VP={vpScore:F2} W={whaleScore:F2}{hunt} → {(total > 0 ? "bull" : "bear")} ({total:F2})";

        return new FusionResult
        {
            Action = action,
            Confidence = Math.Abs(total),
            WhaleScore = whaleScore,
            OrderFlowScore = ofScore,
            VolumeScore = volScore,
            Summary = summary
        };
    }

    private double ScoreAMD()
    {
        return _amd.CurrentPhase switch
        {
            AMDPhase.Markup => 0.9,
            AMDPhase.Accumulation => 0.6,
            AMDPhase.Markdown => -0.9,
            AMDPhase.Distribution => -0.6,
            _ => 0
        };
    }

    private double ScoreOrderFlow()
    {
        var cvdScore = _cvd.GetScore();
        var imbaScore = _imbalance.GetScore();
        return Math.Clamp((cvdScore + imbaScore) / 2, -1, 1);
    }

    private double ScoreVolume(Dictionary<string, Candle> latest)
    {
        if (!latest.TryGetValue("1m", out var c)) return 0;
        if (_volHistory.Count < 5) return 0;
        var avgVol = _volHistory.TakeLast(10).Average();
        if (avgVol <= 0) return 0;
        var ratio = (double)(c.Volume / avgVol);
        return Math.Clamp((ratio - 1) * 0.3, -0.5, 0.8);
    }

    private double ScoreVolumeProfile(Dictionary<string, Candle> latest)
    {
        if (!latest.TryGetValue("1m", out var c)) return 0;
        var vp = _filters.VolumeProfile ?? _ladderRef.VolumeProfile;
        if (vp == null || !vp.IsReady) return 0;

        return vp.GetScore(c.Close, _ladderRef.PositionDirection == TradeDirection.None
            ? TradeDirection.Long
            : _ladderRef.PositionDirection);
    }
}

public class WhaleTrackerServiceRef
{
    private readonly WhaleTrackerService _inner;
    public WhaleTrackerServiceRef(WhaleTrackerService inner) => _inner = inner;
    public double GetScore() => _inner.GetScore();
}
