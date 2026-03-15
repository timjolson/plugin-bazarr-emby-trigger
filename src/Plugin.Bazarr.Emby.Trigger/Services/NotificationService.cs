using System;
using System.Threading;
using System.Threading.Tasks;
using Emby.Notifications;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class NotificationService
{
    private readonly INotificationManager notificationManager;
    private readonly Func<string, User?> resolveUser;

    public NotificationService(INotificationManager notificationManager, IUserManager userManager)
        : this(notificationManager, userId => userManager.GetUserById(userId))
    {
    }

    public NotificationService(INotificationManager notificationManager, Func<string, User?> resolveUser)
    {
        this.notificationManager = notificationManager;
        this.resolveUser = resolveUser;
    }

    public Task NotifySubtitleArrivalAsync(PendingSearchRecord search, CancellationToken cancellationToken)
    {
        SendToRequestors(
            search,
            "Bazarr subtitles downloaded",
            $"New or updated subtitles were detected for {search.GetDisplayName()}.",
            LogSeverity.Info,
            cancellationToken);

        return Task.CompletedTask;
    }

    public Task NotifyTimeoutAsync(PendingSearchRecord search, CancellationToken cancellationToken)
    {
        SendToRequestors(
            search,
            "Bazarr subtitle search timed out",
            $"No new subtitle file was detected for {search.GetDisplayName()} before the search timeout elapsed.",
            LogSeverity.Warn,
            cancellationToken);

        return Task.CompletedTask;
    }

    public Task NotifyConnectionFailureAsync(PendingSearchRecord search, string error, CancellationToken cancellationToken)
    {
        SendToRequestors(
            search,
            "Bazarr connection failed",
            $"A Bazarr connection error prevented subtitle processing for {search.GetDisplayName()}: {error}",
            LogSeverity.Error,
            cancellationToken);

        return Task.CompletedTask;
    }

    public Task NotifyApiFailureAsync(PendingSearchRecord search, string error, CancellationToken cancellationToken)
    {
        SendToRequestors(
            search,
            "Bazarr request failed",
            $"A Bazarr API error prevented subtitle processing for {search.GetDisplayName()}: {error}",
            LogSeverity.Error,
            cancellationToken);

        return Task.CompletedTask;
    }

    public Task NotifyMatchFailureAsync(PendingSearchRecord search, CancellationToken cancellationToken)
    {
        SendToRequestors(
            search,
            "Bazarr match not found",
            $"Bazarr metadata did not contain a match for {search.GetDisplayName()}.",
            LogSeverity.Warn,
            cancellationToken);

        return Task.CompletedTask;
    }

    private void SendToRequestors(PendingSearchRecord search, string title, string description, LogSeverity severity, CancellationToken cancellationToken)
    {
        var requestorIds = search.GetNotificationUserIds();
        if (requestorIds.Count == 0)
        {
            notificationManager.SendNotification(CreateNotificationRequest(title, description, severity, cancellationToken));
            return;
        }

        foreach (var requestorId in requestorIds)
        {
            var user = resolveUser(requestorId);
            if (user != null)
            {
                notificationManager.SendNotification(CreateNotificationRequest(title, description, severity, cancellationToken, user));
            }
        }
    }

    private static NotificationRequest CreateNotificationRequest(string title, string description, LogSeverity severity, CancellationToken cancellationToken, User? user = null)
    {
        return new NotificationRequest
        {
            Title = title,
            Description = description,
            Plugin = Plugin.Instance,
            Severity = severity,
            CancellationToken = cancellationToken,
            User = user,
        };
    }
}
