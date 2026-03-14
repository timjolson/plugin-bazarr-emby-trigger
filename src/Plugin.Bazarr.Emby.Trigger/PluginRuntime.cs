using System;
using System.IO;
using System.Net.Http;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Configuration;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger;

internal static class PluginRuntime
{
    private static readonly object SyncRoot = new object();
    private static SearchCoordinator? coordinator;
    private static ILogger? logger;

    public static void Initialize(PluginConfiguration configuration, INotificationManager notificationManager, ILogManager logManager, string pluginDataDirectory)
    {
        lock (SyncRoot)
        {
            if (coordinator != null)
            {
                return;
            }

            logger = logManager.GetLogger("BazarrEmbyTrigger");
            var client = new BazarrClient(new HttpClient());
            coordinator = new SearchCoordinator(
                configuration,
                client,
                new BazarrCatalogCache(client),
                new MediaMatcher(),
                new SlidingWindowRateLimiter(),
                new SubtitleSnapshotService(),
                new PendingSearchRepository(pluginDataDirectory),
                new NotificationService(notificationManager),
                logger);
            coordinator.Start();
            logger.Info("Bazarr Emby Trigger runtime initialized.");
        }
    }

    public static SearchCoordinator? Coordinator => coordinator;

    public static ILogger? Logger => logger;
}
