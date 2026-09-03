using System;

namespace Jellyfin.Plugin.Sonos.Web;

/// <summary>
/// Idempotent injection of the Sonos web client into jellyfin-web's index.html.
/// </summary>
public static class IndexHtmlInjector
{
    /// <summary>
    /// Attribute written on injected tags so a second pass is a no-op.
    /// </summary>
    public const string Marker = "data-jellyfin-sonos-client";

    /// <summary>
    /// Returns true when <paramref name="path"/> is jellyfin-web's HTML shell.
    /// Matches with or without a server BaseUrl prefix because this middleware
    /// may run before <c>UsePathBase</c>.
    /// </summary>
    /// <param name="path">Request path without query, with or without a trailing slash.</param>
    /// <returns>True for <c>/web</c>, <c>/media/web</c>, and <c>.../web/index.html</c>.</returns>
    public static bool IsWebIndexPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var trimmed = path.TrimEnd('/');
        if (trimmed.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lastSlash = trimmed.LastIndexOf('/');
        var last = lastSlash < 0 ? trimmed : trimmed[(lastSlash + 1)..];
        if (!last.Equals("web", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parent = lastSlash < 0 ? string.Empty : trimmed[..lastSlash];
        return !parent.EndsWith("/Sonos", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Public origin prefix for plugin asset URLs (e.g. <c>/media</c>).
    /// </summary>
    /// <param name="pathBase">ASP.NET PathBase, if already applied.</param>
    /// <param name="path">Raw request path.</param>
    /// <returns>Prefix with no trailing slash, or empty.</returns>
    public static string ResolvePublicBase(string? pathBase, string? path)
    {
        if (!string.IsNullOrEmpty(pathBase) && pathBase != "/")
        {
            return pathBase.TrimEnd('/');
        }

        var p = path ?? string.Empty;
        var idx = p.IndexOf("/web", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? p[..idx].TrimEnd('/') : string.Empty;
    }

    /// <summary>
    /// Inserts stylesheet and script tags before <c>&lt;/head&gt;</c> when not already present.
    /// </summary>
    /// <param name="html">Current index.html body.</param>
    /// <param name="cssUrl">Absolute-from-root CSS URL.</param>
    /// <param name="scriptUrls">Absolute-from-root JS URLs, in load order.</param>
    /// <returns>Possibly modified HTML.</returns>
    public static string Inject(string html, string cssUrl, params string[] scriptUrls)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var snippet = "<link rel=\"stylesheet\" href=\"" + cssUrl + "\" " + Marker + "=\"1\" />";
        if (scriptUrls is not null)
        {
            foreach (var scriptUrl in scriptUrls)
            {
                snippet += "<script src=\"" + scriptUrl + "\" defer " + Marker + "=\"1\"></script>";
            }
        }

        var idx = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return html.Insert(idx, snippet);
        }

        return snippet + html;
    }
}
