namespace ETHWyckoffBot.Models;

public class Notification
{
    public DateTime Timestamp { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
}

public enum NotificationType
{
    Info,
    Entry,
    Exit,
    Whale,
    AMD_Change,
    Risk
}
