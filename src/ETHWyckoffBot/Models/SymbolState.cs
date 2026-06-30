using ETHWyckoffBot.Indicators;
using ETHWyckoffBot.Services;
using ETHWyckoffBot.Strategy;

namespace ETHWyckoffBot.Models;

public class SymbolState
{
    public string Symbol { get; init; } = "";
    public CandleCache CandleCache { get; set; } = new(200);
    public TFLadderStrategy? Strategy { get; set; }
    public StopHuntDetector StopHunt { get; set; } = new();
    public AMDDetector? AMD { get; set; }
    public CumulativeVolumeDelta CVD { get; set; } = new();
    public OrderBookImbalance OrderBook { get; set; } = new();
    public TrailingStop TrailingStop { get; set; } = new();
    public VolumeProfile VolumeProfile { get; set; } = new(12);
    public VWAP Vwap { get; set; } = new();
    public SignalFusionEngine? Fusion { get; set; }
    public Position? Position { get; set; }
    public long? StopOrderId { get; set; }
    public long? TpOrderId { get; set; }
    public decimal BalanceBeforeEntry { get; set; }
}
