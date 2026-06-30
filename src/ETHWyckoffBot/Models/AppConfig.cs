namespace ETHWyckoffBot.Models;

public class AppConfig
{
    public DeltaConfig Delta { get; set; } = new();
    public TradingConfig Trading { get; set; } = new();
    public LadderConfig Ladder { get; set; } = new();
    public OnChainConfig OnChain { get; set; } = new();
}

public class OnChainConfig
{
    public string EthRpcUrl { get; set; } = "https://cloudflare-eth.com";
    public bool Enabled { get; set; } = false;
    public int MinWhaleValueUsd { get; set; } = 100000;
    public string BtcApiUrl { get; set; } = "https://blockchain.info";
}

public class DeltaConfig
{
    public string BaseUrl { get; set; } = "https://cdn-ind.testnet.deltaex.org";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Symbol { get; set; } = "ETH/USDT";
    public List<string> Symbols { get; set; } = new() { "ETHUSD", "BTCUSD", "SOLUSD", "XRPUSD", "DOGEUSD", "ADAUSD" };
    public int MaxConcurrentPositions { get; set; } = 2;
    public bool UseDemo { get; set; } = true;
    public int Leverage { get; set; } = 20;
}

public class TradingConfig
{
    public decimal MaxPositionSizePercent { get; set; } = 0.20m;
    public decimal MaxDailyLossPercent { get; set; } = 0.50m;
    public decimal MaxDrawdownPercent { get; set; } = 0.50m;
    public int MinAngleToHold { get; set; } = 30;
    public int AggressiveAngle { get; set; } = 60;
    public int CandleCheckIntervalSeconds { get; set; } = 60;
    public int MaxConsecutiveLosses { get; set; } = 4;
    public decimal StopLossPercent { get; set; } = 0.25m;
    public decimal TakeProfitPercent { get; set; } = 0.45m;
    public decimal RiskPerTradePercent { get; set; } = 5.0m;
    public decimal MaxRiskPerTradeUSD { get; set; } = 10.0m;
    public decimal EntryFusionThreshold { get; set; } = 0.30m;
    public decimal ExitFusionThreshold { get; set; } = 0.15m;
}

public class LadderConfig
{
    public List<string> Timeframes { get; set; } = new() { "1m", "5m", "10m", "15m", "1h", "4h" };
    public int PromotionCandles { get; set; } = 5;
    public int AtrPeriod { get; set; } = 10;
    public double AtrMultiplier { get; set; } = 3.0;
}
