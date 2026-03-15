using System.Collections.Generic;
using System.Runtime.Serialization;
using MediaBrowser.Controller.Providers;

namespace Plugin.Bazarr.Emby.Trigger.Models;

[DataContract]
public class BazarrMoviesResponse
{
    [DataMember(Name = "data")] public List<BazarrMovieRecord> Data { get; set; } = new();
}

[DataContract]
public class BazarrSeriesResponse
{
    [DataMember(Name = "data")] public List<BazarrSeriesRecord> Data { get; set; } = new();
}

[DataContract]
public class BazarrEpisodesResponse
{
    [DataMember(Name = "data")] public List<BazarrEpisodeRecord> Data { get; set; } = new();
}

[DataContract]
public class BazarrManualSearchResponse
{
    [DataMember(Name = "data")] public List<BazarrManualSubtitleCandidate> Data { get; set; } = new();
}

[DataContract]
public class BazarrMovieRecord
{
    [DataMember(Name = "title")] public string Title { get; set; } = string.Empty;
    [DataMember(Name = "year")] public string Year { get; set; } = string.Empty;
    [DataMember(Name = "path")] public string Path { get; set; } = string.Empty;
    [DataMember(Name = "radarrId")] public int RadarrId { get; set; }
    [DataMember(Name = "imdbId")] public string? ImdbId { get; set; }
    [DataMember(Name = "tmdbId")] public int? TmdbId { get; set; }
}

[DataContract]
public class BazarrSeriesRecord
{
    [DataMember(Name = "title")] public string Title { get; set; } = string.Empty;
    [DataMember(Name = "year")] public string Year { get; set; } = string.Empty;
    [DataMember(Name = "path")] public string Path { get; set; } = string.Empty;
    [DataMember(Name = "sonarrSeriesId")] public int SonarrSeriesId { get; set; }
    [DataMember(Name = "tvdbId")] public int? TvdbId { get; set; }
    [DataMember(Name = "imdbId")] public string? ImdbId { get; set; }
}

[DataContract]
public class BazarrEpisodeRecord
{
    [DataMember(Name = "title")] public string Title { get; set; } = string.Empty;
    [DataMember(Name = "path")] public string Path { get; set; } = string.Empty;
    [DataMember(Name = "season")] public int Season { get; set; }
    [DataMember(Name = "episode")] public int Episode { get; set; }
    [DataMember(Name = "sonarrEpisodeId")] public int SonarrEpisodeId { get; set; }
    [DataMember(Name = "sonarrSeriesId")] public int SonarrSeriesId { get; set; }
}

[DataContract]
public class BazarrManualSubtitleCandidate
{
    [DataMember(Name = "provider")] public string Provider { get; set; } = string.Empty;
    [DataMember(Name = "subtitle")] public string Subtitle { get; set; } = string.Empty;
    [DataMember(Name = "score")] public int Score { get; set; }
    [DataMember(Name = "orig_score")] public int OriginalScore { get; set; }
    [DataMember(Name = "forced")] public string Forced { get; set; } = "False";
    [DataMember(Name = "hearing_impaired")] public string HearingImpaired { get; set; } = "False";
    [DataMember(Name = "original_format")] public string OriginalFormat { get; set; } = "False";
}

public class BazarrCatalogSnapshot
{
    public IReadOnlyList<BazarrMovieRecord> Movies { get; set; } = new List<BazarrMovieRecord>();

    public IReadOnlyList<BazarrSeriesRecord> Series { get; set; } = new List<BazarrSeriesRecord>();

    public IReadOnlyList<BazarrEpisodeRecord> Episodes { get; set; } = new List<BazarrEpisodeRecord>();
}

public class MatchResult
{
    public VideoContentType ContentType { get; set; }

    public BazarrTriggerKind TriggerKind { get; set; }

    public int MovieId { get; set; }

    public int SeriesId { get; set; }

    public int EpisodeId { get; set; }

    public string Explanation { get; set; } = string.Empty;
}

public enum BazarrTriggerKind
{
    MovieSearchMissing,
    EpisodeManualTopResult,
}
