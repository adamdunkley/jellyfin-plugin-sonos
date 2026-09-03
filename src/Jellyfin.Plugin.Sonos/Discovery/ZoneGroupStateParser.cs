using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Parses ZoneGroupTopology GetZoneGroupState XML.
/// </summary>
public static class ZoneGroupStateParser
{
    /// <summary>
    /// Parses the ZoneGroupState XML blob (may be nested/escaped).
    /// </summary>
    /// <param name="xml">Raw SOAP or inner XML.</param>
    /// <returns>Visible members.</returns>
    public static IReadOnlyList<ZoneGroupMember> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var unescaped = xml.Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(unescaped);
        }
        catch (Exception)
        {
            return [];
        }

        var members = new List<ZoneGroupMember>();
        foreach (var group in DescendantsByLocalName(doc.Root, "ZoneGroup"))
        {
            var coordinator = Attr(group, "Coordinator");
            var groupId = Attr(group, "ID");
            foreach (var member in DescendantsByLocalName(group, "ZoneGroupMember"))
            {
                var id = Attr(member, "UUID");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var invisible = Attr(member, "Invisible") is "1" or "true";
                var swGenRaw = Attr(member, "SWGen");
                _ = int.TryParse(swGenRaw, out var swGen);

                members.Add(new ZoneGroupMember
                {
                    Id = id,
                    ZoneName = Attr(member, "ZoneName"),
                    Location = Attr(member, "Location"),
                    SoftwareGeneration = swGen,
                    SoftwareVersion = Attr(member, "SoftwareVersion"),
                    Invisible = invisible,
                    GroupId = groupId,
                    CoordinatorId = coordinator
                });
            }
        }

        return members;
    }

    /// <summary>
    /// True when this member should be treated as S2.
    /// </summary>
    /// <param name="member">Parsed member.</param>
    /// <returns>True for S2.</returns>
    public static bool IsS2(ZoneGroupMember member)
    {
        return member.SoftwareGeneration == 2;
    }

    private static IEnumerable<XElement> DescendantsByLocalName(XElement? root, string localName)
    {
        if (root is null)
        {
            yield break;
        }

        if (string.Equals(root.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
        {
            yield return root;
        }

        foreach (var el in root.Descendants())
        {
            if (string.Equals(el.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            {
                yield return el;
            }
        }
    }

    private static string Attr(XElement el, string name)
    {
        foreach (var attr in el.Attributes())
        {
            if (string.Equals(attr.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            {
                return attr.Value.Trim();
            }
        }

        return string.Empty;
    }
}
