using System.IO;

namespace Voxel;

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

    /// <summary>Full path of the bundled VB-CABLE setup program.</summary>
    public static string VbCableSetupPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "vbcable", VoiceChanger.Core.VbCableInstaller.SetupExeName);
}
