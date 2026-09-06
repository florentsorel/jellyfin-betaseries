using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetaSeries.Api;
using Jellyfin.Plugin.BetaSeries.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetaSeries;

/// <summary>
/// Background service listening to Jellyfin playback and user data events to sync with BetaSeries.
/// </summary>
public class BetaSeriesManager : IHostedService, IDisposable
{
    private readonly ILogger<BetaSeriesManager> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly BetaSeriesClient _client;

    private readonly ConcurrentDictionary<string, DateTime> _recentSyncs = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BetaSeriesManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionManager">The session manager instance.</param>
    /// <param name="userDataManager">The user data manager instance.</param>
    /// <param name="libraryManager">The library manager instance.</param>
    /// <param name="client">The BetaSeries client instance.</param>
    public BetaSeriesManager(
        ILogger<BetaSeriesManager> logger,
        ISessionManager sessionManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        BetaSeriesClient client)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _client = client;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BetaSeries plugin service starting, subscribing to events.");
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BetaSeries plugin service stopping, unsubscribing from events.");
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _sessionManager.PlaybackStopped -= OnPlaybackStopped;
                _userDataManager.UserDataSaved -= OnUserDataSaved;
            }

            _disposed = true;
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (!e.PlayedToCompletion)
        {
            _logger.LogDebug(
                "BetaSeries: PlaybackStopped ignored because PlayedToCompletion is false for item {ItemName}",
                e.Item?.Name);
            return;
        }

        if (e.Item is not (Movie or Episode))
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return;
        }

        var userIds = new HashSet<Guid>();
        if (e.Users != null)
        {
            foreach (var user in e.Users)
            {
                userIds.Add(user.Id);
            }
        }

        if (e.Session != null && !e.Session.UserId.Equals(Guid.Empty))
        {
            userIds.Add(e.Session.UserId);
        }

        if (userIds.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var userId in userIds)
                {
                    var profiles = GetMatchingProfiles(config, userId, p => p.ScrobblePlaybackStop);
                    foreach (var profile in profiles)
                    {
                        await ProcessItemAsync(
                            config.ClientId,
                            profile,
                            e.Item,
                            isWatched: true,
                            action: "playback_stop",
                            watchedAt: DateTime.UtcNow).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BetaSeries: Error processing PlaybackStopped event for item {ItemName}", e.Item.Name);
            }
        });
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Only react to manual toggle or manual updates; ignore automatic playback finishing to prevent duplicate scrobbles
        if (e.SaveReason is not (UserDataSaveReason.TogglePlayed or UserDataSaveReason.UpdateUserData))
        {
            return;
        }

        if (e.Item is not (Movie or Episode))
        {
            return;
        }

        if (e.UserData == null || e.UserId.Equals(Guid.Empty))
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return;
        }

        var profiles = GetMatchingProfiles(config, e.UserId, p => p.SyncUserDataSaved);
        if (profiles.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var isWatched = e.UserData.Played;
                var watchedAt = e.UserData.LastPlayedDate ?? DateTime.UtcNow;

                foreach (var profile in profiles)
                {
                    await ProcessItemAsync(
                        config.ClientId,
                        profile,
                        e.Item,
                        isWatched: isWatched,
                        action: isWatched ? "userdata_watched" : "userdata_unwatched",
                        watchedAt: watchedAt).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BetaSeries: Error processing UserDataSaved event for item {ItemName}", e.Item.Name);
            }
        });
    }

    private static List<BetaSeriesProfile> GetMatchingProfiles(
        PluginConfiguration config,
        Guid userId,
        Func<BetaSeriesProfile, bool> predicate)
    {
        if (config.Profiles == null)
        {
            return new List<BetaSeriesProfile>();
        }

        return config.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Token) &&
                        p.JellyfinUserIds.Contains(userId) &&
                        predicate(p))
            .ToList();
    }

    private async Task ProcessItemAsync(
        string clientId,
        BetaSeriesProfile profile,
        BaseItem item,
        bool isWatched,
        string action,
        DateTime watchedAt)
    {
        if (item is Movie movie)
        {
            if (!profile.SyncMovies)
            {
                return;
            }

            await SyncMovieAsync(clientId, profile, movie, isWatched, action, watchedAt).ConfigureAwait(false);
        }
        else if (item is Episode episode)
        {
            if (!profile.SyncShows)
            {
                return;
            }

            await SyncEpisodeAsync(clientId, profile, episode, isWatched, action, watchedAt).ConfigureAwait(false);
        }
    }

    private async Task SyncMovieAsync(
        string clientId,
        BetaSeriesProfile profile,
        Movie movie,
        bool isWatched,
        string action,
        DateTime watchedAt)
    {
        var tmdbId = movie.GetProviderId(MetadataProvider.Tmdb);
        var imdbId = movie.GetProviderId(MetadataProvider.Imdb);

        var movieId = await _client.FindMovieIdAsync(clientId, profile.Token, tmdbId, imdbId).ConfigureAwait(false);
        if (!movieId.HasValue)
        {
            _logger.LogWarning(
                "BetaSeries: Could not find movie '{MovieName}' (TMDB: {TmdbId}, IMDb: {ImdbId}) on BetaSeries",
                movie.Name,
                tmdbId,
                imdbId);
            return;
        }

        var debounceKey = $"{profile.Id}:movie:{movieId.Value}:{(isWatched ? "watch" : "unwatch")}";
        if (!ShouldProceedWithSync(debounceKey))
        {
            _logger.LogDebug(
                "BetaSeries: Debounce skipped sync for movie '{MovieName}' on profile '{ProfileName}'",
                movie.Name,
                profile.Name);
            return;
        }

        if (isWatched)
        {
            var success = await _client.ScrobbleMovieAsync(clientId, profile.Token, movieId.Value, watchedAt).ConfigureAwait(false);
            if (success)
            {
                _logger.LogInformation(
                    "BetaSeries: Successfully marked movie '{MovieName}' as watched on profile '{ProfileName}' ({Username})",
                    movie.Name,
                    profile.Name,
                    profile.BetaSeriesUsername);
            }
        }
        else
        {
            var success = await _client.UnmarkMovieAsync(clientId, profile.Token, movieId.Value).ConfigureAwait(false);
            if (success)
            {
                _logger.LogInformation(
                    "BetaSeries: Successfully unmarked movie '{MovieName}' on profile '{ProfileName}' ({Username})",
                    movie.Name,
                    profile.Name,
                    profile.BetaSeriesUsername);
            }
        }
    }

    private async Task SyncEpisodeAsync(
        string clientId,
        BetaSeriesProfile profile,
        Episode episode,
        bool isWatched,
        string action,
        DateTime watchedAt)
    {
        var series = episode.Series ??
                     episode.FindParent<Series>() ??
                     (_libraryManager.GetItemById(episode.SeriesId) as Series);

        var seriesTitle = series?.Name ?? episode.SeriesName ?? "Unknown Series";
        var seasonNum = episode.ParentIndexNumber ?? episode.AiredSeasonNumber ?? 1;
        var episodeNum = episode.IndexNumber ?? 1;

        var episodeTvdbId = episode.GetProviderId(MetadataProvider.Tvdb);
        var seriesTvdbId = series?.GetProviderId(MetadataProvider.Tvdb);
        var seriesImdbId = series?.GetProviderId(MetadataProvider.Imdb);
        var seriesTmdbId = series?.GetProviderId(MetadataProvider.Tmdb);

        int showId = 0;
        if (!string.IsNullOrWhiteSpace(seriesTvdbId) || !string.IsNullOrWhiteSpace(seriesImdbId) || !string.IsNullOrWhiteSpace(seriesTitle))
        {
            var foundShowId = await _client.FindShowIdAsync(clientId, profile.Token, seriesTvdbId, seriesImdbId, seriesTitle, seriesTmdbId).ConfigureAwait(false);
            showId = foundShowId ?? 0;
        }

        var episodeId = await _client.FindEpisodeIdAsync(
            clientId,
            profile.Token,
            showId,
            seasonNum,
            episodeNum,
            episodeTvdbId).ConfigureAwait(false);

        if (!episodeId.HasValue)
        {
            _logger.LogWarning(
                "BetaSeries: Could not find episode '{SeriesTitle} S{SeasonNum:00}E{EpisodeNum:00}' (Episode TVDB: {EpTvdbId}, Series TVDB: {ShowTvdbId}, IMDb: {ShowImdbId}) on BetaSeries",
                seriesTitle,
                seasonNum,
                episodeNum,
                episodeTvdbId,
                seriesTvdbId,
                seriesImdbId);
            return;
        }

        var debounceKey = $"{profile.Id}:episode:{episodeId.Value}:{(isWatched ? "watch" : "unwatch")}";
        if (!ShouldProceedWithSync(debounceKey))
        {
            _logger.LogDebug(
                "BetaSeries: Debounce skipped sync for episode '{SeriesTitle} S{SeasonNum:00}E{EpisodeNum:00}' on profile '{ProfileName}'",
                seriesTitle,
                seasonNum,
                episodeNum,
                profile.Name);
            return;
        }

        if (isWatched)
        {
            var success = await _client.ScrobbleEpisodeAsync(clientId, profile.Token, episodeId.Value, watchedAt).ConfigureAwait(false);
            if (success)
            {
                _logger.LogInformation(
                    "BetaSeries: Successfully marked episode '{SeriesTitle} S{SeasonNum:00}E{EpisodeNum:00}' as watched on profile '{ProfileName}' ({Username})",
                    seriesTitle,
                    seasonNum,
                    episodeNum,
                    profile.Name,
                    profile.BetaSeriesUsername);
            }
        }
        else
        {
            var success = await _client.UnmarkEpisodeAsync(clientId, profile.Token, episodeId.Value).ConfigureAwait(false);
            if (success)
            {
                _logger.LogInformation(
                    "BetaSeries: Successfully unmarked episode '{SeriesTitle} S{SeasonNum:00}E{EpisodeNum:00}' on profile '{ProfileName}' ({Username})",
                    seriesTitle,
                    seasonNum,
                    episodeNum,
                    profile.Name,
                    profile.BetaSeriesUsername);
            }
        }
    }

    private bool ShouldProceedWithSync(string debounceKey)
    {
        var now = DateTime.UtcNow;

        // Clean up entries older than 2 minutes
        if (_recentSyncs.Count > 100)
        {
            foreach (var kvp in _recentSyncs)
            {
                if ((now - kvp.Value).TotalMinutes > 2)
                {
                    _recentSyncs.TryRemove(kvp.Key, out _);
                }
            }
        }

        if (_recentSyncs.TryGetValue(debounceKey, out var lastTime))
        {
            if ((now - lastTime).TotalSeconds < 30)
            {
                return false;
            }
        }

        _recentSyncs[debounceKey] = now;
        return true;
    }
}
