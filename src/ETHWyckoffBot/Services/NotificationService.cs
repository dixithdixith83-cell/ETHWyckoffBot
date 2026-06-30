using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class NotificationService
{
    private readonly List<Notification> _notifications = new();

    public IReadOnlyList<Notification> Notifications => _notifications.AsReadOnly();
    public event Action<Notification>? NotificationRaised;

    public void Notify(NotificationType type, string title, string message)
    {
        var notif = new Notification
        {
            Timestamp = DateTime.Now,
            Type = type,
            Title = title,
            Message = message
        };

        _notifications.Insert(0, notif);
        if (_notifications.Count > 100)
            _notifications.RemoveAt(_notifications.Count - 1);

        NotificationRaised?.Invoke(notif);
    }
}
