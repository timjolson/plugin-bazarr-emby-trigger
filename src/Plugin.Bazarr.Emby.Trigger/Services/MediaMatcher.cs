using System;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Providers;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class MediaMatcher
{
    public MatchResult? Match(PendingSearchRecord search, BazarrCatalogSnapshot catalog)
    {
        if (search.ContentType == VideoContentType.Movie)
        {
            return MatchMovie(search, catalog);
        }

        return MatchEpisode(search, catalog);
    }

    private MatchResult? MatchMovie(PendingSearchRecord search, BazarrCatalogSnapshot catalog)
    {
        var candidates = catalog.Movies.AsEnumerable();

        if (int.TryParse(search.RadarrId, out var radarrId))
        {
            var direct = candidates.FirstOrDefault(item => item.RadarrId == radarrId && MatchingHelpers.TitleAndYearMatch(item.Title, search.Title, item.Year, search.ProductionYear));
            if (direct != null)
            {
                return new MatchResult { ContentType = search.ContentType, TriggerKind = BazarrTriggerKind.MovieSearchMissing, MovieId = direct.RadarrId, Explanation = "Matched by Radarr ID." };
            }
        }

        var titleCandidates = candidates
            .Where(item => MatchingHelpers.TitleAndYearMatch(item.Title, search.Title, item.Year, search.ProductionYear))
            .ToList();

        if (!string.IsNullOrWhiteSpace(search.ImdbId))
        {
            var imdb = titleCandidates.FirstOrDefault(item => string.Equals(item.ImdbId, search.ImdbId, StringComparison.OrdinalIgnoreCase));
            if (imdb != null)
            {
                return new MatchResult { ContentType = search.ContentType, TriggerKind = BazarrTriggerKind.MovieSearchMissing, MovieId = imdb.RadarrId, Explanation = "Matched by IMDb ID fallback." };
            }
        }

        if (int.TryParse(search.TmdbId, out var tmdbId))
        {
            var tmdb = titleCandidates.FirstOrDefault(item => item.TmdbId == tmdbId);
            if (tmdb != null)
            {
                return new MatchResult { ContentType = search.ContentType, TriggerKind = BazarrTriggerKind.MovieSearchMissing, MovieId = tmdb.RadarrId, Explanation = "Matched by TMDB ID fallback." };
            }
        }

        var path = candidates.FirstOrDefault(item => PathsEquivalent(item.Path, search.MediaPath) && MatchingHelpers.TitleAndYearMatch(item.Title, search.Title, item.Year, search.ProductionYear));
        if (path != null)
        {
            return new MatchResult { ContentType = search.ContentType, TriggerKind = BazarrTriggerKind.MovieSearchMissing, MovieId = path.RadarrId, Explanation = "Matched by cached file path fallback." };
        }

        if (titleCandidates.Count == 1)
        {
            return new MatchResult { ContentType = search.ContentType, TriggerKind = BazarrTriggerKind.MovieSearchMissing, MovieId = titleCandidates[0].RadarrId, Explanation = "Matched by cached title/year fallback." };
        }

        return null;
    }

    private MatchResult? MatchEpisode(PendingSearchRecord search, BazarrCatalogSnapshot catalog)
    {
        var seriesCandidates = catalog.Series.AsEnumerable();
        var episodeCandidates = catalog.Episodes.AsEnumerable();

        if (int.TryParse(search.SonarrEpisodeId, out var episodeId))
        {
            var exactEpisode = episodeCandidates.FirstOrDefault(item => item.SonarrEpisodeId == episodeId && EpisodeShapeMatches(item, search));
            if (exactEpisode != null)
            {
                return new MatchResult
                {
                    ContentType = search.ContentType,
                    TriggerKind = BazarrTriggerKind.EpisodeManualTopResult,
                    EpisodeId = exactEpisode.SonarrEpisodeId,
                    SeriesId = exactEpisode.SonarrSeriesId,
                    Explanation = "Matched by Sonarr episode ID.",
                };
            }
        }

        if (int.TryParse(search.SonarrSeriesId, out var seriesId))
        {
            var directSeries = seriesCandidates.FirstOrDefault(item => item.SonarrSeriesId == seriesId && MatchingHelpers.TitleAndYearMatch(item.Title, search.SeriesName, item.Year, search.ProductionYear));
            if (directSeries != null)
            {
                var episode = episodeCandidates.FirstOrDefault(item => item.SonarrSeriesId == directSeries.SonarrSeriesId && EpisodeShapeMatches(item, search));
                if (episode != null)
                {
                    return new MatchResult
                    {
                        ContentType = search.ContentType,
                        TriggerKind = BazarrTriggerKind.EpisodeManualTopResult,
                        EpisodeId = episode.SonarrEpisodeId,
                        SeriesId = episode.SonarrSeriesId,
                        Explanation = "Matched by Sonarr series ID plus episode numbers.",
                    };
                }
            }
        }

        if (int.TryParse(search.TvdbId, out var tvdbId))
        {
            var tvdbSeries = seriesCandidates.FirstOrDefault(item => item.TvdbId == tvdbId && MatchingHelpers.TitleAndYearMatch(item.Title, search.SeriesName, item.Year, search.ProductionYear));
            if (tvdbSeries != null)
            {
                var episode = episodeCandidates.FirstOrDefault(item => item.SonarrSeriesId == tvdbSeries.SonarrSeriesId && EpisodeShapeMatches(item, search));
                if (episode != null)
                {
                    return new MatchResult
                    {
                        ContentType = search.ContentType,
                        TriggerKind = BazarrTriggerKind.EpisodeManualTopResult,
                        EpisodeId = episode.SonarrEpisodeId,
                        SeriesId = episode.SonarrSeriesId,
                        Explanation = "Matched by TVDB ID plus episode numbers.",
                    };
                }
            }
        }

        var titleSeriesCandidates = seriesCandidates
            .Where(item => MatchingHelpers.TitleAndYearMatch(item.Title, search.SeriesName, item.Year, search.ProductionYear))
            .ToList();
        if (titleSeriesCandidates.Count == 1)
        {
            var episode = episodeCandidates.FirstOrDefault(item => item.SonarrSeriesId == titleSeriesCandidates[0].SonarrSeriesId && EpisodeShapeMatches(item, search));
            if (episode != null)
            {
                return new MatchResult
                {
                    ContentType = search.ContentType,
                    TriggerKind = BazarrTriggerKind.EpisodeManualTopResult,
                    EpisodeId = episode.SonarrEpisodeId,
                    SeriesId = episode.SonarrSeriesId,
                    Explanation = "Matched by cached series title/year fallback plus episode numbers.",
                };
            }
        }

        var byPath = episodeCandidates.FirstOrDefault(item => PathsEquivalent(item.Path, search.MediaPath) && EpisodeShapeMatches(item, search));
        if (byPath != null)
        {
            return new MatchResult
            {
                ContentType = search.ContentType,
                TriggerKind = BazarrTriggerKind.EpisodeManualTopResult,
                EpisodeId = byPath.SonarrEpisodeId,
                SeriesId = byPath.SonarrSeriesId,
                Explanation = "Matched by cached file path fallback.",
            };
        }

        return null;
    }

    private static bool EpisodeShapeMatches(BazarrEpisodeRecord item, PendingSearchRecord search)
        => item.Season == search.SeasonNumber.GetValueOrDefault() && item.Episode == search.EpisodeNumber.GetValueOrDefault();

    private static bool PathsEquivalent(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
