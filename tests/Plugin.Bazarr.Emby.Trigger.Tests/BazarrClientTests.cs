using System.Net;
using System.Net.Http;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Models;
using Plugin.Bazarr.Emby.Trigger.Options;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class BazarrClientTests
{
    [Fact]
    public async Task GetCatalogSnapshotAsync_WithoutSeriesId_SkipsEpisodesRequest()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/movies", StringComparison.Ordinal))
            {
                return CreateJsonResponse("{\"data\":[]}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/series", StringComparison.Ordinal))
            {
                return CreateJsonResponse("{\"data\":[]}");
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var client = new BazarrClient(new HttpClient(handler));
        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Episode,
            SeriesName = "Unknown Show",
            ProductionYear = 2024,
        };

        var snapshot = await client.GetCatalogSnapshotAsync(CreateOptions(), search, CancellationToken.None);

        Assert.Empty(snapshot.Movies);
        Assert.Empty(snapshot.Series);
        Assert.Empty(snapshot.Episodes);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsolutePath.EndsWith("/api/episodes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCatalogSnapshotAsync_WithoutSonarrSeriesId_UsesCachedTvdbMatchToLoadEpisodes()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/movies", StringComparison.Ordinal))
            {
                return CreateJsonResponse("{\"data\":[]}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/series", StringComparison.Ordinal))
            {
                return CreateJsonResponse("{\"data\":[{\"title\":\"Example Show\",\"year\":\"2024\",\"path\":\"/shows/example\",\"sonarrSeriesId\":88,\"tvdbId\":12345}]}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/episodes", StringComparison.Ordinal))
            {
                return CreateJsonResponse("{\"data\":[{\"title\":\"Pilot\",\"season\":1,\"episode\":1,\"sonarrEpisodeId\":99,\"sonarrSeriesId\":88,\"path\":\"/shows/example/s01e01.mkv\"}]}");
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var client = new BazarrClient(new HttpClient(handler));
        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Episode,
            SeriesName = "Example Show",
            ProductionYear = 2024,
            TvdbId = "12345",
        };

        var snapshot = await client.GetCatalogSnapshotAsync(CreateOptions(), search, CancellationToken.None);

        Assert.Single(snapshot.Series);
        Assert.Single(snapshot.Episodes);
        Assert.Contains(handler.Requests, request => request.RequestUri!.AbsolutePath.EndsWith("/api/episodes", StringComparison.Ordinal)
            && request.RequestUri.Query == "?seriesid[]=88");
    }

    [Fact]
    public async Task TriggerSearchAsync_ForMovie_UsesBazarrPatchApi()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new BazarrClient(new HttpClient(handler));
        var search = new PendingSearchRecord
        {
            ContentType = MediaBrowser.Controller.Providers.VideoContentType.Movie,
            Title = "Dune",
            ProductionYear = 2021,
            RequestedLanguage = "eng",
        };
        var match = new MatchResult
        {
            TriggerKind = BazarrTriggerKind.MovieSearchMissing,
            MovieId = 101,
        };

        await client.TriggerSearchAsync(CreateOptions(), search, match, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/api/movies", request.RequestUri!.AbsolutePath);
        Assert.Equal("?radarrid=101&action=search-missing", request.RequestUri.Query);
    }

    private static PluginOptions CreateOptions()
        => new()
        {
            BazarrHost = "http://localhost",
            BazarrPort = 6767,
            BazarrApiKey = "api-key",
        };

    private static HttpResponseMessage CreateJsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            this.responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
