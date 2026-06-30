namespace ETHWyckoffBot.Models;

public class WhaleAlert
{
    public DateTime Timestamp { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public WhaleAlertType Type { get; set; }
    public decimal ValueUsd { get; set; }
}

public enum WhaleAlertType
{
    WhaleBuy,
    WhaleSell,
    ExchangeInflow,
    ExchangeOutflow,
    Accumulation,
    Distribution
}
