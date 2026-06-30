namespace ETHWyckoffBot.Models;

public class FusionResult
{
    public TradingAction Action { get; set; }
    public double Confidence { get; set; }
    public double WhaleScore { get; set; }
    public double OrderFlowScore { get; set; }
    public double VolumeScore { get; set; }
    public string Summary { get; set; } = "";
}
