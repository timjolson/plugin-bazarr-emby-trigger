using System.Net.Http;
using System.Net;
using System.Text.Json;
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

    [Fact]
    public async Task RunTriggeredMonitoringPassAsync_WhenPollingEnabled_CompletesSearchAfterSubtitleAppears()
    {
        using var scenario = new SearchCoordinatorScenario();
        scenario.Options.PollMediaFolders = true;
        scenario.Repository.Save(new[] { scenario.CreateTriggeredRecord() });
        using var coordinator = scenario.CreateCoordinator();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(scenario.MediaPath)!, "Example Movie (2024).eng.srt"), "subtitle");

        await coordinator.RunTriggeredMonitoringPassAsync(CancellationToken.None);

        Assert.Empty(scenario.Repository.Load());
    }

    [Fact]
    public async Task RunTriggeredMonitoringPassAsync_WhenPollingDisabled_WaitsForMatchingLibraryEvent()
    {
        using var scenario = new SearchCoordinatorScenario();
        scenario.Options.PollMediaFolders = false;
        scenario.Repository.Save(new[] { scenario.CreateTriggeredRecord() });
        using var coordinator = scenario.CreateCoordinator();
        var subtitlePath = Path.Combine(Path.GetDirectoryName(scenario.MediaPath)!, "Example Movie (2024).eng.srt");
        File.WriteAllText(subtitlePath, "subtitle");

        await coordinator.RunTriggeredMonitoringPassAsync(CancellationToken.None);
        Assert.Single(scenario.Repository.Load());
        Assert.Empty(scenario.NotificationManager.Sent);

        await coordinator.HandleLibraryItemChangeAsync(subtitlePath, Path.GetDirectoryName(scenario.MediaPath), CancellationToken.None);

        Assert.Empty(scenario.Repository.Load());
    }

    [Fact]
    public async Task RunQueueProcessingPassAsync_QueuedRequestFirstSend_SetsOriginalAndLastSentTimestamps()
    {
        using var scenario = new SearchCoordinatorScenario();
        var record = scenario.CreatePendingRecord("user-1", "Queued Movie", 301);
        scenario.Repository.Save(new[] { record });
        using var coordinator = scenario.CreateCoordinator();

        await coordinator.RunQueueProcessingPassAsync(CancellationToken.None);

        var saved = Assert.Single(scenario.Repository.Load());
        Assert.Equal(PendingSearchState.Triggered, saved.State);
        Assert.Equal(saved.TriggeredUtc, saved.LastSentUtc);
        Assert.NotNull(saved.TriggeredUtc);
        Assert.Equal(new[] { 301 }, scenario.Handler.TriggeredMovieIds);
    }

    [Fact]
    public async Task RunQueueProcessingPassAsync_TriggeredRequestTooRecent_SkipsResendAndKeepsTimestamp()
    {
        using var scenario = new SearchCoordinatorScenario();
        var lastSentUtc = DateTime.UtcNow;
        var record = scenario.CreateTriggeredRecord("user-1", "Recent Movie", 302, DateTime.UtcNow.AddMinutes(-10), lastSentUtc);
        scenario.Repository.Save(new[] { record });
        using var coordinator = scenario.CreateCoordinator();

        await coordinator.RunQueueProcessingPassAsync(CancellationToken.None);

        var saved = Assert.Single(scenario.Repository.Load());
        Assert.NotNull(saved.LastSentUtc);
        Assert.True((saved.LastSentUtc.Value - lastSentUtc).Duration() < TimeSpan.FromSeconds(1));
        Assert.Empty(scenario.Handler.TriggeredMovieIds);
    }

    [Fact]
    public async Task RunQueueProcessingPassAsync_TriggeredRequestReadyToResend_UpdatesLastSentWithoutResettingOriginalTimestamp()
    {
        using var scenario = new SearchCoordinatorScenario();
        var originalSentUtc = DateTime.UtcNow.AddMinutes(-15);
        var lastSentUtc = DateTime.UtcNow.AddMinutes(-10);
        var record = scenario.CreateTriggeredRecord("user-1", "Retry Movie", 303, originalSentUtc, lastSentUtc);
        scenario.Repository.Save(new[] { record });
        using var coordinator = scenario.CreateCoordinator();

        await coordinator.RunQueueProcessingPassAsync(CancellationToken.None);

        var saved = Assert.Single(scenario.Repository.Load());
        Assert.NotNull(saved.TriggeredUtc);
        Assert.True((saved.TriggeredUtc.Value - originalSentUtc).Duration() < TimeSpan.FromSeconds(1));
        Assert.True(saved.LastSentUtc > lastSentUtc);
        Assert.Equal(new[] { 303 }, scenario.Handler.TriggeredMovieIds);
    }

    [Fact]
    public async Task RunQueueProcessingPassAsync_SkippedTriggeredRequest_AllowsLaterQueuedRequestToSend()
    {
        using var scenario = new SearchCoordinatorScenario();
        var recent = scenario.CreateTriggeredRecord("user-1", "Recent Front Movie", 304, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow);
        var queued = scenario.CreatePendingRecord("user-2", "Queued Back Movie", 305);
        scenario.Repository.Save(new[] { recent, queued });
        using var coordinator = scenario.CreateCoordinator();

        await coordinator.RunQueueProcessingPassAsync(CancellationToken.None);

        Assert.DoesNotContain(304, scenario.Handler.TriggeredMovieIds);
        Assert.Contains(305, scenario.Handler.TriggeredMovieIds);
    }

    [Fact]
    public async Task RunTriggeredMonitoringPassAsync_RequestPastOriginalTtl_RemovesRequestEvenWhenLastSendIsRecent()
    {
        using var scenario = new SearchCoordinatorScenario();
        scenario.Options.SearchTimeoutMinutes = 20;
        var expired = scenario.CreateTriggeredRecord("user-1", "Expired Movie", 306, DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow);
        scenario.Repository.Save(new[] { expired });
        using var coordinator = scenario.CreateCoordinator();

        await coordinator.RunTriggeredMonitoringPassAsync(CancellationToken.None);

        Assert.Empty(scenario.Repository.Load());
    }

    private sealed class SearchCoordinatorScenario : IDisposable
    {
        private readonly DirectoryInfo directory;
        private readonly StubBazarrHandler handler = new();
        private readonly BazarrClient bazarrClient;
        private readonly PluginOptions options = new();
        private readonly BazarrCatalogCache catalogCache;
        private readonly MediaMatcher matcher = new();
        private readonly SlidingWindowRateLimiter rateLimiter = new();
        private readonly SubtitleSnapshotService snapshotService = new();
        private readonly NotificationService notificationService;
        private readonly TestLogger logger = new();

        public SearchCoordinatorScenario()
        {
            directory = Directory.CreateTempSubdirectory();
            bazarrClient = new BazarrClient(new HttpClient(handler));
            MediaPath = EnsureMediaFile("Example Movie (2024).mkv");
            Repository = new PendingSearchRepository(directory.FullName);
            catalogCache = new BazarrCatalogCache(bazarrClient);
            NotificationManager = new TestNotificationManager();
            notificationService = new NotificationService(NotificationManager, _ => null);
            Coordinator = CreateCoordinator();
        }

        public string MediaPath { get; }

        public StubBazarrHandler Handler => handler;

        public PluginOptions Options => options;

        public PendingSearchRepository Repository { get; }

        public SearchCoordinator Coordinator { get; }

        public TestNotificationManager NotificationManager { get; }

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
                null,
                logger);

        public PendingSearchRecord CreatePendingRecord(string userId, string title = "Example Movie", int radarrId = 101, int productionYear = 2024)
        {
            var mediaPath = EnsureMediaFile($"{title} ({productionYear}).mkv");
            handler.RegisterMovie(radarrId, title, productionYear, mediaPath);
            var pending = new PendingSearchRecord
            {
                ContentType = VideoContentType.Movie,
                MediaPath = mediaPath,
                Title = title,
                ProductionYear = productionYear,
                RequestedLanguage = "eng",
                ForcedOnly = false,
                RadarrId = radarrId.ToString(),
            };
            pending.AddNotificationUserId(userId);
            return pending;
        }

        public PendingSearchRecord CreateTriggeredRecord(string userId = "user-1", string title = "Example Movie", int radarrId = 101, DateTime? triggeredUtc = null, DateTime? lastSentUtc = null, int productionYear = 2024)
        {
            var pending = CreatePendingRecord(userId, title, radarrId, productionYear);
            pending.State = PendingSearchState.Triggered;
            pending.TriggeredUtc = triggeredUtc ?? DateTime.UtcNow;
            pending.LastSentUtc = lastSentUtc ?? pending.TriggeredUtc;
            pending.Snapshot = snapshotService.Capture(pending.MediaPath);
            return pending;
        }

        private string EnsureMediaFile(string fileName)
        {
            var path = Path.Combine(directory.FullName, fileName);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "video");
            }

            return path;
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            directory.Delete(true);
        }
    }

    private sealed class TestNotificationManager : INotificationManager
    {
        public List<NotificationRequest> Sent { get; } = new();

        public NotificationTokenCategory[] GetNotificationTokens(string category)
            => Array.Empty<NotificationTokenCategory>();

        public NotificationCategoryInfo[] GetNotificationCategories(string category, User user)
            => Array.Empty<NotificationCategoryInfo>();

        public void SaveUserNotification(NotificationInfo notification, User user, string key)
        {
        }

        public void SendNotification(NotificationRequest request)
        {
            Sent.Add(request);
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

    public sealed class StubBazarrHandler : HttpMessageHandler
    {
        private readonly Dictionary<int, (string Title, int Year, string Path)> movies = new();

        public List<int> TriggeredMovieIds { get; } = new();

        public void RegisterMovie(int radarrId, string title, int year, string path)
            => movies[radarrId] = (title, year, path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get && string.Equals(path, "/api/movies", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(new
                {
                    data = movies.Select(item => new
                    {
                        title = item.Value.Title,
                        year = item.Value.Year.ToString(),
                        path = item.Value.Path,
                        radarrId = item.Key,
                        imdbId = (string?)null,
                    }),
                }));
            }

            if (request.Method == HttpMethod.Get && (string.Equals(path, "/api/series", StringComparison.Ordinal) || string.Equals(path, "/api/episodes", StringComparison.Ordinal)))
            {
                return Task.FromResult(JsonResponse(new { data = Array.Empty<object>() }));
            }

            if (request.Method == HttpMethod.Patch && string.Equals(path, "/api/movies", StringComparison.Ordinal))
            {
                if (TryGetQueryInt(request.RequestUri, "radarrid", out var radarrId))
                {
                    TriggeredMovieIds.Add(radarrId);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(object payload)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload)),
            };

        private static bool TryGetQueryInt(Uri? uri, string key, out int value)
        {
            value = 0;
            var query = uri?.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            foreach (var pair in query!.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2
                    && string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(Uri.UnescapeDataString(parts[1]), out value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
