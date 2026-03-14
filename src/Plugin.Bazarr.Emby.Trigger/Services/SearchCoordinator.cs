using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Configuration;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class SearchCoordinator : IDisposable
{
    private readonly PluginConfiguration configuration;
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
        PluginConfiguration configuration,
        BazarrClient bazarrClient,
        BazarrCatalogCache catalogCache,
        MediaMatcher matcher,
        SlidingWindowRateLimiter rateLimiter,
        SubtitleSnapshotService snapshotService,
        PendingSearchRepository repository,
        NotificationService notificationService,
        ILogger logger)
    {
        this.configuration = configuration;
        this.bazarrClient = bazarrClient;
        this.catalogCache = catalogCache;
        this.matcher = matcher;
        this.rateLimiter = rateLimiter;
        this.snapshotService = snapshotService;
        this.repository = repository;
        this.notificationService = notificationService;
        this.logger = logger;
        searches = repository.Load();
    }

    public Task QueueAsync(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var pending = CreatePendingRecord(request);
        lock (syncRoot)
        {
            searches.Add(pending);
            repository.Save(searches);
        }

        logger.Info($"Queued subtitle request {pending.Id} for {pending.GetDisplayName()} ({pending.RequestedLanguage}, forced={pending.ForcedOnly}).");
        return Task.CompletedTask;
    }

    public void Start()
    {
        timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(Math.Max(configuration.QueuePollIntervalSeconds, 5)));
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

            if (!rateLimiter.TryAcquire(DateTime.UtcNow, Math.Max(configuration.SearchesPerHour, 1)))
            {
                if (configuration.VerboseLogging)
                {
                    var next = rateLimiter.GetNextAvailableUtc(DateTime.UtcNow, Math.Max(configuration.SearchesPerHour, 1));
                    logger.Info($"Rate limit reached; queued subtitle searches will resume around {next:u}.");
                }

                return;
            }

            queued.LastAttemptUtc = DateTime.UtcNow;
            queued.RetryCount++;

            try
            {
                var catalog = await catalogCache.GetAsync(configuration, TryParseInt(queued.SonarrSeriesId), cancellationToken).ConfigureAwait(false);
                var match = matcher.Match(queued, catalog);
                if (match == null)
                {
                    queued.LastError = "No Bazarr catalog match was found.";
                    logger.Warn($"No Bazarr match found for queued subtitle request {queued.Id}: {queued.GetDisplayName()}.");
                    Persist();
                    return;
                }

                logger.Info($"Matched queued subtitle request {queued.Id}: {match.Explanation}");
                await bazarrClient.TriggerSearchAsync(configuration, queued, match, cancellationToken).ConfigureAwait(false);
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

        lock (syncRoot)
        {
            foreach (var search in searches.Where(item => item.State == PendingSearchState.Triggered).ToList())
            {
                if (snapshotService.HasNewOrModifiedSubtitle(search.MediaPath, search.Snapshot))
                {
                    search.State = PendingSearchState.Completed;
                    completed.Add(search);
                }
                else if (search.TriggeredUtc.HasValue && search.TriggeredUtc.Value.AddMinutes(Math.Max(configuration.SearchTimeoutMinutes, 1)) < DateTime.UtcNow)
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
        return new PendingSearchRecord
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
