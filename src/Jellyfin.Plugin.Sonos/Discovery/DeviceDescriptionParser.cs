using System;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Parses UPnP device_description.xml.
/// </summary>
public static class DeviceDescriptionParser
{
    /// <summary>
    /// Parses a device description XML document.
    /// </summary>
    /// <param name="xml">Raw XML.</param>
    /// <returns>The description, or null if required fields are missing.</returns>
    public static DeviceDescription? Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return null;
        }

        var device = FindDevice(doc.Root);
        if (device is null)
        {
            return null;
        }

        var udn = Local(device, "UDN");
        var id = StripUuid(udn);
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return new DeviceDescription
        {
            Id = id,
            RoomName = Local(device, "roomName"),
            ModelNumber = Local(device, "modelNumber"),
            ModelName = Local(device, "modelName"),
            DisplayName = Local(device, "displayName")
        };
    }

    private static XElement? FindDevice(XElement? root)
    {
        if (root is null)
        {
            return null;
        }

        foreach (var el in root.Descendants())
        {
            if (string.Equals(el.Name.LocalName, "device", StringComparison.OrdinalIgnoreCase))
            {
                return el;
            }
        }

        return null;
    }

    private static string Local(XElement device, string localName)
    {
        foreach (var el in device.Elements())
        {
            if (string.Equals(el.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            {
                return el.Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string StripUuid(string udn)
    {
        const string prefix = "uuid:";
        if (udn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return udn[prefix.Length..];
        }

        return udn.Trim();
    }
}
