using VoiceChanger.Mac;

namespace Voxel.Mac;

/// <summary>Remembers whether the welcome flow was finished, so it only shows once.</summary>
public static class Setup
{
    private static readonly string FlagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxel", "setup-complete");

    public static bool IsComplete => File.Exists(FlagPath);

    public static void MarkComplete()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FlagPath)!);
        File.WriteAllText(FlagPath, DateTime.UtcNow.ToString("O"));
    }

    public static void Reset()
    {
        if (File.Exists(FlagPath)) File.Delete(FlagPath);
    }

    /// <summary>The bundled VB-CABLE package and its install script.</summary>
    public static string VbCableFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "vbcable");

    // Only a Mac has Core Audio; anywhere else (the screenshot tool) there is simply no cable.
    public static bool IsCableInstalled => OperatingSystem.IsMacOS() && MacAudioDevices.Cable() != null;
}
