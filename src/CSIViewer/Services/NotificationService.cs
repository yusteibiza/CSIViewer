using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using CSIViewer.Application;

namespace CSIViewer.Services;

public sealed class NotificationService : INotificationService
{
    private bool _registered;

    public void Register()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            _registered = false;
        }
    }

    public void Show(string title, string message)
    {
        try
        {
            if (!_registered)
            {
                Register();
            }

            if (!_registered)
            {
                return;
            }

            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Notifications are non-critical; the session UI remains authoritative.
        }
    }
}
