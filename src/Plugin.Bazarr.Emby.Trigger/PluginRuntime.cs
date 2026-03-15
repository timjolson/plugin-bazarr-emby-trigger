using System;
using System.Collections;
using System.Linq;
using System.Net.Http;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger;

internal static class PluginRuntime
{
    private static readonly object SyncRoot = new object();
    private static SearchCoordinator? coordinator;
    private static ILogger? logger;

    public static void Initialize(Func<Options.PluginOptions> optionsAccessor, INotificationManager notificationManager, IUserManager userManager, ILibraryManager libraryManager, ILogManager logManager, string pluginDataDirectory)
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
                optionsAccessor,
                client,
                new BazarrCatalogCache(client),
                new MediaMatcher(),
                new SlidingWindowRateLimiter(),
                new SubtitleSnapshotService(),
                new PendingSearchRepository(pluginDataDirectory),
                new NotificationService(notificationManager, userManager),
                () => GetMostRecentlyActiveUserId(userManager),
                libraryManager,
                logger);
            coordinator.Start();
            logger.Info("Bazarr Emby Trigger runtime initialized.");
        }
    }

    public static SearchCoordinator? Coordinator => coordinator;

    public static ILogger? Logger => logger;

    private static string? GetMostRecentlyActiveUserId(IUserManager userManager)
    {
        try
        {
            var getUsers = typeof(IUserManager).GetMethod("GetUsers", Type.EmptyTypes);
            var users = getUsers?.Invoke(userManager, Array.Empty<object>()) as IEnumerable;
            if (users == null)
            {
                return null;
            }

            return users
                .Cast<object>()
                .OrderByDescending(GetLastActivityDate)
                .ThenByDescending(GetLastLoginDate)
                .Select(GetUserId)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        }
        catch
        {
            return null;
        }
    }

    private static DateTime GetLastActivityDate(object user)
        => GetDateTimeProperty(user, "LastActivityDate") ?? DateTime.MinValue;

    private static DateTime GetLastLoginDate(object user)
        => GetDateTimeProperty(user, "LastLoginDate") ?? DateTime.MinValue;

    private static DateTime? GetDateTimeProperty(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName);
        if (property == null)
        {
            return null;
        }

        var value = property.GetValue(source);
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        return null;
    }

    private static string? GetUserId(object user)
    {
        foreach (var propertyName in new[] { "Id", "UserId" })
        {
            var property = user.GetType().GetProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            var value = property.GetValue(user);
            if (value is Guid guidValue && guidValue != Guid.Empty)
            {
                return guidValue.ToString("D");
            }

            if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue.Trim();
            }
        }

        return null;
    }
}
