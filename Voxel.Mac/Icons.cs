using System.Reflection;
using System.Xml.Linq;
using Avalonia.Media;

namespace Voxel.Mac;

/// <summary>
/// Tabler icons (MIT) by name. Most come from the Windows app's Icons.xaml so both apps share one
/// set; the Windows app draws a few others with the Segoe icon font, which Macs don't have.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> PathData = Load();

    private static Dictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Voxel.Icons.xaml")!;
        var icons = XDocument.Load(stream).Root!.Elements()
            .ToDictionary(e => e.Attributes().First(a => a.Name.LocalName == "Key").Value["Icon.".Length..], e => e.Value.Trim());
        icons["settings"] = "M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065 M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0";
        icons["arrow-left"] = "M5 12l14 0 M5 12l6 6 M5 12l6 -6";
        icons["check"] = "M5 12l5 5l10 -10";
        icons["info-circle"] = "M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0 M12 9h.01 M11 12h1v4h1";
        return icons;
    }

    /// <summary>The icon's shape, or the microphone if there's no icon by that name.</summary>
    public static Geometry Get(string name) => StreamGeometry.Parse(PathData.TryGetValue(name, out var data) ? data : PathData["microphone"]);
}
