using Jellyfin.Plugin.Sonos.Web;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class IndexHtmlInjectorTests
{
    [Theory]
    [InlineData("/web")]
    [InlineData("/web/")]
    [InlineData("/web/index.html")]
    [InlineData("/Web/Index.html")]
    [InlineData("/media/web")]
    [InlineData("/media/web/")]
    [InlineData("/media/web/index.html")]
    public void IsWebIndexPath_MatchesShell(string path)
    {
        Assert.True(IndexHtmlInjector.IsWebIndexPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/web/main.jellyfin.bundle.js")]
    [InlineData("/Sonos/web/sonos-client.js")]
    [InlineData("/media/Sonos/web/sonos-client.js")]
    [InlineData("/media/Sonos/web")]
    public void IsWebIndexPath_IgnoresOtherPaths(string? path)
    {
        Assert.False(IndexHtmlInjector.IsWebIndexPath(path));
    }

    [Fact]
    public void ResolvePublicBase_PrefersPathBase()
    {
        Assert.Equal("/media", IndexHtmlInjector.ResolvePublicBase("/media", "/web/index.html"));
    }

    [Fact]
    public void ResolvePublicBase_InfersFromUnstrippedPath()
    {
        Assert.Equal("/media", IndexHtmlInjector.ResolvePublicBase(string.Empty, "/media/web/index.html"));
    }

    [Fact]
    public void Inject_InsertsTagsBeforeHeadClose()
    {
        const string html = "<html><head><title>x</title></head><body></body></html>";

        var result = IndexHtmlInjector.Inject(html, "/media/Sonos/web/sonos-client.css?v=1", "/media/Sonos/web/sonos-client.js?v=1");

        Assert.Contains("data-jellyfin-sonos-client=\"1\"", result, System.StringComparison.Ordinal);
        Assert.Contains("/media/Sonos/web/sonos-client.css?v=1", result, System.StringComparison.Ordinal);
        Assert.Contains("/media/Sonos/web/sonos-client.js?v=1", result, System.StringComparison.Ordinal);
        var linkAt = result.IndexOf("<link", System.StringComparison.Ordinal);
        var scriptAt = result.IndexOf("<script", System.StringComparison.Ordinal);
        var headClose = result.IndexOf("</head>", System.StringComparison.OrdinalIgnoreCase);
        Assert.True(linkAt < scriptAt);
        Assert.True(scriptAt < headClose);
    }

    [Fact]
    public void Inject_IsIdempotent()
    {
        const string html = "<html><head></head></html>";
        var once = IndexHtmlInjector.Inject(html, "/a.css", "/a.js");
        var twice = IndexHtmlInjector.Inject(once, "/b.css", "/b.js");

        Assert.Equal(once, twice);
        Assert.Equal(2, CountOccurrences(twice, IndexHtmlInjector.Marker));
    }

    [Fact]
    public void ClientAssets_AreEmbedded()
    {
        var names = typeof(IndexHtmlInjector).Assembly.GetManifestResourceNames();
        Assert.Contains("Jellyfin.Plugin.Sonos.Web.sonos-client.js", names);
        Assert.Contains("Jellyfin.Plugin.Sonos.Web.player-handoff.js", names);
        Assert.Contains("Jellyfin.Plugin.Sonos.Web.sonos-client.css", names);
    }

    [Fact]
    public void Inject_LoadsHandoffModuleBeforeClient()
    {
        const string html = "<html><head></head></html>";

        var result = IndexHtmlInjector.Inject(
            html,
            "/c.css",
            "/Sonos/web/player-handoff.js",
            "/Sonos/web/sonos-client.js");

        var handoffAt = result.IndexOf("player-handoff.js", System.StringComparison.Ordinal);
        var clientAt = result.IndexOf("sonos-client.js", System.StringComparison.Ordinal);
        Assert.True(handoffAt >= 0);
        Assert.True(clientAt > handoffAt);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
