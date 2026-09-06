using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetaSeries.Api;
using Jellyfin.Plugin.BetaSeries.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.BetaSeries.Tests;

public class BetaSeriesClientTests
{
    private const string ClientId = "test-client-id";
    private const string Token = "test-token";

    [Fact]
    public async Task FindMovieIdAsync_WithValidTmdbId_ReturnsIdAndCachesResult()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?tmdb_id=550",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"title\":\"Fight Club\"}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindMovieIdAsync(ClientId, Token, tmdbId: "550", imdbId: null);

        Assert.Equal(141, result);
        Assert.Single(mockHandler.Requests);

        // Second call should hit the cache and not perform an HTTP request
        var cachedResult = await client.FindMovieIdAsync(ClientId, Token, tmdbId: "550", imdbId: null);
        Assert.Equal(141, cachedResult);
        Assert.Single(mockHandler.Requests);
    }

    [Fact]
    public async Task FindMovieIdAsync_WhenTmdbReturns4001_FallsBackToImdbId()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?tmdb_id=999999",
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":4001,\"text\":\"Le film n'existe pas.\"}]}");
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?imdb_id=tt0137523",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"title\":\"Fight Club\"}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindMovieIdAsync(ClientId, Token, tmdbId: "999999", imdbId: "tt0137523");

        Assert.Equal(141, result);
        Assert.Equal(2, mockHandler.Requests.Count);
    }

    [Fact]
    public async Task FindMovieIdAsync_WhenBothFail_ReturnsNull()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?tmdb_id=999999",
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":4001,\"text\":\"Le film n'existe pas.\"}]}");
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?imdb_id=tt9999999",
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":4001,\"text\":\"Le film n'existe pas.\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindMovieIdAsync(ClientId, Token, tmdbId: "999999", imdbId: "tt9999999");

        Assert.Null(result);
    }

    [Fact]
    public async Task ScrobbleMovieAsync_SendsCorrectHeadersAndParameters()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Post,
            "/movies/movie",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"user\":{\"status\":1}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var watchedAt = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var result = await client.ScrobbleMovieAsync(ClientId, Token, movieId: 141, watchedAt: watchedAt);

        Assert.True(result);
        Assert.Single(mockHandler.Requests);

        var request = mockHandler.Requests[0];
        Assert.Equal("test-client-id", request.Headers.GetValues("X-BetaSeries-Key").FirstOrDefault());
        Assert.Equal("3.0", request.Headers.GetValues("X-BetaSeries-Version").FirstOrDefault());
        Assert.Equal("Bearer test-token", request.Headers.GetValues("Authorization").FirstOrDefault());

        Assert.NotNull(request.Content);
        Assert.Contains("id=141", request.Content);
        Assert.Contains("state=1", request.Content);
        Assert.Contains("date=2026-09-06+12%3A00%3A00", request.Content);
    }

    [Fact]
    public async Task ScrobbleMovieAsync_WhenAlreadyWatched_ReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Post,
            "/movies/movie",
            HttpStatusCode.OK,
            "{\"errors\":[{\"code\":0,\"text\":\"L'utilisateur a déjà ce film dans son compte.\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.ScrobbleMovieAsync(ClientId, Token, movieId: 141);

        Assert.True(result);
    }

    [Fact]
    public async Task UnmarkMovieAsync_SendsDeleteAndReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Delete,
            "/movies/movie?id=141",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"user\":{\"in_account\":false}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.UnmarkMovieAsync(ClientId, Token, movieId: 141);

        Assert.True(result);
        Assert.Single(mockHandler.Requests);
        Assert.Equal(HttpMethod.Delete, mockHandler.Requests[0].Method);
    }

    [Fact]
    public async Task UnmarkMovieAsync_WhenAlreadyUnmarked_ReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Delete,
            "/movies/movie?id=141",
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":2005,\"text\":\"L'utilisateur n'a pas ce film dans son compte.\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.UnmarkMovieAsync(ClientId, Token, movieId: 141);

        Assert.True(result);
    }

    [Fact]
    public async Task FindShowIdAsync_WithTvdbId_ReturnsShowId()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/shows/display?thetvdb_id=81189",
            HttpStatusCode.OK,
            "{\"show\":{\"id\":481,\"title\":\"Breaking Bad\"}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindShowIdAsync(ClientId, Token, tvdbId: "81189", imdbId: null);

        Assert.Equal(481, result);
    }

    [Fact]
    public async Task FindShowIdAsync_WhenTvdbFails_FallsBackToImdbId()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/shows/display?thetvdb_id=99999",
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":4001,\"text\":\"Show not found\"}]}");
        mockHandler.Setup(
            HttpMethod.Get,
            "/shows/display?imdb_id=tt0903747",
            HttpStatusCode.OK,
            "{\"show\":{\"id\":481,\"title\":\"Breaking Bad\"}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindShowIdAsync(ClientId, Token, tvdbId: "99999", imdbId: "tt0903747");

        Assert.Equal(481, result);
    }

    [Fact]
    public async Task FindShowIdAsync_WhenTvdbAndImdbMissing_FallsBackToSearchByTitleMatchingTmdb()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/shows/search",
            HttpStatusCode.OK,
            "{\"shows\":[{\"id\":999,\"title\":\"Other Show\",\"themoviedb_id\":9999},{\"id\":481,\"title\":\"Breaking Bad\",\"themoviedb_id\":1396}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindShowIdAsync(
            ClientId,
            Token,
            tvdbId: null,
            imdbId: null,
            title: "Breaking Bad",
            tmdbId: "1396");

        Assert.Equal(481, result);
    }

    [Fact]
    public async Task FindEpisodeIdAsync_WithEpisodeTvdbId_ReturnsEpisodeId()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/episodes/display?thetvdb_id=349232",
            HttpStatusCode.OK,
            "{\"episode\":{\"id\":239475,\"title\":\"Pilot\",\"season\":1,\"episode\":1}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindEpisodeIdAsync(ClientId, Token, showId: 481, seasonNumber: 1, episodeNumber: 1, episodeTvdbId: "349232");

        Assert.Equal(239475, result);
    }

    [Fact]
    public async Task FindEpisodeIdAsync_ViaShowEpisodesList_ReturnsEpisodeId()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/shows/episodes?id=481&season=1&episode=1",
            HttpStatusCode.OK,
            "{\"episodes\":[{\"id\":239475,\"season\":1,\"episode\":1,\"title\":\"Pilot\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindEpisodeIdAsync(ClientId, Token, showId: 481, seasonNumber: 1, episodeNumber: 1, episodeTvdbId: null);

        Assert.Equal(239475, result);
    }

    [Fact]
    public async Task ScrobbleEpisodeAsync_SendsBulkFalseAndCorrectDate()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Post,
            "/episodes/watched",
            HttpStatusCode.OK,
            "{\"episode\":{\"id\":239475,\"user\":{\"seen\":true}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var watchedAt = new DateTime(2026, 9, 6, 14, 30, 0, DateTimeKind.Utc);
        var result = await client.ScrobbleEpisodeAsync(ClientId, Token, episodeId: 239475, watchedAt: watchedAt);

        Assert.True(result);
        Assert.Single(mockHandler.Requests);

        var request = mockHandler.Requests[0];
        Assert.NotNull(request.Content);

        Assert.Contains("id=239475", request.Content);
        Assert.Contains("bulk=false", request.Content); // CRITICAL check: bulk=false must always be sent!
        Assert.Contains("date=2026-09-06+14%3A30%3A00", request.Content);
    }

    [Fact]
    public async Task ScrobbleEpisodeAsync_WhenAlreadyWatched_ReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Post,
            "/episodes/watched",
            HttpStatusCode.OK,
            "{\"errors\":[{\"code\":0,\"text\":\"L'utilisateur a déjà marqué cet épisode comme vu.\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.ScrobbleEpisodeAsync(ClientId, Token, episodeId: 239475);

        Assert.True(result);
    }

    [Fact]
    public async Task UnmarkEpisodeAsync_SendsDeleteAndReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Delete,
            "/episodes/watched?id=239475",
            HttpStatusCode.OK,
            "{\"episode\":{\"id\":239475,\"user\":{\"seen\":false}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.UnmarkEpisodeAsync(ClientId, Token, episodeId: 239475);

        Assert.True(result);
        Assert.Single(mockHandler.Requests);
        Assert.Equal(HttpMethod.Delete, mockHandler.Requests[0].Method);
    }

    [Fact]
    public async Task UnmarkEpisodeAsync_WhenAlreadyUnmarked_ReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Delete,
            "/episodes/watched?id=239475",
            HttpStatusCode.BadRequest,
            "{\"errors\":[{\"code\":2005,\"text\":\"L'utilisateur n'a pas marqué cet épisode comme vu.\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.UnmarkEpisodeAsync(ClientId, Token, episodeId: 239475);

        Assert.True(result);
    }

    [Fact]
    public async Task ApiError_WithNon4001Code_ReturnsNullGracefully()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie",
            HttpStatusCode.Unauthorized,
            "{\"errors\":[{\"code\":2001,\"text\":\"Jeton utilisateur invalide.\"}]}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindMovieIdAsync(ClientId, Token, tmdbId: "550", imdbId: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task HttpServerError_ReturnsNullGracefullyWithoutThrowing()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie",
            HttpStatusCode.InternalServerError,
            "");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        var result = await client.FindMovieIdAsync(ClientId, Token, tmdbId: "550", imdbId: null);

        Assert.Null(result);
    }
}
