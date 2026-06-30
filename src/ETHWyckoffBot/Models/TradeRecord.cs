namespace ETHWyckoffBot.Models;

public class TradeRecord
{
    public int Id { get; set; }
    public DateTime EnteredAt { get; set; }
    public DateTime? ExitedAt { get; set; }
    public TradeDirection Direction { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal? PnL { get; set; }
    public decimal? PnLPercent { get; set; }
    public string? ExitReason { get; set; }
    public double FusionScore { get; set; }
}
