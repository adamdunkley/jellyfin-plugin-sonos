using System;
using System.IO;
using Jellyfin.Plugin.Sonos.Streaming;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Sonos.Api;

/// <summary>
/// Speaker-facing tokenized artwork.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Sonos/image")]
public class ImageController : ControllerBase
{
    private readonly StreamTokenService _tokens;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageController"/> class.
    /// </summary>
    /// <param name="tokens">Token service.</param>
    /// <param name="libraryManager">Library.</param>
    public ImageController(StreamTokenService tokens, ILibraryManager libraryManager)
    {
        _tokens = tokens;
        _libraryManager = libraryManager;
    }

    /// <summary>HEAD probe.</summary>
    /// <param name="token">Stream token.</param>
    /// <returns>Headers only.</returns>
    [HttpHead("{token}")]
    public IActionResult Head(string token) => Serve(token);

    /// <summary>GET primary image.</summary>
    /// <param name="token">Stream token.</param>
    /// <returns>Image bytes.</returns>
    [HttpGet("{token}")]
    public IActionResult Get(string token) => Serve(token);

    private IActionResult Serve(string token)
    {
        if (!_tokens.TryUnpack(token, out var payload, out var expired) || expired)
        {
            return ProblemResults.Create(
                expired ? StatusCodes.Status410Gone : StatusCodes.Status403Forbidden,
                expired ? "StreamExpired" : "InvalidToken",
                "Image token is invalid or expired");
        }

        var item = _libraryManager.GetItemById<BaseItem>(payload.ItemId, payload.UserId);
        if (item is null)
        {
            return NotFound();
        }

        var imageItem = ItemWithPrimaryImage(item);
        if (imageItem is null)
        {
            return NotFound();
        }

        var path = imageItem.GetImagePath(ImageType.Primary);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/jpeg",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
        return PhysicalFile(path, contentType);
    }

    private static BaseItem? ItemWithPrimaryImage(BaseItem item)
    {
        if (HasPrimaryFile(item))
        {
            return item;
        }

        if (item is Audio audio && audio.AlbumEntity is BaseItem albumEntity && HasPrimaryFile(albumEntity))
        {
            return albumEntity;
        }

        for (var parent = item.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (HasPrimaryFile(parent))
            {
                return parent;
            }
        }

        return null;
    }

    private static bool HasPrimaryFile(BaseItem item)
    {
        if (!item.HasImage(ImageType.Primary, 0))
        {
            return false;
        }

        var path = item.GetImagePath(ImageType.Primary);
        return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
    }
}
