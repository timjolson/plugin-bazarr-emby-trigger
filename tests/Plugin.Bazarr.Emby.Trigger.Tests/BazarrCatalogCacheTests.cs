using System.Net;
using System.Net.Http;
using System.Threading;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class BazarrCatalogCacheTests
{
    [Fact]
    public async Task GetAsync_ConcurrentCacheMisses_ShareSingleRefresh()
    {
        var handler = new DelayedRecordingHandler();
        var client = new BazarrClient(new HttpClient(handler));
        var cache = new BazarrCatalogCache(client);
        var options = new PluginOptions
        {
            BazarrHost = "http://localhost",
            BazarrPort = 6767,
            BazarrApiKey = "api-key",
            MetadataCacheTtlMinutes = 30,
            VerboseLogging = false,
        };
        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Movie,
            Title = "Dune",
            ProductionYear = 2021,
            MediaPath = "/library/movies/Dune (2021).mkv",
        };

        await Task.WhenAll(
            cache.GetAsync(options, search, CancellationToken.None),
            cache.GetAsync(options, search, CancellationToken.None),
            cache.GetAsync(options, search, CancellationToken.None));

        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class DelayedRecordingHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            await Task.Delay(50, cancellationToken);

            if (request.RequestUri!.AbsolutePath.EndsWith("/api/movies", StringComparison.Ordinal)
                || request.RequestUri.AbsolutePath.EndsWith("/api/series", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}"),
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        }
    }
}
