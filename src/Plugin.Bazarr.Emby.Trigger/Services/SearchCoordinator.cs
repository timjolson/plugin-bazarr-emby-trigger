using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ILogger logger;
    private readonly object syncRoot = new object();
    private readonly List<PendingSearchRecord> searches;
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
        this.logger = logger;
        searches = repository.Load();

        if (searches.Any(item => item.NormalizeNotificationUserIds()))
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
            await MonitorTriggeredSearchesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.ErrorException("Background subtitle processing tick failed.", ex);
        }
    }

    private async Task TriggerQueuedSearchesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            PendingSearchRecord? queued;
            lock (syncRoot)
            {
                queued = searches.FirstOrDefault(item => item.State == PendingSearchState.Queued);
            }

            if (queued == null)
            {
                return;
            }

            var options = optionsAccessor();
            if (!rateLimiter.TryAcquire(DateTime.UtcNow, Math.Max(options.SearchesPerHour, 1)))
            {
                if (options.VerboseLogging)
                {
                    var next = rateLimiter.GetNextAvailableUtc(DateTime.UtcNow, Math.Max(options.SearchesPerHour, 1));
                    logger.Info($"Rate limit reached; queued subtitle searches will resume around {next:u}.");
                }

                return;
            }

            queued.LastAttemptUtc = DateTime.UtcNow;
            queued.RetryCount++;

            try
            {
                var catalog = await catalogCache.GetAsync(options, TryParseInt(queued.SonarrSeriesId), cancellationToken).ConfigureAwait(false);
                var match = matcher.Match(queued, catalog);
                if (match == null)
                {
                    queued.LastError = "No Bazarr catalog match was found.";
                    logger.Warn($"No Bazarr match found for queued subtitle request {queued.Id}: {queued.GetDisplayName()}.");
                    Persist();
                    return;
                }

                logger.Info($"Matched queued subtitle request {queued.Id}: {match.Explanation}");
                await bazarrClient.TriggerSearchAsync(options, queued, match, cancellationToken).ConfigureAwait(false);
                queued.State = PendingSearchState.Triggered;
                queued.TriggeredUtc = DateTime.UtcNow;
                queued.BazarrMovieId = match.MovieId == 0 ? queued.BazarrMovieId : match.MovieId;
                queued.BazarrSeriesId = match.SeriesId == 0 ? queued.BazarrSeriesId : match.SeriesId;
                queued.BazarrEpisodeId = match.EpisodeId == 0 ? queued.BazarrEpisodeId : match.EpisodeId;
                queued.LastError = null;
                Persist();
            }
            catch (Exception ex)
            {
                queued.LastError = ex.Message;
                logger.ErrorException($"Bazarr request failed for queued subtitle search {queued.Id}.", ex);
                Persist();
                return;
            }
        }
    }

    private async Task MonitorTriggeredSearchesAsync(CancellationToken cancellationToken)
    {
        List<PendingSearchRecord> completed = new List<PendingSearchRecord>();
        List<PendingSearchRecord> timedOut = new List<PendingSearchRecord>();

        var timeoutMinutes = Math.Max(optionsAccessor().SearchTimeoutMinutes, 1);
        lock (syncRoot)
        {
            foreach (var search in searches.Where(item => item.State == PendingSearchState.Triggered).ToList())
            {
                if (snapshotService.HasNewOrModifiedSubtitle(search.MediaPath, search.Snapshot))
                {
                    search.State = PendingSearchState.Completed;
                    completed.Add(search);
                }
                else if (search.TriggeredUtc.HasValue && search.TriggeredUtc.Value.AddMinutes(timeoutMinutes) < DateTime.UtcNow)
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

        pending.AddNotificationUserId(GetRequestingUserId(request));
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

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().Replace("_", " ").Replace(".", " ");

    private void Persist()
    {
        lock (syncRoot)
        {
            repository.Save(searches);
        }
    }

    public void Dispose()
    {
        timer?.Dispose();
    }
}
