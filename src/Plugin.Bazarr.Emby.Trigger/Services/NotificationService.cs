using System.Threading;
using System.Threading.Tasks;
using Emby.Notifications;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class NotificationService
{
    private readonly INotificationManager notificationManager;

    public NotificationService(INotificationManager notificationManager)
    {
        this.notificationManager = notificationManager;
    }

    public Task NotifySubtitleArrivalAsync(PendingSearchRecord search, CancellationToken cancellationToken)
    {
        notificationManager.SendNotification(new NotificationRequest
        {
            Title = "Bazarr subtitles downloaded",
            Description = $"New or updated subtitles were detected for {search.GetDisplayName()}.",
            Plugin = Plugin.Instance,
            Severity = LogSeverity.Info,
            CancellationToken = cancellationToken,
        });
        return Task.CompletedTask;
    }

    public Task NotifyTimeoutAsync(PendingSearchRecord search, CancellationToken cancellationToken)
    {
        notificationManager.SendNotification(new NotificationRequest
        {
            Title = "Bazarr subtitle search timed out",
            Description = $"No new subtitle file was detected for {search.GetDisplayName()} before the search timeout elapsed.",
            Plugin = Plugin.Instance,
            Severity = LogSeverity.Warn,
            CancellationToken = cancellationToken,
        });
        return Task.CompletedTask;
    }
}
