using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class SearchCoordinator : IDisposable
{
    private static readonly string[] RequestingUserPropertyNames = { "RequestingUserId", "UserId", "RequestedByUserId", "User" };
    private static readonly string[] NestedUserIdPropertyNames = { "Id", "UserId" };
    private readonly Func<PluginOptions> optionsAccessor;
    private readonly BazarrClient bazarrClient;
    private readonly BazarrCatalogCache catalogCache;
    private readonly MediaMatcher matcher;
    private readonly SlidingWindowRateLimiter rateLimiter;
    private readonly SubtitleSnapshotService snapshotService;
    private readonly PendingSearchRepository repository;
    private readonly NotificationService notificationService;
    private readonly Func<string?>? fallbackRequestingUserIdAccessor;
    private readonly ILogger logger;
    private readonly ILibraryManager? libraryManager;
    private readonly object syncRoot = new object();
    private readonly List<PendingSearchRecord> searches;
    private DateTime? nextAutomatedConnectionAttemptUtc;
    private string? lastConnectionFailureMessage;
    private DateTime? lastConnectionNotificationUtc;
    private DateTime? lastApiNotificationUtc;
    private DateTime? lastMatchNotificationUtc;
    private Timer? timer;

    public SearchCoordinator(
        Func<PluginOptions> optionsAccessor,
        BazarrClient bazarrClient,
        BazarrCatalogCache catalogCache,
        MediaMatcher matcher,
        SlidingWindowRateLimiter rateLimiter,
        SubtitleSnapshotService snapshotService,
        PendingSearchRepository repository,
        NotificationService notificationService,
        Func<string?>? fallbackRequestingUserIdAccessor,
        ILibraryManager? libraryManager,
        ILogger logger)
    {
        this.optionsAccessor = optionsAccessor;
        this.bazarrClient = bazarrClient;
        this.catalogCache = catalogCache;
        this.matcher = matcher;
        this.rateLimiter = rateLimiter;
        this.snapshotService = snapshotService;
        this.repository = repository;
        this.notificationService = notificationService;
        this.fallbackRequestingUserIdAccessor = fallbackRequestingUserIdAccessor;
        this.libraryManager = libraryManager;
        this.logger = logger;
        searches = repository.Load();

        if (searches.Any(item => item.NormalizeNotificationUserIds() || item.NormalizeSendTimestamps()))
        {
            repository.Save(searches);
        }
    }

    public Task QueueAsync(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var pending = CreatePendingRecord(request);
        return QueueAsync(pending, cancellationToken);
    }

    internal Task QueueAsync(PendingSearchRecord pending, CancellationToken cancellationToken)
    {
        PendingSearchRecord? existing;
        lock (syncRoot)
        {
            existing = searches.FirstOrDefault(item =>
                (item.State == PendingSearchState.Queued || item.State == PendingSearchState.Triggered)
                && IsEquivalentSearch(item, pending));

            if (existing == null)
            {
                searches.Add(pending);
            }
            else
            {
                MergePendingRequest(existing, pending);
            }

            repository.Save(searches);
        }

        if (existing != null)
        {
            logger.Info($"Merged subtitle request into pending search {existing.Id} for {existing.GetDisplayName()} ({existing.RequestedLanguage}, forced={existing.ForcedOnly}).");
            return Task.CompletedTask;
        }

        logger.Info($"Queued subtitle request {pending.Id} for {pending.GetDisplayName()} ({pending.RequestedLanguage}, forced={pending.ForcedOnly}).");
        return Task.CompletedTask;
    }

    public void Start()
    {
        var options = optionsAccessor();
        libraryManager?.ItemAdded += OnLibraryItemChanged;
        libraryManager?.ItemUpdated += OnLibraryItemChanged;
        timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(Math.Max(options.QueuePollIntervalSeconds, 5)));
    }

    private void Tick()
    {
        _ = TickAsync(CancellationToken.None);
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TriggerQueuedSearchesAsync(cancellationToken).ConfigureAwait(false);
            await MonitorTriggeredSearchesAsync(optionsAccessor().PollMediaFolders, null, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.ErrorException("Background subtitle processing tick failed.", ex);
        }
    }

    private async Task TriggerQueuedSearchesAsync(CancellationToken cancellationToken)
    {
        var options = optionsAccessor();
        var now = DateTime.UtcNow;
        var searchesPerHour = Math.Max(options.SearchesPerHour, 1);
        var timeoutMinutes = Math.Max(options.SearchTimeoutMinutes, 1);
        var perRequestRetryDelay = TimeSpan.FromHours(1d / searchesPerHour);
        var rateLimitLogged = false;

        List<string> activeSearchIds;
        lock (syncRoot)
        {
            activeSearchIds = searches
                .Where(IsActiveSearch)
                .Select(item => item.Id)
                .ToList();
        }

        foreach (var searchId in activeSearchIds)
        {
            PendingSearchRecord? queued;
            lock (syncRoot)
            {
                queued = searches.FirstOrDefault(item => item.Id == searchId);
            }

            if (queued == null || !IsActiveSearch(queued))
            {
                continue;
            }

            if (queued.State == PendingSearchState.Triggered && HasRequestExpired(queued, now, timeoutMinutes))
            {
                // Expired triggered requests are removed by the monitoring pass that runs
                // after queue processing in the normal background tick.
                continue;
            }

            if (queued.State == PendingSearchState.Triggered
                && queued.LastSentUtc.HasValue
                && queued.LastSentUtc.Value.Add(perRequestRetryDelay) > now)
            {
                MoveToBack(queued);
                Persist();
                continue;
            }

            if (queued.State == PendingSearchState.Queued
                && queued.LastAttemptUtc.HasValue
                && queued.LastAttemptUtc.Value.Add(perRequestRetryDelay) > now)
            {
                MoveToBack(queued);
                Persist();
                continue;
            }

            if (nextAutomatedConnectionAttemptUtc.HasValue && nextAutomatedConnectionAttemptUtc.Value > now)
            {
                queued.LastError = lastConnectionFailureMessage ?? "Bazarr connection is temporarily paused after a recent failure.";
                await NotifySearchFailureAsync(
                        queued,
                        BazarrRequestFailureKind.Connection,
                        queued.LastError,
                        now,
                        perRequestRetryDelay,
                        cancellationToken,
                        respectRateLimit: true)
                    .ConfigureAwait(false);
                MoveToBack(queued);
                Persist();
                continue;
            }

            if (!rateLimiter.TryAcquire(now, searchesPerHour))
            {
                if (options.VerboseLogging && !rateLimitLogged)
                {
                    var next = rateLimiter.GetNextAvailableUtc(now, searchesPerHour);
                    logger.Info($"Rate limit reached; queued subtitle searches will resume around {next:u}.");
                    rateLimitLogged = true;
                }

                if (queued.State == PendingSearchState.Triggered)
                {
                    MoveToBack(queued);
                    Persist();
                    continue;
                }

                return;
            }

            try
            {
                queued.LastAttemptUtc = now;
                queued.RetryCount++;
                var catalog = await catalogCache.GetAsync(options, TryParseInt(queued.SonarrSeriesId), cancellationToken).ConfigureAwait(false);
                ResetConnectionFailureState();
                ResetApiNotificationState();
                var match = matcher.Match(queued, catalog);
                if (match == null)
                {
                    queued.LastError = "No Bazarr catalog match was found.";
                    logger.Warn($"No Bazarr match found for queued subtitle request {queued.Id}: {queued.GetDisplayName()}.");
                    await NotifyMatchFailureAsync(queued, now, perRequestRetryDelay, cancellationToken).ConfigureAwait(false);
                    MoveToBack(queued);
                    Persist();
                    continue;
                }

                logger.Info($"Matched queued subtitle request {queued.Id}: {match.Explanation}");
                await bazarrClient.TriggerSearchAsync(options, queued, match, cancellationToken).ConfigureAwait(false);
                ResetConnectionFailureState();
                ResetApiNotificationState();
                queued.State = PendingSearchState.Triggered;
                // Preserve the original successful Bazarr send time for TTL checks even when
                // older queue files or future migrations populate TriggeredUtc ahead of resends.
                queued.TriggeredUtc ??= now;
                queued.LastSentUtc = now;
                queued.BazarrMovieId = match.MovieId == 0 ? queued.BazarrMovieId : match.MovieId;
                queued.BazarrSeriesId = match.SeriesId == 0 ? queued.BazarrSeriesId : match.SeriesId;
                queued.BazarrEpisodeId = match.EpisodeId == 0 ? queued.BazarrEpisodeId : match.EpisodeId;
                queued.LastError = null;
                queued.LastErrorNotificationUtc = null;
                MoveToBack(queued);
                Persist();
            }
            catch (BazarrRequestException ex)
            {
                queued.LastError = ex.Message;
                logger.ErrorException($"Bazarr request failed for queued subtitle search {queued.Id}.", ex);
                if (ex.Kind == BazarrRequestFailureKind.Connection)
                {
                    nextAutomatedConnectionAttemptUtc = now.Add(perRequestRetryDelay);
                    lastConnectionFailureMessage = ex.Message;
                }

                await NotifySearchFailureAsync(
                        queued,
                        ex.Kind,
                        ex.Message,
                        now,
                        perRequestRetryDelay,
                        cancellationToken,
                        respectRateLimit: true)
                    .ConfigureAwait(false);
                MoveToBack(queued);
                Persist();
                continue;
            }
            catch (Exception ex)
            {
                queued.LastError = ex.Message;
                logger.ErrorException($"Bazarr request failed for queued subtitle search {queued.Id}.", ex);
                await NotifySearchFailureAsync(
                        queued,
                        BazarrRequestFailureKind.Api,
                        ex.Message,
                        now,
                        perRequestRetryDelay,
                        cancellationToken,
                        respectRateLimit: true)
                    .ConfigureAwait(false);
                MoveToBack(queued);
                Persist();
                continue;
            }
        }
    }

    internal Task RunQueueProcessingPassAsync(CancellationToken cancellationToken)
        => TriggerQueuedSearchesAsync(cancellationToken);

    internal Task HandleLibraryItemChangeAsync(string? itemPath, string? parentPath, CancellationToken cancellationToken)
    {
        if (optionsAccessor().PollMediaFolders)
        {
            return Task.CompletedTask;
        }

        return MonitorTriggeredSearchesAsync(true, itemPath, parentPath, cancellationToken);
    }

    internal Task RunTriggeredMonitoringPassAsync(CancellationToken cancellationToken)
        => MonitorTriggeredSearchesAsync(optionsAccessor().PollMediaFolders, null, null, cancellationToken);

    private async Task MonitorTriggeredSearchesAsync(bool checkForSubtitleChanges, string? changedItemPath, string? changedParentPath, CancellationToken cancellationToken)
    {
        List<PendingSearchRecord> completed = new List<PendingSearchRecord>();
        List<PendingSearchRecord> timedOut = new List<PendingSearchRecord>();

        var timeoutMinutes = Math.Max(optionsAccessor().SearchTimeoutMinutes, 1);
        lock (syncRoot)
        {
            foreach (var search in searches.Where(item => item.State == PendingSearchState.Triggered).ToList())
            {
                if (checkForSubtitleChanges
                    && IsRelevantLibraryChange(search, changedItemPath, changedParentPath)
                    && snapshotService.HasNewOrModifiedSubtitle(search.MediaPath, search.Snapshot))
                {
                    search.State = PendingSearchState.Completed;
                    completed.Add(search);
                }
                else if (HasRequestExpired(search, DateTime.UtcNow, timeoutMinutes))
                {
                    search.State = PendingSearchState.TimedOut;
                    timedOut.Add(search);
                }
            }
        }

        foreach (var item in completed)
        {
            logger.Info($"Detected new subtitle file for {item.GetDisplayName()}; sending notification.");
            await notificationService.NotifySubtitleArrivalAsync(item, cancellationToken).ConfigureAwait(false);
        }

        foreach (var item in timedOut)
        {
            logger.Warn($"Subtitle search timed out for {item.GetDisplayName()}; sending timeout notification.");
            await notificationService.NotifyTimeoutAsync(item, cancellationToken).ConfigureAwait(false);
        }

        if (completed.Count > 0 || timedOut.Count > 0)
        {
            lock (syncRoot)
            {
                searches.RemoveAll(item => item.State == PendingSearchState.Completed || item.State == PendingSearchState.TimedOut);
                repository.Save(searches);
            }
        }
    }

    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (optionsAccessor().PollMediaFolders)
        {
            return;
        }

        _ = HandleLibraryItemChangeSafeAsync(e.Item?.Path, e.Parent?.Path);
    }

    private async Task HandleLibraryItemChangeSafeAsync(string? itemPath, string? parentPath)
    {
        try
        {
            await HandleLibraryItemChangeAsync(itemPath, parentPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.ErrorException("Event-driven subtitle monitoring failed.", ex);
        }
    }

    private PendingSearchRecord CreatePendingRecord(SubtitleSearchRequest request)
    {
        var pending = new PendingSearchRecord
        {
            ContentType = request.ContentType,
            MediaPath = request.MediaPath ?? string.Empty,
            Title = request.Name ?? string.Empty,
            SeriesName = request.SeriesName ?? string.Empty,
            ProductionYear = request.ProductionYear,
            SeasonNumber = request.ParentIndexNumber,
            EpisodeNumber = request.IndexNumber,
            RequestedLanguage = request.Language ?? string.Empty,
            ForcedOnly = request.IsForced ?? false,
            TmdbId = GetProviderId(request.ProviderIds, "Tmdb"),
            TvdbId = GetProviderId(request.SeriesProviderIds, "Tvdb") ?? GetProviderId(request.ProviderIds, "Tvdb"),
            RadarrId = GetProviderId(request.ProviderIds, "Radarr"),
            SonarrSeriesId = GetProviderId(request.SeriesProviderIds, "Sonarr") ?? GetProviderId(request.SeriesProviderIds, "SonarrSeries"),
            SonarrEpisodeId = GetProviderId(request.ProviderIds, "SonarrEpisode") ?? GetProviderId(request.ProviderIds, "Sonarr"),
            ImdbId = GetProviderId(request.ProviderIds, "Imdb") ?? GetProviderId(request.SeriesProviderIds, "Imdb"),
            Snapshot = snapshotService.Capture(request.MediaPath ?? string.Empty),
        };

        pending.AddNotificationUserId(GetRequestingUserId(request) ?? fallbackRequestingUserIdAccessor?.Invoke());
        return pending;
    }

    private static string? GetProviderId(System.Collections.Generic.IDictionary<string, string>? providerIds, string key)
    {
        if (providerIds == null)
        {
            return null;
        }

        foreach (var pair in providerIds)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static int? TryParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static string? GetRequestingUserId(SubtitleSearchRequest request)
    {
        var requestType = request.GetType();
        foreach (var propertyName in RequestingUserPropertyNames)
        {
            var property = requestType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(request);
            }
            catch
            {
                continue;
            }

            var userId = ExtractUserId(value);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return userId;
            }
        }

        return null;
    }

    private static string? ExtractUserId(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is string stringValue)
        {
            return string.IsNullOrWhiteSpace(stringValue) ? null : stringValue.Trim();
        }

        if (value is Guid guidValue)
        {
            return guidValue == Guid.Empty ? null : guidValue.ToString("D");
        }

        var runtimeType = value.GetType();
        foreach (var propertyName in NestedUserIdPropertyNames)
        {
            var property = runtimeType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? nestedValue;
            try
            {
                nestedValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            var userId = ExtractUserId(nestedValue);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return userId;
            }
        }

        return null;
    }

    private static void MergePendingRequest(PendingSearchRecord existing, PendingSearchRecord incoming)
    {
        existing.NormalizeNotificationUserIds();
        incoming.NormalizeNotificationUserIds();

        foreach (var userId in incoming.GetNotificationUserIds())
        {
            existing.AddNotificationUserId(userId);
        }

        existing.RadarrId ??= incoming.RadarrId;
        existing.SonarrSeriesId ??= incoming.SonarrSeriesId;
        existing.SonarrEpisodeId ??= incoming.SonarrEpisodeId;
        existing.TmdbId ??= incoming.TmdbId;
        existing.TvdbId ??= incoming.TvdbId;
        existing.ImdbId ??= incoming.ImdbId;
        existing.BazarrMovieId ??= incoming.BazarrMovieId;
        existing.BazarrSeriesId ??= incoming.BazarrSeriesId;
        existing.BazarrEpisodeId ??= incoming.BazarrEpisodeId;
    }

    private async Task NotifyMatchFailureAsync(PendingSearchRecord queued, DateTime now, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        if (!ShouldNotify(queued, lastMatchNotificationUtc, now, retryDelay, respectRateLimit: true))
        {
            return;
        }

        await notificationService.NotifyMatchFailureAsync(queued, cancellationToken).ConfigureAwait(false);
        if (!HasImmediateNotificationRequestor(queued))
        {
            lastMatchNotificationUtc = now;
        }

        queued.LastErrorNotificationUtc = now;
    }

    private async Task NotifySearchFailureAsync(
        PendingSearchRecord queued,
        BazarrRequestFailureKind failureKind,
        string error,
        DateTime now,
        TimeSpan retryDelay,
        CancellationToken cancellationToken,
        bool respectRateLimit)
    {
        var lastNotificationUtc = failureKind == BazarrRequestFailureKind.Connection
            ? lastConnectionNotificationUtc
            : lastApiNotificationUtc;

        if (!ShouldNotify(queued, lastNotificationUtc, now, retryDelay, respectRateLimit))
        {
            return;
        }

        if (failureKind == BazarrRequestFailureKind.Connection)
        {
            await notificationService.NotifyConnectionFailureAsync(queued, error, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await notificationService.NotifyApiFailureAsync(queued, error, cancellationToken).ConfigureAwait(false);
        }

        if (!HasImmediateNotificationRequestor(queued))
        {
            if (failureKind == BazarrRequestFailureKind.Connection)
            {
                lastConnectionNotificationUtc = now;
            }
            else
            {
                lastApiNotificationUtc = now;
            }
        }

        queued.LastErrorNotificationUtc = now;
    }

    private static bool ShouldNotify(
        PendingSearchRecord queued,
        DateTime? lastNotificationUtc,
        DateTime now,
        TimeSpan retryDelay,
        bool respectRateLimit)
    {
        if (HasImmediateNotificationRequestor(queued))
        {
            return !queued.LastErrorNotificationUtc.HasValue
                || !queued.LastAttemptUtc.HasValue
                || queued.LastErrorNotificationUtc.Value < queued.LastAttemptUtc.Value;
        }

        if (!respectRateLimit)
        {
            return true;
        }

        return !lastNotificationUtc.HasValue || lastNotificationUtc.Value.Add(retryDelay) <= now;
    }

    private static bool HasImmediateNotificationRequestor(PendingSearchRecord queued)
        => queued.HasNotificationUserIds();

    private void ResetConnectionFailureState()
    {
        nextAutomatedConnectionAttemptUtc = null;
        lastConnectionFailureMessage = null;
        lastConnectionNotificationUtc = null;
    }

    private void ResetApiNotificationState()
    {
        lastApiNotificationUtc = null;
    }

    private static bool IsEquivalentSearch(PendingSearchRecord left, PendingSearchRecord right)
    {
        if (left.ContentType != right.ContentType
            || left.ForcedOnly != right.ForcedOnly
            || !string.Equals(left.RequestedLanguage, right.RequestedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (PathsEquivalent(left.MediaPath, right.MediaPath))
        {
            return true;
        }

        if (left.ContentType == VideoContentType.Movie)
        {
            return SharedId(left.RadarrId, right.RadarrId)
                || SharedId(left.ImdbId, right.ImdbId)
                || SharedId(left.TmdbId, right.TmdbId)
                || (string.Equals(Normalize(left.Title), Normalize(right.Title), StringComparison.OrdinalIgnoreCase)
                    && left.ProductionYear == right.ProductionYear);
        }

        return SharedId(left.SonarrEpisodeId, right.SonarrEpisodeId)
            || (SharedId(left.SonarrSeriesId, right.SonarrSeriesId)
                && left.SeasonNumber == right.SeasonNumber
                && left.EpisodeNumber == right.EpisodeNumber)
            || (SharedId(left.TvdbId, right.TvdbId)
                && left.SeasonNumber == right.SeasonNumber
                && left.EpisodeNumber == right.EpisodeNumber)
            || (string.Equals(Normalize(left.SeriesName), Normalize(right.SeriesName), StringComparison.OrdinalIgnoreCase)
                && left.SeasonNumber == right.SeasonNumber
                && left.EpisodeNumber == right.EpisodeNumber);
    }

    private static bool SharedId(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var normalizedLeft = left!.Trim();
        var normalizedRight = right!.Trim();
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().Replace("_", " ").Replace(".", " ");

    private static bool IsActiveSearch(PendingSearchRecord search)
        => search.State == PendingSearchState.Queued || search.State == PendingSearchState.Triggered;

    private static bool HasRequestExpired(PendingSearchRecord search, DateTime now, int timeoutMinutes)
        => search.TriggeredUtc.HasValue && search.TriggeredUtc.Value.AddMinutes(timeoutMinutes) < now;

    private static bool IsRelevantLibraryChange(PendingSearchRecord search, string? changedItemPath, string? changedParentPath)
    {
        if (string.IsNullOrWhiteSpace(changedItemPath) && string.IsNullOrWhiteSpace(changedParentPath))
        {
            return true;
        }

        if (PathsEquivalent(search.MediaPath, changedItemPath))
        {
            return true;
        }

        var searchDirectory = GetDirectoryPath(search.MediaPath);
        var changedItemDirectory = GetDirectoryPath(changedItemPath);
        if (string.IsNullOrWhiteSpace(searchDirectory))
        {
            return false;
        }

        return PathsEquivalent(searchDirectory, changedItemPath)
            || PathsEquivalent(searchDirectory, changedParentPath)
            || PathsEquivalent(searchDirectory, changedItemDirectory);
    }

    private static string? GetDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }

    private void Persist()
    {
        lock (syncRoot)
        {
            repository.Save(searches);
        }
    }

    private void MoveToBack(PendingSearchRecord search)
    {
        lock (syncRoot)
        {
            var removed = searches.Remove(search);
            if (removed)
            {
                searches.Add(search);
            }
        }
    }

    public void Dispose()
    {
        if (libraryManager != null)
        {
            libraryManager.ItemAdded -= OnLibraryItemChanged;
            libraryManager.ItemUpdated -= OnLibraryItemChanged;
        }

        timer?.Dispose();
    }
}
