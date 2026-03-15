using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class BazarrCatalogCache
{
    private readonly BazarrClient bazarrClient;
    private readonly object syncRoot = new object();
    private readonly ILogger? logger;
    private BazarrCatalogSnapshot snapshot = new BazarrCatalogSnapshot();
    private DateTime fetchedUtc = DateTime.MinValue;
    private Task<BazarrCatalogSnapshot>? refreshTask;

    public BazarrCatalogCache(BazarrClient bazarrClient, ILogger? logger = null)
    {
        this.bazarrClient = bazarrClient;
        this.logger = logger;
    }

    public async Task<BazarrCatalogSnapshot> GetAsync(PluginOptions configuration, PendingSearchRecord search, CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(configuration.MetadataCacheTtlMinutes, 1));
        Task<BazarrCatalogSnapshot>? inFlightRefresh;
        lock (syncRoot)
        {
            if (DateTime.UtcNow - fetchedUtc < ttl)
            {
                if (configuration.VerboseLogging)
                {
                    logger?.Info($"Using cached Bazarr catalog snapshot for {search.GetDisplayName()}.");
                }

                return snapshot;
            }

            refreshTask ??= RefreshAsync(configuration, search, CancellationToken.None);
            inFlightRefresh = refreshTask;
        }

        return await inFlightRefresh.ConfigureAwait(false);
    }

    private async Task<BazarrCatalogSnapshot> RefreshAsync(PluginOptions configuration, PendingSearchRecord search, CancellationToken cancellationToken)
    {
        if (configuration.VerboseLogging)
        {
            logger?.Info($"Refreshing Bazarr catalog snapshot for {search.GetDisplayName()} after cache expiry.");
        }

        try
        {
            var fresh = await bazarrClient.GetCatalogSnapshotAsync(configuration, search, cancellationToken).ConfigureAwait(false);
            lock (syncRoot)
            {
                snapshot = fresh;
                fetchedUtc = DateTime.UtcNow;
                return snapshot;
            }
        }
        finally
        {
            lock (syncRoot)
            {
                refreshTask = null;
            }
        }
    }
}
