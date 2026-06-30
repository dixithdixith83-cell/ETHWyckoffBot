namespace ETHWyckoffBot.Models;

public class Position
{
    public TradeDirection Direction { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal CurrentStop { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal PnL => (CurrentPrice - EntryPrice) * Quantity * (Direction == TradeDirection.Long ? 1 : -1);
    public decimal PnLPercent => EntryPrice > 0 ? (PnL / (EntryPrice * Quantity)) * 100 : 0;
    public int LadderTier { get; set; }
    public DateTime EntryTime { get; set; }
    public List<string> ConfirmedTimeframes { get; set; } = new();
}
