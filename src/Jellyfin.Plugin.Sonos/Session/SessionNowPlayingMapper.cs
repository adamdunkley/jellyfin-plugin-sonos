using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Sonos.Api.Models;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// Builds the <see cref="BaseItemDto"/> jellyfin-web's now-playing bar reads from a Play To session.
/// </summary>
public static class SessionNowPlayingMapper
{
    /// <summary>
    /// Maps a queue row into a now-playing DTO with title and artists.
    /// </summary>
    /// <param name="item">Current queue item.</param>
    /// <returns>A DTO the now-playing bar can render.</returns>
    public static BaseItemDto FromQueueItem(QueueItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new BaseItemDto
        {
            Id = item.ItemId,
            Name = item.Name,
            Album = item.Album,
            Artists = item.Artists ?? [],
            RunTimeTicks = item.DurationTicks > 0 ? item.DurationTicks : null,
            MediaType = MediaType.Audio,
            Type = BaseItemKind.Audio
        };
    }

    /// <summary>
    /// Maps a queue row and copies artwork tags from the library item when present.
    /// </summary>
    /// <param name="item">Current queue item.</param>
    /// <param name="libraryItem">Resolved library item, if any.</param>
    /// <param name="images">Image processor used for cache tags.</param>
    /// <returns>A DTO the now-playing bar can render, including artwork when available.</returns>
    public static BaseItemDto FromQueueItem(QueueItemDto item, BaseItem? libraryItem, IImageProcessor? images)
    {
        var dto = FromQueueItem(item);
        if (libraryItem is null)
        {
            return dto;
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            dto.Name = libraryItem.Name;
        }

        dto.RunTimeTicks ??= libraryItem.RunTimeTicks;
        ApplyImages(dto, libraryItem, images);
        return dto;
    }

    private static void ApplyImages(BaseItemDto dto, BaseItem item, IImageProcessor? images)
    {
        if (images is null)
        {
            return;
        }

        if (TryPrimaryTag(item, images, out var ownTag))
        {
            dto.ImageTags = new Dictionary<ImageType, string>
            {
                [ImageType.Primary] = ownTag
            };
        }

        var artItem = ItemWithPrimaryImage(item);
        if (artItem is null || artItem.Id.Equals(item.Id))
        {
            return;
        }

        if (!TryPrimaryTag(artItem, images, out var albumTag))
        {
            return;
        }

        dto.AlbumId = artItem.Id;
        dto.AlbumPrimaryImageTag = albumTag;
    }

    private static bool TryPrimaryTag(BaseItem item, IImageProcessor images, out string tag)
    {
        tag = string.Empty;
        if (!item.HasImage(ImageType.Primary, 0))
        {
            return false;
        }

        try
        {
            var info = item.GetImageInfo(ImageType.Primary, 0);
            tag = images.GetImageCacheTag(item, info) ?? string.Empty;
        }
        catch (Exception)
        {
            return false;
        }

        return !string.IsNullOrEmpty(tag);
    }

    private static BaseItem? ItemWithPrimaryImage(BaseItem item)
    {
        if (item.HasImage(ImageType.Primary, 0))
        {
            return item;
        }

        if (item is Audio audio && audio.AlbumEntity is BaseItem album && album.HasImage(ImageType.Primary, 0))
        {
            return album;
        }

        for (var parent = item.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent.HasImage(ImageType.Primary, 0))
            {
                return parent;
            }
        }

        return null;
    }
}
