using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.BetaSeries.Api;
using Jellyfin.Plugin.BetaSeries.Configuration;
using Jellyfin.Plugin.BetaSeries.Tests.Mocks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.BetaSeries.Tests;

public class BetaSeriesManagerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ISessionManager> _sessionManagerMock = new();
    private readonly Mock<IUserDataManager> _userDataManagerMock = new();
    private readonly Mock<ILibraryManager> _libraryManagerMock = new();

    private User CreateTestUser() => new("testuser", "password", "salt") { Id = _userId };

    private void SetupPlugin(BetaSeriesProfile profile)
    {
        var config = new PluginConfiguration
        {
            ClientId = "test-client-id",
            Profiles = new[] { profile },
        };

        PluginTestHelper.CreateMockPlugin(config);
    }

    [Fact]
    public async Task StartAsync_And_StopAsync_ManageSubscriptionsProperly()
    {
        var mockHandler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlaybackStopped_WhenPlayedToCompletionIsFalse_DoesNotScrobble()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            ScrobblePlaybackStop = true,
            SyncMovies = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var movie = new Movie { Name = "Incomplete Movie" };
        movie.ProviderIds["Tmdb"] = "550";

        var args = new PlaybackStopEventArgs
        {
            Item = movie,
            PlayedToCompletion = false, // Not completed!
            Users = new List<User> { CreateTestUser() },
        };

        _sessionManagerMock.Raise(s => s.PlaybackStopped += null, args);

        // Give async Task.Run time to complete
        await Task.Delay(100);

        Assert.Empty(mockHandler.Requests);
    }

    [Fact]
    public async Task PlaybackStopped_WhenScrobblePlaybackStopIsFalse_DoesNotScrobble()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            ScrobblePlaybackStop = false, // Disabled!
            SyncMovies = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var movie = new Movie { Name = "Completed Movie" };
        movie.ProviderIds["Tmdb"] = "550";

        var args = new PlaybackStopEventArgs
        {
            Item = movie,
            PlayedToCompletion = true,
            Users = new List<User> { CreateTestUser() },
        };

        _sessionManagerMock.Raise(s => s.PlaybackStopped += null, args);

        await Task.Delay(100);

        Assert.Empty(mockHandler.Requests);
    }

    [Fact]
    public async Task PlaybackStopped_WhenValidMovieCompleted_ScrobblesToBetaSeries()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            ScrobblePlaybackStop = true,
            SyncMovies = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?tmdb_id=550",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"title\":\"Fight Club\"}}");
        mockHandler.Setup(
            HttpMethod.Post,
            "/movies/movie",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"user\":{\"status\":1}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var movie = new Movie { Name = "Fight Club" };
        movie.ProviderIds["Tmdb"] = "550";

        var args = new PlaybackStopEventArgs
        {
            Item = movie,
            PlayedToCompletion = true,
            Users = new List<User> { CreateTestUser() },
        };

        _sessionManagerMock.Raise(s => s.PlaybackStopped += null, args);

        await Task.Delay(150);

        // Should have made 1 lookup request and 1 post request
        Assert.Equal(2, mockHandler.Requests.Count);
        Assert.Contains(mockHandler.Requests, r => r.Method == HttpMethod.Get && (r.RequestUri?.PathAndQuery.Contains("/movies/movie") ?? false));
        Assert.Contains(mockHandler.Requests, r => r.Method == HttpMethod.Post && (r.RequestUri?.PathAndQuery.Contains("/movies/movie") ?? false));
    }

    [Fact]
    public async Task PlaybackStopped_WhenValidEpisodeCompleted_ScrobblesToBetaSeriesWithBulkFalse()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            ScrobblePlaybackStop = true,
            SyncShows = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/episodes/display?thetvdb_id=349232",
            HttpStatusCode.OK,
            "{\"episode\":{\"id\":239475,\"title\":\"Pilot\"}}");
        mockHandler.Setup(
            HttpMethod.Post,
            "/episodes/watched",
            HttpStatusCode.OK,
            "{\"episode\":{\"id\":239475,\"user\":{\"seen\":true}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var episode = new Episode
        {
            Name = "Pilot",
            IndexNumber = 1,
            ParentIndexNumber = 1,
        };
        episode.ProviderIds["Tvdb"] = "349232";

        var args = new PlaybackStopEventArgs
        {
            Item = episode,
            PlayedToCompletion = true,
            Users = new List<User> { CreateTestUser() },
        };

        _sessionManagerMock.Raise(s => s.PlaybackStopped += null, args);

        await Task.Delay(150);

        Assert.Equal(2, mockHandler.Requests.Count);
        var postRequest = mockHandler.Requests.FirstOrDefault(r => r.Method == HttpMethod.Post);
        Assert.NotNull(postRequest);
        Assert.Contains("bulk=false", postRequest.Content);
    }

    [Fact]
    public async Task UserDataSaved_WhenSaveReasonIsPlaybackFinished_IsIgnoredToPreventDuplicates()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            SyncUserDataSaved = true,
            SyncMovies = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var movie = new Movie { Name = "Test Movie" };
        movie.ProviderIds["Tmdb"] = "550";

        var args = new UserDataSaveEventArgs
        {
            Item = movie,
            UserId = _userId,
            SaveReason = UserDataSaveReason.PlaybackFinished, // Must be ignored!
            UserData = new UserItemData { Key = "test-key", Played = true },
        };

        _userDataManagerMock.Raise(u => u.UserDataSaved += null, args);

        await Task.Delay(100);

        Assert.Empty(mockHandler.Requests);
    }

    [Fact]
    public async Task UserDataSaved_WhenTogglePlayedFalse_CallsUnmarkEndpoint()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            SyncUserDataSaved = true,
            SyncMovies = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?tmdb_id=550",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"title\":\"Fight Club\"}}");
        mockHandler.Setup(
            HttpMethod.Delete,
            "/movies/movie?id=141",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"user\":{\"in_account\":false}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var movie = new Movie { Name = "Fight Club" };
        movie.ProviderIds["Tmdb"] = "550";

        var args = new UserDataSaveEventArgs
        {
            Item = movie,
            UserId = _userId,
            SaveReason = UserDataSaveReason.TogglePlayed,
            UserData = new UserItemData { Key = "test-key", Played = false }, // Unmarked!
        };

        _userDataManagerMock.Raise(u => u.UserDataSaved += null, args);

        await Task.Delay(150);

        Assert.Equal(2, mockHandler.Requests.Count);
        var deleteRequest = mockHandler.Requests.FirstOrDefault(r => r.Method == HttpMethod.Delete);
        Assert.NotNull(deleteRequest);
        Assert.Contains("id=141", deleteRequest.RequestUri?.Query);
    }

    [Fact]
    public async Task Debounce_WhenDuplicateEventsFireRapidly_OnlyCallsApiOnce()
    {
        var profile = new BetaSeriesProfile
        {
            JellyfinUserIds = new[] { _userId },
            Token = "token-123",
            SyncUserDataSaved = true,
            SyncMovies = true,
        };
        SetupPlugin(profile);

        var mockHandler = new MockHttpMessageHandler();
        mockHandler.Setup(
            HttpMethod.Get,
            "/movies/movie?tmdb_id=550",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"title\":\"Fight Club\"}}");
        mockHandler.Setup(
            HttpMethod.Post,
            "/movies/movie",
            HttpStatusCode.OK,
            "{\"movie\":{\"id\":141,\"user\":{\"status\":1}}}");

        using var httpClient = new HttpClient(mockHandler);
        var client = new BetaSeriesClient(httpClient, NullLogger<BetaSeriesClient>.Instance);

        using var manager = new BetaSeriesManager(
            NullLogger<BetaSeriesManager>.Instance,
            _sessionManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            client);

        await manager.StartAsync(CancellationToken.None);

        var movie = new Movie { Name = "Fight Club" };
        movie.ProviderIds["Tmdb"] = "550";

        var args = new UserDataSaveEventArgs
        {
            Item = movie,
            UserId = _userId,
            SaveReason = UserDataSaveReason.TogglePlayed,
            UserData = new UserItemData { Key = "test-key", Played = true },
        };

        // Fire event twice rapidly
        _userDataManagerMock.Raise(u => u.UserDataSaved += null, args);
        _userDataManagerMock.Raise(u => u.UserDataSaved += null, args);

        await Task.Delay(150);

        // Lookup: 1 request (cached on second attempt)
        // Post scrobble: only 1 request (debounced on second attempt)
        var postRequests = mockHandler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Single(postRequests);
    }
}
