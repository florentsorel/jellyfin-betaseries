using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.BetaSeries.Configuration;

/// <summary>
/// Configuration for a specific user/profile.
/// </summary>
public class BetaSeriesProfile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BetaSeriesProfile"/> class.
    /// </summary>
    public BetaSeriesProfile()
    {
        Id = Guid.NewGuid();
        Name = "Nouveau profil";
        JellyfinUserIds = Array.Empty<Guid>();
        Token = string.Empty;
        BetaSeriesUsername = string.Empty;
        ScrobblePlaybackStop = true;
        SyncUserDataSaved = true;
        SyncMovies = true;
        SyncShows = true;
    }

    /// <summary>
    /// Gets or sets the unique identifier of the profile.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the friendly name of the profile.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin user IDs linked to this profile.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Needed for XML serialization")]
    public Guid[] JellyfinUserIds { get; set; }

    /// <summary>
    /// Gets or sets the BetaSeries user access token.
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// Gets or sets the BetaSeries username.
    /// </summary>
    public string BetaSeriesUsername { get; set; }

    /// <summary>
    /// Gets or sets the BetaSeries numeric member ID.
    /// </summary>
    public int? BetaSeriesUserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playback stop event triggers scrobble.
    /// </summary>
    public bool ScrobblePlaybackStop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether manual mark as watched (UserDataSaved) triggers sync.
    /// </summary>
    public bool SyncUserDataSaved { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether movie watch events are synchronized.
    /// </summary>
    public bool SyncMovies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether show/episode watch events are synchronized.
    /// </summary>
    public bool SyncShows { get; set; }
}
