namespace Voxel.Mac;

/// <summary>
/// Where Voxel's files are. In Voxel.app they sit in Contents/Resources/Assets, where macOS expects
/// data (code signing treats Contents/MacOS as code only); when run from a build folder, next to the program.
/// </summary>
public static class AppPaths
{
    public static string Assets { get; } = Find();

    private static string Find()
    {
        var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Assets"));
        return Directory.Exists(bundled) ? bundled : Path.Combine(AppContext.BaseDirectory, "Assets");
    }
}
