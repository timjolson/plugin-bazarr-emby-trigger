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

    [Fact]
    public void Match_FallsBackToTmdbAndUniqueTitleYearForMovies()
    {
        var matcher = new MediaMatcher();
        var search = new PendingSearchRecord
        {
            ContentType = VideoContentType.Movie,
            Title = "Dune",
            ProductionYear = 2021,
            TmdbId = "438631",
            MediaPath = "/library/movies/other-file.mkv",
        };
        var catalog = new BazarrCatalogSnapshot
        {
            Movies = new[]
            {
                new BazarrMovieRecord { Title = "Dune", Year = "1984", RadarrId = 11, TmdbId = 841, Path = "/library/movies/dune-1984.mkv" },
                new BazarrMovieRecord { Title = "Dune", Year = "2021", RadarrId = 22, TmdbId = 438631, Path = "/library/movies/dune-2021.mkv" },
            },
        };

        var result = matcher.Match(search, catalog);

        Assert.NotNull(result);
        Assert.Equal(22, result!.MovieId);
        Assert.Equal("Matched by TMDB ID fallback.", result.Explanation);
    }

    [Fact]
    public void Match_FallsBackToUniqueCachedTitleYearForMovies()
    {
        var matcher = new MediaMatcher();
        var search = new PendingSearchRecord
        {
            ContentType = VideoContentType.Movie,
            Title = "Arrival",
            ProductionYear = 2016,
            MediaPath = "/library/movies/unknown-arrival.mkv",
        };
        var catalog = new BazarrCatalogSnapshot
        {
            Movies = new[]
            {
                new BazarrMovieRecord { Title = "Arrival", Year = "2016", RadarrId = 77, Path = "/different/path.mkv" },
            },
        };

        var result = matcher.Match(search, catalog);

        Assert.NotNull(result);
        Assert.Equal(77, result!.MovieId);
        Assert.Equal("Matched by cached title/year fallback.", result.Explanation);
    }

    [Fact]
    public void Match_FallsBackToCachedSeriesTitleYearForEpisodes()
    {
        var matcher = new MediaMatcher();
        var search = new PendingSearchRecord
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Example Show",
            Title = "Pilot",
            ProductionYear = 2024,
            SeasonNumber = 1,
            EpisodeNumber = 1,
            MediaPath = "/library/shows/unknown.mkv",
        };
        var catalog = new BazarrCatalogSnapshot
        {
            Series = new[]
            {
                new BazarrSeriesRecord { Title = "Example Show", Year = "2024", SonarrSeriesId = 88 },
            },
            Episodes = new[]
            {
                new BazarrEpisodeRecord
                {
                    Title = "Pilot",
                    Season = 1,
                    Episode = 1,
                    SonarrSeriesId = 88,
                    SonarrEpisodeId = 99,
                    Path = "/different/path.mkv",
                },
            },
        };

        var result = matcher.Match(search, catalog);

        Assert.NotNull(result);
        Assert.Equal(99, result!.EpisodeId);
        Assert.Equal(88, result.SeriesId);
        Assert.Equal("Matched by cached series title/year fallback plus episode numbers.", result.Explanation);
    }

    [Fact]
    public void Match_InvalidPathFallback_DoesNotThrow()
    {
        var matcher = new MediaMatcher();
        var search = new PendingSearchRecord
        {
            ContentType = VideoContentType.Movie,
            Title = "Dune",
            ProductionYear = 2021,
            MediaPath = "bad\0path",
        };
        var catalog = new BazarrCatalogSnapshot
        {
            Movies = new[]
            {
                new BazarrMovieRecord { Title = "Dune", Year = "2021", RadarrId = 11, Path = "/library/movies/dune-one.mkv" },
                new BazarrMovieRecord { Title = "Dune", Year = "2021", RadarrId = 22, Path = "/library/movies/dune-two.mkv" },
            },
        };

        var result = matcher.Match(search, catalog);

        Assert.Null(result);
    }
}
