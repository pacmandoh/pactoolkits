using System;
using System.Collections.Concurrent;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Platform;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>Assets/Icons 下 Tabler outline SVG，path 以控件 Foreground 描边</summary>
internal static class StrokeAssets
{
    private const string BaseUri = "avares://PacToolkits.Desktop/Assets/Icons/";
    private static readonly ConcurrentDictionary<string, Geometry?> Cache = new(StringComparer.Ordinal);

    public static Geometry? TryGet(string kind)
    {
        var stem = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return stem.Length == 0 ? null : Cache.GetOrAdd(stem, Load);
    }

    private static Geometry? Load(string stem)
    {
        try
        {
            var uri = new Uri(BaseUri + stem + ".svg");
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            var doc = XDocument.Load(stream);
            var group = new GeometryGroup { FillRule = FillRule.NonZero };

            foreach (var el in doc.Descendants())
            {
                if (!string.Equals(el.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Tabler 占位 path：stroke="none"
                if (string.Equals(el.Attribute("stroke")?.Value, "none", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var d = el.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(d))
                {
                    continue;
                }

                group.Children.Add(StreamGeometry.Parse(d));
            }

            return group.Children.Count == 0 ? null : group;
        }
        catch
        {
            return null;
        }
    }
}
