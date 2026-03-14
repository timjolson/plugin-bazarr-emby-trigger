using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using MediaBrowser.Controller.Providers;

namespace Plugin.Bazarr.Emby.Trigger.Models;

[DataContract]
public class PendingSearchRecord
{
    [DataMember(Order = 1)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public VideoContentType ContentType { get; set; }
    [DataMember(Order = 3)] public string MediaPath { get; set; } = string.Empty;
    [DataMember(Order = 4)] public string Title { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string SeriesName { get; set; } = string.Empty;
    [DataMember(Order = 6)] public int? ProductionYear { get; set; }
    [DataMember(Order = 7)] public int? SeasonNumber { get; set; }
    [DataMember(Order = 8)] public int? EpisodeNumber { get; set; }
    [DataMember(Order = 9)] public string RequestedLanguage { get; set; } = string.Empty;
    [DataMember(Order = 10)] public bool ForcedOnly { get; set; }
    [DataMember(Order = 11)] public string? TmdbId { get; set; }
    [DataMember(Order = 12)] public string? TvdbId { get; set; }
    [DataMember(Order = 13)] public string? RadarrId { get; set; }
    [DataMember(Order = 14)] public string? SonarrSeriesId { get; set; }
    [DataMember(Order = 15)] public string? SonarrEpisodeId { get; set; }
    [DataMember(Order = 16)] public string? ImdbId { get; set; }
    [DataMember(Order = 17)] public PendingSearchState State { get; set; } = PendingSearchState.Queued;
    [DataMember(Order = 18)] public DateTime EnqueuedUtc { get; set; } = DateTime.UtcNow;
    [DataMember(Order = 19)] public DateTime? TriggeredUtc { get; set; }
    [DataMember(Order = 20)] public DateTime? LastAttemptUtc { get; set; }
    [DataMember(Order = 21)] public string? LastError { get; set; }
    [DataMember(Order = 22)] public int RetryCount { get; set; }
    [DataMember(Order = 23)] public string? NotificationUserId { get; set; }
    [DataMember(Order = 24)] public int? BazarrSeriesId { get; set; }
    [DataMember(Order = 25)] public int? BazarrEpisodeId { get; set; }
    [DataMember(Order = 26)] public int? BazarrMovieId { get; set; }
    [DataMember(Order = 27)] public List<SubtitleFileFingerprint> Snapshot { get; set; } = new();

    public string GetDisplayName()
    {
        if (ContentType == VideoContentType.Movie)
        {
            return ProductionYear.HasValue ? $"{Title} ({ProductionYear.Value})" : Title;
        }

        return $"{SeriesName} - S{SeasonNumber.GetValueOrDefault():00}E{EpisodeNumber.GetValueOrDefault():00}";
    }
}

public enum PendingSearchState
{
    Queued,
    Triggered,
    Completed,
    TimedOut,
    Failed,
}
