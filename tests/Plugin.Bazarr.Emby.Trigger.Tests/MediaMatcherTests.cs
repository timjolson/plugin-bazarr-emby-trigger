using MediaBrowser.Controller.Providers;
using Plugin.Bazarr.Emby.Trigger.Models;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class MediaMatcherTests
{
    [Fact]
    public void Match_PrefersIdMatchesForMovies()
    {
        var matcher = new MediaMatcher();
        var search = new PendingSearchRecord
        {
            ContentType = VideoContentType.Movie,
            Title = "Dune",
            ProductionYear = 2021,
            RadarrId = "101",
            MediaPath = "/library/movies/Dune (2021).mkv",
        };
        var catalog = new BazarrCatalogSnapshot
        {
            Movies = new[]
            {
                new BazarrMovieRecord { Title = "Dune", Year = "2021", RadarrId = 101, Path = "/other/path.mkv" },
                new BazarrMovieRecord { Title = "Dune", Year = "2021", RadarrId = 202, Path = search.MediaPath },
            },
        };

        var result = matcher.Match(search, catalog);

        Assert.NotNull(result);
        Assert.Equal(BazarrTriggerKind.MovieSearchMissing, result!.TriggerKind);
        Assert.Equal(101, result.MovieId);
    }

    [Fact]
    public void Match_FallsBackToPathMatchingForEpisodes()
    {
        var matcher = new MediaMatcher();
        var search = new PendingSearchRecord
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Example Show",
            Title = "Pilot",
            SeasonNumber = 1,
            EpisodeNumber = 1,
            MediaPath = "/library/shows/Example Show/Season 01/Example Show - S01E01.mkv",
        };
        var catalog = new BazarrCatalogSnapshot
        {
            Series = new[] { new BazarrSeriesRecord { Title = "Example Show", Year = "2024", SonarrSeriesId = 88 } },
            Episodes = new[]
            {
                new BazarrEpisodeRecord
                {
                    Title = "Pilot",
                    Season = 1,
                    Episode = 1,
                    SonarrSeriesId = 88,
                    SonarrEpisodeId = 99,
                    Path = search.MediaPath,
                },
            },
        };

        var result = matcher.Match(search, catalog);

        Assert.NotNull(result);
        Assert.Equal(BazarrTriggerKind.EpisodeManualTopResult, result!.TriggerKind);
        Assert.Equal(99, result.EpisodeId);
        Assert.Equal(88, result.SeriesId);
    }
}
