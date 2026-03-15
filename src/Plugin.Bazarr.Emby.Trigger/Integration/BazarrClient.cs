using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Models;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Integration;

public class BazarrClient
{
    private readonly HttpClient httpClient;

    public BazarrClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<(bool Success, string Message, string Endpoint)> TestConnectionAsync(PluginOptions configuration, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpointSummary(configuration);
        try
        {
            using (var request = CreateRequest(HttpMethod.Get, configuration, "/api/system/ping"))
            using (var response = await SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Bazarr responded successfully.", endpoint);
                }

                return (false, $"Bazarr returned {(int)response.StatusCode} {response.ReasonPhrase}.", endpoint);
            }
        }
        catch (BazarrRequestException ex) when (ex.Kind == BazarrRequestFailureKind.Connection)
        {
            return (false, ex.Message, endpoint);
        }
    }

    public async Task<BazarrCatalogSnapshot> GetCatalogSnapshotAsync(PluginOptions configuration, int? seriesId, CancellationToken cancellationToken)
    {
        return await GetCatalogSnapshotAsync(configuration, null, seriesId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BazarrCatalogSnapshot> GetCatalogSnapshotAsync(PluginOptions configuration, PendingSearchRecord? search, CancellationToken cancellationToken)
    {
        return await GetCatalogSnapshotAsync(configuration, search, MatchingHelpers.TryParseInt(search?.SonarrSeriesId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<BazarrCatalogSnapshot> GetCatalogSnapshotAsync(PluginOptions configuration, PendingSearchRecord? search, int? requestedSeriesId, CancellationToken cancellationToken)
    {
        var moviesTask = GetJsonAsync<BazarrMoviesResponse>(configuration, "/api/movies?length=-1", cancellationToken);
        var seriesTask = GetJsonAsync<BazarrSeriesResponse>(configuration, "/api/series?length=-1", cancellationToken);
        await Task.WhenAll(moviesTask, seriesTask).ConfigureAwait(false);
        var movies = moviesTask.Result;
        var series = seriesTask.Result;

        var resolvedSeriesId = requestedSeriesId ?? ResolveSeriesId(search, series.Data);
        var episodesTask = resolvedSeriesId.HasValue
            ? GetJsonAsync<BazarrEpisodesResponse>(configuration, $"/api/episodes?seriesid[]={resolvedSeriesId.Value}", cancellationToken)
            : Task.FromResult(new BazarrEpisodesResponse());

        await episodesTask.ConfigureAwait(false);

        return new BazarrCatalogSnapshot
        {
            Movies = movies.Data,
            Series = series.Data,
            Episodes = episodesTask.Result.Data,
        };
    }

    public async Task TriggerSearchAsync(PluginOptions configuration, PendingSearchRecord search, MatchResult match, CancellationToken cancellationToken)
    {
        if (match.TriggerKind == BazarrTriggerKind.MovieSearchMissing)
        {
            var path = $"/api/movies?radarrid={match.MovieId}&action=search-missing";
            using (var request = CreateRequest(new HttpMethod("PATCH"), configuration, path, search))
            using (var response = await SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                EnsureSuccess(response);
            }

            return;
        }

        var manualResults = await GetJsonAsync<BazarrManualSearchResponse>(configuration, $"/api/providers/episodes?episodeid={match.EpisodeId}", cancellationToken).ConfigureAwait(false);
        var chosen = manualResults.Data
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.OriginalScore)
            .FirstOrDefault();

        if (chosen == null)
        {
            throw new InvalidOperationException("Bazarr returned no manual subtitle candidates for the episode search.");
        }

        var form = new Dictionary<string, string>
        {
            ["seriesid"] = match.SeriesId.ToString(),
            ["episodeid"] = match.EpisodeId.ToString(),
            ["hi"] = "False",
            ["forced"] = search.ForcedOnly ? "True" : chosen.Forced,
            ["original_format"] = chosen.OriginalFormat,
            ["provider"] = chosen.Provider,
            ["subtitle"] = chosen.Subtitle,
        };

        using (var request = CreateRequest(HttpMethod.Post, configuration, "/api/providers/episodes", search))
        {
            request.Content = new FormUrlEncodedContent(form);
            using (var response = await SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                EnsureSuccess(response);
            }
        }
    }

    public static string BuildEndpointSummary(PluginOptions configuration)
    {
        var builder = CreateBaseUriBuilder(configuration);
        builder.Path = NormalizeBaseUrl(configuration.BazarrBaseUrl) + "/api";
        return builder.Uri.ToString().TrimEnd('/');
    }

    private async Task<T> GetJsonAsync<T>(PluginOptions configuration, string relativePath, CancellationToken cancellationToken) where T : class
    {
        using (var request = CreateRequest(HttpMethod.Get, configuration, relativePath))
        using (var response = await SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
            EnsureSuccess(response);
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var copy = new MemoryStream())
            {
                await stream.CopyToAsync(copy).ConfigureAwait(false);
                copy.Position = 0;
                using (var reader = new StreamReader(copy, System.Text.Encoding.UTF8, true, 1024, true))
                {
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return JsonFileStore.ReadString<T>(json);
                }
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BazarrRequestException(BazarrRequestFailureKind.Connection, "Timed out while connecting to Bazarr.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BazarrRequestException(BazarrRequestFailureKind.Connection, "Unable to connect to Bazarr.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? "Unknown response"
            : response.ReasonPhrase;
        throw new BazarrRequestException(
            BazarrRequestFailureKind.Api,
            $"Bazarr returned {(int)response.StatusCode} {reason}.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, PluginOptions configuration, string relativePath, PendingSearchRecord? search = null)
    {
        var uri = BuildUri(configuration, relativePath);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-API-KEY", configuration.BazarrApiKey ?? string.Empty);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var header in configuration.ParseCustomHeaders())
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (search != null)
        {
            request.Headers.TryAddWithoutValidation("X-Emby-Requested-Language", search.RequestedLanguage ?? string.Empty);
            request.Headers.TryAddWithoutValidation("X-Emby-Forced-Only", search.ForcedOnly ? "true" : "false");
        }

        return request;
    }

    private Uri BuildUri(PluginOptions configuration, string relativePath)
    {
        var builder = CreateBaseUriBuilder(configuration);
        var queryIndex = relativePath.IndexOf('?');
        var path = queryIndex >= 0 ? relativePath.Substring(0, queryIndex) : relativePath;
        builder.Path = NormalizeBaseUrl(configuration.BazarrBaseUrl).TrimEnd('/') + path;
        builder.Query = queryIndex >= 0 ? relativePath.Substring(queryIndex + 1) : string.Empty;
        return builder.Uri;
    }

    private static UriBuilder CreateBaseUriBuilder(PluginOptions configuration)
    {
        var trimmedHost = (configuration.BazarrHost ?? string.Empty).Trim();
        if (Uri.TryCreate(trimmedHost, UriKind.Absolute, out var absoluteUri))
        {
            return new UriBuilder(absoluteUri.Scheme, absoluteUri.Host, configuration.BazarrPort);
        }

        return new UriBuilder(Uri.UriSchemeHttp, trimmedHost, configuration.BazarrPort);
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmed = (baseUrl ?? string.Empty).Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.TrimEnd('/');
    }

    private static int? ResolveSeriesId(PendingSearchRecord? search, IReadOnlyList<BazarrSeriesRecord> series)
    {
        if (search == null || search.ContentType != MediaBrowser.Controller.Providers.VideoContentType.Episode)
        {
            return null;
        }

        var titleCandidates = series
            .Where(item => MatchingHelpers.TitleAndYearMatch(item.Title, search.SeriesName, item.Year, search.ProductionYear))
            .ToList();

        if (int.TryParse(search.TvdbId, out var tvdbId))
        {
            var tvdbSeries = titleCandidates.FirstOrDefault(item => item.TvdbId == tvdbId);
            if (tvdbSeries != null)
            {
                return tvdbSeries.SonarrSeriesId;
            }
        }

        if (!string.IsNullOrWhiteSpace(search.ImdbId))
        {
            var imdbSeries = titleCandidates.FirstOrDefault(item => string.Equals(item.ImdbId, search.ImdbId, StringComparison.OrdinalIgnoreCase));
            if (imdbSeries != null)
            {
                return imdbSeries.SonarrSeriesId;
            }
        }

        return titleCandidates.Count == 1
            ? titleCandidates[0].SonarrSeriesId
            : null;
    }

}
