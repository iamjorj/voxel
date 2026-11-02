using System.Diagnostics;

namespace VoiceChanger.Core;

/// <summary>
/// Runs the bundled VB-CABLE setup (silent, elevated) and undoes the two things its installer
/// changes that we don't want: it makes its own devices the default speakers and microphone.
/// </summary>
public static class VbCableInstaller
{
    public const string SetupExeName = "VBCABLE_Setup_x64.exe";

    /// <summary>
    /// Installs VB-CABLE from <paramref name="setupExePath"/>. Windows shows a UAC prompt and a
    /// one-time "install this device software?" prompt; both need the user's click.
    /// Returns true when the CABLE devices are present afterwards.
    /// </summary>
    public static async Task<bool> InstallAsync(string setupExePath)
    {
        if (!File.Exists(setupExePath))
            throw new FileNotFoundException("VB-CABLE setup not found.", setupExePath);

        var info = new ProcessStartInfo(setupExePath, "-i -h")
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(setupExePath),
        };

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not start the VB-CABLE setup.");
        await process.WaitForExitAsync();

        // Devices appear a moment after the setup exits.
        for (var i = 0; i < 10 && !AudioDevices.IsVbCableInstalled(); i++)
            await Task.Delay(500);

        var installed = AudioDevices.IsVbCableInstalled();
        if (installed)
        {
            DefaultMicSwitcher.RepairSpeakersIfHijacked();
            DefaultMicSwitcher.RepairMicIfHijacked();
        }
        return installed;
    }
}
