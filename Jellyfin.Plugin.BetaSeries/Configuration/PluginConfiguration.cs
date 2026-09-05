using System;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BetaSeries.Configuration;

/// <summary>
/// Plugin configuration for BetaSeries.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ClientId = string.Empty;
        ClientSecret = string.Empty;
        RedirectUri = string.Empty;
        Profiles = Array.Empty<BetaSeriesProfile>();
    }

    /// <summary>
    /// Gets or sets the BetaSeries OAuth Client ID (Developer Key).
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the BetaSeries OAuth Client Secret.
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the OAuth Redirect URI.
    /// </summary>
    public string RedirectUri { get; set; }

    /// <summary>
    /// Gets or sets the list of configured user profiles.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Needed for XML serialization")]
    public BetaSeriesProfile[] Profiles { get; set; }
}
