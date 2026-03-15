using System.Net.Http;
using Emby.Notifications;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class SearchCoordinatorTests
{
    [Fact]
    public async Task QueueAsync_MergesDuplicateQueuedRequestsAndTracksAllRequestors()
    {
        using var scenario = new SearchCoordinatorScenario();

        await scenario.Coordinator.QueueAsync(scenario.CreatePendingRecord("user-1"), CancellationToken.None);
        await scenario.Coordinator.QueueAsync(scenario.CreatePendingRecord("user-2"), CancellationToken.None);

        var saved = scenario.Repository.Load();
        var record = Assert.Single(saved);
        Assert.Equal(PendingSearchState.Queued, record.State);
        Assert.Equal(new[] { "user-1", "user-2" }, record.GetNotificationUserIds());
    }

    [Fact]
    public async Task QueueAsync_MergesDuplicateTriggeredRequestsAndTracksAdditionalRequestors()
    {
        using var scenario = new SearchCoordinatorScenario();
        var existing = new PendingSearchRecord
        {
            ContentType = VideoContentType.Movie,
            MediaPath = scenario.MediaPath,
            Title = "Example Movie",
            ProductionYear = 2024,
            RequestedLanguage = "eng",
            ForcedOnly = false,
            State = PendingSearchState.Triggered,
        };
        existing.AddNotificationUserId("user-1");
        scenario.Repository.Save(new[] { existing });

        using var coordinator = scenario.CreateCoordinator();
        await coordinator.QueueAsync(scenario.CreatePendingRecord("user-2"), CancellationToken.None);

        var saved = scenario.Repository.Load();
        var record = Assert.Single(saved);
        Assert.Equal(PendingSearchState.Triggered, record.State);
        Assert.Equal(new[] { "user-1", "user-2" }, record.GetNotificationUserIds());
    }

    private sealed class SearchCoordinatorScenario : IDisposable
    {
        private readonly DirectoryInfo directory;
        private readonly PluginOptions options = new();
        private readonly BazarrClient bazarrClient = new(new HttpClient());
        private readonly BazarrCatalogCache catalogCache;
        private readonly MediaMatcher matcher = new();
        private readonly SlidingWindowRateLimiter rateLimiter = new();
        private readonly SubtitleSnapshotService snapshotService = new();
        private readonly NotificationService notificationService;
        private readonly TestLogger logger = new();

        public SearchCoordinatorScenario()
        {
            directory = Directory.CreateTempSubdirectory();
            MediaPath = Path.Combine(directory.FullName, "Example Movie (2024).mkv");
            File.WriteAllText(MediaPath, "video");
            Repository = new PendingSearchRepository(directory.FullName);
            catalogCache = new BazarrCatalogCache(bazarrClient);
            notificationService = new NotificationService(new TestNotificationManager(), _ => null);
            Coordinator = CreateCoordinator();
        }

        public string MediaPath { get; }

        public PendingSearchRepository Repository { get; }

        public SearchCoordinator Coordinator { get; }

        public SearchCoordinator CreateCoordinator()
            => new(
                () => options,
                bazarrClient,
                catalogCache,
                matcher,
                rateLimiter,
                snapshotService,
                Repository,
                notificationService,
                logger);

        public PendingSearchRecord CreatePendingRecord(string userId)
        {
            var pending = new PendingSearchRecord
            {
                ContentType = VideoContentType.Movie,
                MediaPath = MediaPath,
                Title = "Example Movie",
                ProductionYear = 2024,
                RequestedLanguage = "eng",
                ForcedOnly = false,
            };
            pending.AddNotificationUserId(userId);
            return pending;
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            directory.Delete(true);
        }
    }

    private sealed class TestNotificationManager : INotificationManager
    {
        public NotificationTokenCategory[] GetNotificationTokens(string category)
            => Array.Empty<NotificationTokenCategory>();

        public NotificationCategoryInfo[] GetNotificationCategories(string category, User user)
            => Array.Empty<NotificationCategoryInfo>();

        public void SaveUserNotification(NotificationInfo notification, User user, string key)
        {
        }

        public void SendNotification(NotificationRequest request)
        {
        }

        public Task SendNotification(MediaBrowser.Model.Notifications.NotificationRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message, params object[] paramList) { }
        public void Debug(ReadOnlyMemory<char> message) { }
        public void Error(string message, params object[] paramList) { }
        public void Error(ReadOnlyMemory<char> message) { }
        public void ErrorException(string message, Exception exception, params object[] paramList) { }
        public void Fatal(string message, params object[] paramList) { }
        public void FatalException(string message, Exception exception, params object[] paramList) { }
        public void Info(string message, params object[] paramList) { }
        public void Info(ReadOnlyMemory<char> message) { }
        public void Log(LogSeverity severity, string message, params object[] paramList) { }
        public void Log(LogSeverity severity, ReadOnlyMemory<char> message) { }
        public void LogMultiline(string message, LogSeverity severity, System.Text.StringBuilder additionalContent) { }
        public void Warn(string message, params object[] paramList) { }
        public void Warn(ReadOnlyMemory<char> message) { }
    }
}
