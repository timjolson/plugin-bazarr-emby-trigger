using Emby.Notifications;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Notifications;
using Plugin.Bazarr.Emby.Trigger.Models;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class NotificationServiceTests
{
    [Fact]
    public async Task NotifySubtitleArrivalAsync_SendsOneNotificationPerTrackedRequestor()
    {
        var sent = new List<NotificationRequest>();
        var user1 = new User();
        var user2 = new User();
        var notificationManager = new RecordingNotificationManager(sent);
        var service = new NotificationService(notificationManager, userId => userId switch
        {
            "user-1" => user1,
            "user-2" => user2,
            _ => null,
        });

        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Movie,
            Title = "Example Movie",
            ProductionYear = 2024,
        };
        search.AddNotificationUserId("user-1");
        search.AddNotificationUserId("user-2");

        await service.NotifySubtitleArrivalAsync(search, CancellationToken.None);

        Assert.Equal(2, sent.Count);
        Assert.Same(user1, sent[0].User);
        Assert.Same(user2, sent[1].User);
        Assert.All(sent, item => Assert.Equal("Bazarr subtitles downloaded", item.Title));
    }

    [Fact]
    public async Task NotifyConnectionFailureAsync_UsesExpectedTitleAndErrorMessage()
    {
        var sent = new List<NotificationRequest>();
        var user = new User();
        var notificationManager = new RecordingNotificationManager(sent);
        var service = new NotificationService(notificationManager, userId => userId == "user-1" ? user : null);
        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Movie,
            Title = "Example Movie",
            ProductionYear = 2024,
        };
        search.AddNotificationUserId("user-1");

        await service.NotifyConnectionFailureAsync(search, "Unable to connect to Bazarr.", CancellationToken.None);

        var notification = Assert.Single(sent);
        Assert.Equal("Bazarr connection failed", notification.Title);
        Assert.Contains("Unable to connect to Bazarr.", notification.Description);
        Assert.Same(user, notification.User);
    }

    [Fact]
    public async Task NotifySubtitleArrivalAsync_WithoutTrackedRequestor_DoesNotBroadcast()
    {
        var sent = new List<NotificationRequest>();
        var notificationManager = new RecordingNotificationManager(sent);
        var service = new NotificationService(notificationManager, _ => null);
        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Movie,
            Title = "Example Movie",
            ProductionYear = 2024,
        };

        await service.NotifySubtitleArrivalAsync(search, CancellationToken.None);

        Assert.Empty(sent);
    }

    private sealed class RecordingNotificationManager : INotificationManager
    {
        private readonly List<NotificationRequest> sent;

        public RecordingNotificationManager(List<NotificationRequest> sent)
        {
            this.sent = sent;
        }

        public NotificationCategoryInfo[] GetNotificationCategories(string category, User user)
            => Array.Empty<NotificationCategoryInfo>();

        public MediaBrowser.Controller.Notifications.NotificationTokenCategory[] GetNotificationTokens(string category)
            => Array.Empty<MediaBrowser.Controller.Notifications.NotificationTokenCategory>();

        public void SaveUserNotification(NotificationInfo notification, User user, string key)
        {
        }

        public void SendNotification(NotificationRequest request)
        {
            sent.Add(request);
        }

        public Task SendNotification(MediaBrowser.Model.Notifications.NotificationRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
