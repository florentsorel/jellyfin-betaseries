using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetaSeries.Api;

/// <summary>
/// Client for interacting with the BetaSeries REST API.
/// </summary>
public class BetaSeriesClient
{
    private const string BaseApiUrl = "https://api.betaseries.com";
    private const string ApiVersion = "3.0";
    private const int UnknownIdentifierErrorCode = 4001;

    private readonly HttpClient _httpClient;
    private readonly ILogger<BetaSeriesClient> _logger;

    private readonly ConcurrentDictionary<string, int> _movieCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _showCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _episodeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="BetaSeriesClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance.</param>
    /// <param name="logger">The logger instance.</param>
    public BetaSeriesClient(HttpClient httpClient, ILogger<BetaSeriesClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Finds a BetaSeries movie identifier given TMDB or IMDb identifiers.
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="tmdbId">The TMDB movie identifier.</param>
    /// <param name="imdbId">The IMDb movie identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The BetaSeries movie ID if found; otherwise, null.</returns>
    public async Task<int?> FindMovieIdAsync(
        string clientId,
        string token,
        string? tmdbId,
        string? imdbId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(tmdbId) && _movieCache.TryGetValue($"tmdb_{tmdbId}", out var cachedId))
        {
            return cachedId;
        }

        if (!string.IsNullOrWhiteSpace(imdbId) && _movieCache.TryGetValue($"imdb_{imdbId}", out cachedId))
        {
            return cachedId;
        }

        // 1. Try lookup by TMDB ID
        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            var movie = await GetMovieByParamAsync(clientId, token, "tmdb_id", tmdbId, cancellationToken).ConfigureAwait(false);
            if (movie.HasValue)
            {
                CacheMovie(movie.Value, tmdbId, imdbId);
                return movie.Value;
            }
        }

