using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Bazarr.Emby.Trigger.Configuration;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class BazarrCatalogCache
{
    private readonly BazarrClient bazarrClient;
    private readonly object syncRoot = new object();
    private BazarrCatalogSnapshot snapshot = new BazarrCatalogSnapshot();
    private DateTime fetchedUtc = DateTime.MinValue;

    public BazarrCatalogCache(BazarrClient bazarrClient)
    {
        this.bazarrClient = bazarrClient;
    }

    public async Task<BazarrCatalogSnapshot> GetAsync(PluginConfiguration configuration, int? seriesId, CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(configuration.MetadataCacheTtlMinutes, 1));
        lock (syncRoot)
        {
            if (DateTime.UtcNow - fetchedUtc < ttl)
            {
                return snapshot;
            }
        }

        var fresh = await bazarrClient.GetCatalogSnapshotAsync(configuration, seriesId, cancellationToken).ConfigureAwait(false);
        lock (syncRoot)
        {
            snapshot = fresh;
            fetchedUtc = DateTime.UtcNow;
            return snapshot;
        }
    }
}
