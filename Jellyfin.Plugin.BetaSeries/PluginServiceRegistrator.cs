using Jellyfin.Plugin.BetaSeries.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.BetaSeries;

/// <summary>
/// Registers plugin services into the Jellyfin dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<BetaSeriesClient>();
        serviceCollection.AddHostedService<BetaSeriesManager>();
    }
}
