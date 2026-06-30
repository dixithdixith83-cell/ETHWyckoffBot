using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class MetricsCollector
{
    private readonly List<TradeRecord> _trades = new();

    public void RecordTrade(TradeRecord trade)
    {
        _trades.Add(trade);
    }

    public int TotalTrades => _trades.Count;
    public int WinningTrades => _trades.Count(t => t.PnL > 0);
    public int LosingTrades => _trades.Count(t => t.PnL < 0);
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
    public decimal TotalPnL => _trades.Sum(t => t.PnL ?? 0);

    public double SharpeRatio
    {
        get
        {
            if (_trades.Count < 2) return 0;
            var returns = _trades.Select(t => (double)(t.PnLPercent ?? 0)).ToList();
            var avg = returns.Average();
            var std = Math.Sqrt(returns.Sum(r => Math.Pow(r - avg, 2)) / (returns.Count - 1));
            return std > 0 ? avg / std * Math.Sqrt(returns.Count) : 0;
        }
    }

    public decimal MaxDrawdown { get; private set; }

    public void CalculateMaxDrawdown(decimal balanceHistory)
    {
        // simplified: called externally with current balance
    }

    public string GetSummary()
    {
        return $"Trades: {TotalTrades} | Win: {WinRate:F1}% | PnL: ${TotalPnL:F2} | Sharpe: {SharpeRatio:F2}";
    }
}