        // 2. Try lookup by IMDb ID
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            var movie = await GetMovieByParamAsync(clientId, token, "imdb_id", imdbId, cancellationToken).ConfigureAwait(false);
            if (movie.HasValue)
            {
                CacheMovie(movie.Value, tmdbId, imdbId);
                return movie.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Marks a movie as watched on BetaSeries.
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="movieId">The BetaSeries movie ID.</param>
    /// <param name="watchedAt">Optional date and time when the movie was watched.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the movie was marked as watched successfully; otherwise, false.</returns>
    public async Task<bool> ScrobbleMovieAsync(
        string clientId,
        string token,
        int movieId,
        DateTime? watchedAt = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("id", movieId.ToString(CultureInfo.InvariantCulture)),
            new("state", "1"), // 1 = watched
        };

        if (watchedAt.HasValue)
        {
            var utcDate = watchedAt.Value.Kind == DateTimeKind.Utc ? watchedAt.Value : watchedAt.Value.ToUniversalTime();
            parameters.Add(new KeyValuePair<string, string>("date", utcDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        using var request = CreateRequest(HttpMethod.Post, "/movies/movie", clientId, token, parameters);
        using var doc = await SendRequestAsync(request, cancellationToken, isMutation: true, isWatchAction: true).ConfigureAwait(false);
        return doc != null;
    }

    /// <summary>
    /// Removes a movie from the member's account (unmark as watched).
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="movieId">The BetaSeries movie ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the movie was removed successfully; otherwise, false.</returns>
    public async Task<bool> UnmarkMovieAsync(
        string clientId,
        string token,
        int movieId,
        CancellationToken cancellationToken = default)
    {
        var query = new[]
        {
            new KeyValuePair<string, string>("id", movieId.ToString(CultureInfo.InvariantCulture)),
        };

        using var request = CreateRequest(HttpMethod.Delete, "/movies/movie", clientId, token, queryParams: query);
        using var doc = await SendRequestAsync(request, cancellationToken, isMutation: true, isWatchAction: false).ConfigureAwait(false);
        return doc != null;
    }

    /// <summary>
    /// Finds a BetaSeries show identifier given TVDB, IMDb, or TMDB identifiers.
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="tvdbId">The TVDB show identifier.</param>
    /// <param name="imdbId">The IMDb show identifier.</param>
    /// <param name="title">The title of the series for fallback search.</param>
    /// <param name="tmdbId">The TMDB show identifier for fallback matching.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The BetaSeries show ID if found; otherwise, null.</returns>
    public async Task<int?> FindShowIdAsync(
        string clientId,
        string token,
        string? tvdbId,
        string? imdbId,
        string? title = null,
        string? tmdbId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(tvdbId) && _showCache.TryGetValue($"tvdb_{tvdbId}", out var cachedId))
        {
            return cachedId;
        }

        if (!string.IsNullOrWhiteSpace(imdbId) && _showCache.TryGetValue($"imdb_{imdbId}", out cachedId))
        {
            return cachedId;
        }

        if (!string.IsNullOrWhiteSpace(tmdbId) && _showCache.TryGetValue($"tmdb_{tmdbId}", out cachedId))
        {
            return cachedId;
        }

        // 1. Try lookup by TVDB ID
        if (!string.IsNullOrWhiteSpace(tvdbId))
        {
            var show = await GetShowByParamAsync(clientId, token, "thetvdb_id", tvdbId, cancellationToken).ConfigureAwait(false);
            if (show.HasValue)
            {
                CacheShow(show.Value, tvdbId, imdbId, tmdbId);
                return show.Value;
            }
        }

        // 2. Try lookup by IMDb ID
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            var show = await GetShowByParamAsync(clientId, token, "imdb_id", imdbId, cancellationToken).ConfigureAwait(false);
            if (show.HasValue)
            {
                CacheShow(show.Value, tvdbId, imdbId, tmdbId);
                return show.Value;
            }
        }

        // 3. Fallback search by title
        if (!string.IsNullOrWhiteSpace(title))
        {
            var show = await SearchShowByTitleAsync(clientId, token, title, tmdbId, cancellationToken).ConfigureAwait(false);
            if (show.HasValue)
            {
                CacheShow(show.Value, tvdbId, imdbId, tmdbId);
                return show.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a BetaSeries episode identifier.
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="showId">The BetaSeries show ID.</param>
    /// <param name="seasonNumber">The season number.</param>
    /// <param name="episodeNumber">The episode number.</param>
    /// <param name="episodeTvdbId">Optional TVDB episode identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The BetaSeries episode ID if found; otherwise, null.</returns>
    public async Task<int?> FindEpisodeIdAsync(
        string clientId,
        string token,
        int showId,
        int seasonNumber,
        int episodeNumber,
        string? episodeTvdbId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(episodeTvdbId) && _episodeCache.TryGetValue($"tvdb_{episodeTvdbId}", out var cachedId))
        {
            return cachedId;
        }

        var showSeasonEpKey = $"s{showId}_{seasonNumber}_{episodeNumber}";
        if (_episodeCache.TryGetValue(showSeasonEpKey, out cachedId))
        {
            return cachedId;
        }

        // 1. Try lookup by episode TVDB ID
        if (!string.IsNullOrWhiteSpace(episodeTvdbId))
        {
            var epId = await GetEpisodeByTvdbIdAsync(clientId, token, episodeTvdbId, cancellationToken).ConfigureAwait(false);
            if (epId.HasValue)
            {
                CacheEpisode(epId.Value, showId, seasonNumber, episodeNumber, episodeTvdbId);
                return epId.Value;
            }
        }

        // 2. Lookup via show episodes list
        if (showId > 0)
        {
            var query = new[]
            {
                new KeyValuePair<string, string>("id", showId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("season", seasonNumber.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("episode", episodeNumber.ToString(CultureInfo.InvariantCulture)),
            };

            using var request = CreateRequest(HttpMethod.Get, "/shows/episodes", clientId, token, queryParams: query);
            using var doc = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
            if (doc != null && doc.RootElement.TryGetProperty("episodes", out var episodesElement) && episodesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var epItem in episodesElement.EnumerateArray())
                {
                    if (epItem.TryGetProperty("id", out var idElement))
                    {
                        var foundId = idElement.GetInt32();
                        CacheEpisode(foundId, showId, seasonNumber, episodeNumber, episodeTvdbId);
                        return foundId;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Marks an episode as watched on BetaSeries.
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="episodeId">The BetaSeries episode ID.</param>
    /// <param name="watchedAt">Optional date and time when the episode was watched.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the episode was marked as watched successfully; otherwise, false.</returns>
    public async Task<bool> ScrobbleEpisodeAsync(
        string clientId,
        string token,
        int episodeId,
        DateTime? watchedAt = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("id", episodeId.ToString(CultureInfo.InvariantCulture)),
            new("bulk", "false"), // bulk=false is critical to not mark prior episodes
        };

        if (watchedAt.HasValue)
        {
            var utcDate = watchedAt.Value.Kind == DateTimeKind.Utc ? watchedAt.Value : watchedAt.Value.ToUniversalTime();
            parameters.Add(new KeyValuePair<string, string>("date", utcDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        using var request = CreateRequest(HttpMethod.Post, "/episodes/watched", clientId, token, parameters);
        using var doc = await SendRequestAsync(request, cancellationToken, isMutation: true, isWatchAction: true).ConfigureAwait(false);
        return doc != null;
    }

    /// <summary>
    /// Removes an episode watched mark on BetaSeries.
    /// </summary>
    /// <param name="clientId">The BetaSeries application client ID.</param>
    /// <param name="token">The BetaSeries member token.</param>
    /// <param name="episodeId">The BetaSeries episode ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the episode mark was removed successfully; otherwise, false.</returns>
    public async Task<bool> UnmarkEpisodeAsync(
        string clientId,
        string token,
        int episodeId,
        CancellationToken cancellationToken = default)
    {
        var query = new[]
        {
            new KeyValuePair<string, string>("id", episodeId.ToString(CultureInfo.InvariantCulture)),
        };

        using var request = CreateRequest(HttpMethod.Delete, "/episodes/watched", clientId, token, queryParams: query);
        using var doc = await SendRequestAsync(request, cancellationToken, isMutation: true, isWatchAction: false).ConfigureAwait(false);
        return doc != null;
    }

    private void CacheMovie(int movieId, string? tmdbId, string? imdbId)
    {
        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            _movieCache[$"tmdb_{tmdbId}"] = movieId;
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            _movieCache[$"imdb_{imdbId}"] = movieId;
        }
    }

    private void CacheShow(int showId, string? tvdbId, string? imdbId, string? tmdbId)
    {
        if (!string.IsNullOrWhiteSpace(tvdbId))
        {
            _showCache[$"tvdb_{tvdbId}"] = showId;
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            _showCache[$"imdb_{imdbId}"] = showId;
        }

        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            _showCache[$"tmdb_{tmdbId}"] = showId;
        }
    }

    private void CacheEpisode(int episodeId, int showId, int seasonNumber, int episodeNumber, string? episodeTvdbId)
    {
        if (!string.IsNullOrWhiteSpace(episodeTvdbId))
        {
            _episodeCache[$"tvdb_{episodeTvdbId}"] = episodeId;
        }

        if (showId > 0)
        {
            _episodeCache[$"s{showId}_{seasonNumber}_{episodeNumber}"] = episodeId;
        }
    }

    private async Task<int?> GetMovieByParamAsync(
        string clientId,
        string token,
        string paramName,
        string paramValue,
        CancellationToken cancellationToken)
    {
        var query = new[] { new KeyValuePair<string, string>(paramName, paramValue) };
        using var request = CreateRequest(HttpMethod.Get, "/movies/movie", clientId, token, queryParams: query);
        using var doc = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("movie", out var movieElement) && movieElement.TryGetProperty("id", out var idElement))
        {
            return idElement.GetInt32();
        }

        return null;
    }

    private async Task<int?> GetShowByParamAsync(
        string clientId,
        string token,
        string paramName,
        string paramValue,
        CancellationToken cancellationToken)
    {
        var query = new[] { new KeyValuePair<string, string>(paramName, paramValue) };
        using var request = CreateRequest(HttpMethod.Get, "/shows/display", clientId, token, queryParams: query);
        using var doc = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("show", out var showElement) && showElement.TryGetProperty("id", out var idElement))
        {
            return idElement.GetInt32();
        }

        return null;
    }

    private async Task<int?> SearchShowByTitleAsync(
        string clientId,
        string token,
        string title,
        string? tmdbId,
        CancellationToken cancellationToken)
    {
        var query = new[] { new KeyValuePair<string, string>("title", title) };
        using var request = CreateRequest(HttpMethod.Get, "/shows/search", clientId, token, queryParams: query);
        using var doc = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("shows", out var showsElement) && showsElement.ValueKind == JsonValueKind.Array)
        {
            int? tmdbInt = null;
            if (!string.IsNullOrWhiteSpace(tmdbId) && int.TryParse(tmdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTmdb))
            {
                tmdbInt = parsedTmdb;
            }

            int? firstShowId = null;
            foreach (var item in showsElement.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetInt32();
                    firstShowId ??= id;

                    if (tmdbInt.HasValue && item.TryGetProperty("themoviedb_id", out var showTmdbProp))
                    {
                        if (showTmdbProp.ValueKind == JsonValueKind.Number && showTmdbProp.GetInt32() == tmdbInt.Value)
                        {
                            return id;
                        }
                    }
                }
            }

            // Fallback to the first match if no TMDB match
            return firstShowId;
        }

        return null;
    }

    private async Task<int?> GetEpisodeByTvdbIdAsync(
        string clientId,
        string token,
        string episodeTvdbId,
        CancellationToken cancellationToken)
    {
        var query = new[] { new KeyValuePair<string, string>("thetvdb_id", episodeTvdbId) };
        using var request = CreateRequest(HttpMethod.Get, "/episodes/display", clientId, token, queryParams: query);
        using var doc = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("episode", out var epElement) && epElement.TryGetProperty("id", out var idElement))
        {
            return idElement.GetInt32();
        }

        return null;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string clientId,
        string token,
        IEnumerable<KeyValuePair<string, string>>? formParams = null,
        IEnumerable<KeyValuePair<string, string>>? queryParams = null)
    {
        var uriBuilder = new UriBuilder(BaseApiUrl)
        {
            Path = path,
        };

        if (queryParams != null)
        {
            var queryList = new List<string>();
            foreach (var kvp in queryParams)
            {
                queryList.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
            }

            uriBuilder.Query = string.Join("&", queryList);
        }

        var request = new HttpRequestMessage(method, uriBuilder.Uri);
        request.Headers.TryAddWithoutValidation("X-BetaSeries-Key", clientId);
        request.Headers.TryAddWithoutValidation("X-BetaSeries-Version", ApiVersion);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (formParams != null)
        {
            request.Content = new FormUrlEncodedContent(formParams);
        }

        return request;
    }

    private async Task<JsonDocument?> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool isMutation = false,
        bool isWatchAction = true)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("BetaSeries API returned empty response for {Method} {Uri} with status {StatusCode}", request.Method, request.RequestUri, response.StatusCode);
                return null;
            }

            var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Array &&
                errorsElement.GetArrayLength() > 0)
            {
                var firstError = errorsElement[0];
                var code = firstError.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : 0;
                var text = (firstError.TryGetProperty("text", out var textElement) ? textElement.GetString() : null) ?? "Unknown error";

                if (isMutation)
                {
                    // If marking as watched and item is already watched: consider success
                    if (isWatchAction && (code == 0 && (text.Contains("déjà", StringComparison.OrdinalIgnoreCase) || text.Contains("already", StringComparison.OrdinalIgnoreCase))))
                    {
                        _logger.LogInformation("BetaSeries: Item was already marked as watched on BetaSeries ({Text})", text);
                        return doc;
                    }

                    // If unmarking and item is not marked as watched: consider success
                    if (!isWatchAction && (code == 2005 || text.Contains("n'a pas", StringComparison.OrdinalIgnoreCase) || text.Contains("not marked", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("BetaSeries: Item was already unmarked on BetaSeries ({Text})", text);
                        return doc;
                    }
                }

                doc.Dispose();

                if (code == UnknownIdentifierErrorCode)
                {
                    _logger.LogDebug("BetaSeries: Item not found (code {Code}) for {Uri}: {Text}", code, request.RequestUri, text);
                    return null;
                }

                _logger.LogWarning("BetaSeries API error {Code} for {Method} {Uri}: {Text}", code, request.Method, request.RequestUri, text);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                doc.Dispose();
                _logger.LogWarning("BetaSeries API returned unsuccessful HTTP status {StatusCode} for {Method} {Uri}", response.StatusCode, request.Method, request.RequestUri);
                return null;
            }

            return doc;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed for BetaSeries API: {Method} {Uri}", request.Method, request.RequestUri);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response from BetaSeries API: {Method} {Uri}", request.Method, request.RequestUri);
            return null;
        }
    }
}
