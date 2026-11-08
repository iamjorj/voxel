using System.Diagnostics;

namespace VoiceChanger.Mac;

/// <summary>
/// Installs VB-CABLE on a Mac: runs install-vbcable.sh as root through the normal macOS password
/// prompt, then waits for the device to show up.
/// </summary>
public static class MacVbCableInstaller
{
    public const string PackageName = "VBCable_MACDriver_Pack108.zip";
    public const string ScriptName = "install-vbcable.sh";

    /// <summary>True when VB-Cable is there afterwards. Throws <see cref="OperationCanceledException"/> if the user cancels the password prompt.</summary>
    public static async Task<bool> InstallAsync(string folder)
    {
        var script = Path.Combine(folder, ScriptName);
        var package = Path.Combine(folder, PackageName);
        if (!File.Exists(script) || !File.Exists(package))
            throw new FileNotFoundException("The VB-CABLE package is missing from Voxel.", package);

        // Paths go in as arguments and through `quoted form of`, so nothing in them can break the command.
        var start = new ProcessStartInfo("/usr/bin/osascript") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[]
                 {
                     "-e", "on run argv",
                     "-e", "do shell script \"/bin/sh \" & quoted form of item 1 of argv & \" \" & quoted form of item 2 of argv " +
                           "with prompt \"Voxel wants to install its virtual microphone, VB-CABLE.\" with administrator privileges",
                     "-e", "end run",
                     script, package,
                 })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Couldn't start the installer.");
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            if (errors.Contains("-128")) throw new OperationCanceledException(); // "User canceled."
            throw new InvalidOperationException(errors.Trim().Length > 0 ? errors.Trim() : $"The installer stopped (code {process.ExitCode}).");
        }

        // Core Audio restarts after the install and takes a moment to list the new device.
        for (var i = 0; i < 20 && MacAudioDevices.Cable() == null; i++)
            await Task.Delay(500);
        return MacAudioDevices.Cable() != null;
    }
}
