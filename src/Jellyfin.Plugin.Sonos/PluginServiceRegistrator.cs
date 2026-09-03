using Jellyfin.Plugin.Sonos.Control;
using Jellyfin.Plugin.Sonos.Discovery;
using Jellyfin.Plugin.Sonos.Queue;
using Jellyfin.Plugin.Sonos.Session;
using Jellyfin.Plugin.Sonos.Streaming;
using Jellyfin.Plugin.Sonos.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Sonos;

/// <summary>
/// Registers plugin services with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PlayerRegistry>();
        serviceCollection.AddSingleton<SonosHttpProbe>();
        serviceCollection.AddSingleton<SsdpProbe>();
        serviceCollection.AddSingleton<CoordinatorGate>();
        serviceCollection.AddSingleton<SoapAvTransportClient>();
        serviceCollection.AddSingleton<LanControlClient>();
        serviceCollection.AddSingleton<ISonosControlClient, CompositeSonosControlClient>();
        serviceCollection.AddSingleton<StreamTokenService>();
        serviceCollection.AddSingleton<FfmpegTranscodeCache>();
        serviceCollection.AddSingleton<LogicalQueueStore>();
        serviceCollection.AddSingleton<TargetResolver>();
        serviceCollection.AddSingleton<SonosPlaybackService>();
        serviceCollection.AddTransient<IStartupFilter, SonosWebInjectionStartupFilter>();
        serviceCollection.AddHostedService<DiscoveryHostedService>();
        serviceCollection.AddHostedService<SonosSessionBridge>();
        serviceCollection.AddHostedService<SonosIndexHtmlPatchHostedService>();
    }
}
